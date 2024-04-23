﻿using Armat.Collections;

using System;
using System.Collections.Generic;
using System.Threading;
using Armat.Utils.Extensions;
using System.Xml.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Armat.Threading
{
	// JobScheduler is the default implementation of IJobScheduler interface
	// It performs asynchronous execution of jobs with a dedicated thread pools for regular and long-running jobs respectively
	public class JobScheduler : JobSchedulerBase
	{
		public JobScheduler() : this(String.Empty, Default.MinThreads, Default.MaxThreads, Default.MaxLongRunningThreads)
		{
		}
		public JobScheduler(String name) : this(name, Default.MinThreads, Default.MaxThreads, Default.MaxLongRunningThreads)
		{
		}
		public JobScheduler(Int32 maxThreads, Int32 maxLongRunningThreads) : this(String.Empty, 0, maxThreads, maxLongRunningThreads)
		{
		}
		public JobScheduler(String name, Int32 minThreads, Int32 maxThreads, Int32 maxLongRunningThreads)
		{
			InitRuntimeDelegates();

			_minThreadCount = minThreads;

			Name = !String.IsNullOrEmpty(name) ? name : Guid.NewGuid().ToString();

			MaxThreads = maxThreads;
			MaxLongRunningThreads = maxLongRunningThreads;

			_poolThreads = new ConcurrentList<Thread>();
			_dedicatedThreads = new ConcurrentList<Thread>();
			_threadNameCounter = new Armat.Utils.Counter();

			_poolThreadsWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
			_longRunningThreadsWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);

			_jobsQueue = new JobsQueue();
			_jobsInPool = new System.Collections.Concurrent.ConcurrentDictionary<Job, JobStatus>();

			_statsCalculator = new JobSchedulerStatisticsCalculator();
		}

		protected override void Dispose(Boolean disposing)
		{
			if (disposing && !IsDisposed)
			{
				StopAllJobs();

				_statsCalculator.Dispose();

				_jobsInPool.Clear();
				_poolThreadsWaitHandle.Dispose();
				_longRunningThreadsWaitHandle.Dispose();

				_dedicatedThreads.Dispose();
				_poolThreads.Dispose();
			}

			base.Dispose(disposing);
		}

		private const String DEFAULT_NAME = "JS"; //"JobScheduler";
		private const Int32 POOL_THEADS_IDLE_TIMEOUT_MS = 1000;
		private const Int32 JOB_EXECUTION_WAIT_TIMEOUT_MS = 15000;

		private static readonly JobScheduler _default = new(DEFAULT_NAME, 0, Environment.ProcessorCount * 2, Environment.ProcessorCount);
		public static new JobScheduler Default
		{
			get => _default;
		}

		public String Name { get; }
		public Int32 MinThreads
		{
			get { return _minThreadCount; }
			set
			{
				if (value < 0)
					throw new ArgumentException("Number of threads in the scheduler must be non negative", nameof(value));
				if (value > MaxThreads)
					throw new ArgumentException("Minimum number of threads in the scheduler must not be more then MaxThreads", nameof(value));

				_minThreadCount = value;
			}
		}
		public Int32 MaxThreads
		{
			get { return _maxThreadCount; }
			set
			{
				if (value < 1)
					throw new ArgumentException("Number of threads in the scheduler must be positive", nameof(value));
				if (value < MinThreads)
					throw new ArgumentException("Maximum number of threads in the scheduler must not be less then MinThreads", nameof(value));

				_maxThreadCount = value;
			}
		}
		public Int32 MaxLongRunningThreads
		{
			get { return _maxLRThreadCount; }
			set
			{
				if (value < 0)
					throw new ArgumentException("Number of threads in the scheduler cannot be negative", nameof(value));

				_maxLRThreadCount = value;
			}
		}
		public Boolean IsStopped
		{
			get { return _isStopped; }
			private set { _isStopped = value; }
		}

		private readonly ConcurrentList<Thread> _poolThreads;
		private readonly ConcurrentList<Thread> _dedicatedThreads;
		private Int32 _minThreadCount = 0, _maxThreadCount = 0, _maxLRThreadCount = 0;
		private volatile Boolean _isStopped = false;
		private readonly Armat.Utils.Counter _threadNameCounter;

		private readonly EventWaitHandle _poolThreadsWaitHandle;
		private readonly EventWaitHandle _longRunningThreadsWaitHandle;

		// thread-safe queue of jobs
		private readonly JobsQueue _jobsQueue;
		// dictionary of jobs to their status values
		// this dictionary is needed to associate statuses with job instances
		// Note: status in this dictionary may not match the one in the Job instance
		private readonly System.Collections.Concurrent.ConcurrentDictionary<Job, JobStatus> _jobsInPool;

		// job statistics
		private readonly JobSchedulerStatisticsCalculator _statsCalculator;

		public override Int32 PendingJobsCount
		{
			get => _jobsQueue.Count;
		}

		public JobSchedulerStatistics Statistics => _statsCalculator.JobStatistics;
		public JobSchedulerStatistics MethodBuilderStatistics => _statsCalculator.MethodBuilderStatistics;

		public override void Enqueue(Job job)
		{
			if (IsStopped)
				throw new ObjectDisposedException("this");
			if (job.Scheduler != this)
				throw new ArgumentException("Job scheduler is not set correctly", nameof(job));

			// enqueue the job
			if (!UpdateJobStatus(job, JobStatus.WaitingForActivation, JobStatus.Created))
				throw new ArgumentException("The specified job is already started", nameof(job));

			if ((job.CreationOptions & JobCreationOptions.RunSynchronously) == JobCreationOptions.RunSynchronously)
			{
				// just run it in the calling thread
				RunJobSynchronously(job);
			}
			else if ((job.CreationOptions & JobCreationOptions.LongRunning) == JobCreationOptions.LongRunning)
			{
				// create / register a dedicated thread for this job
				StartLongRunningJob(job);
			}
			else
			{
				// enqueue the job for further processing in the thread pool
				EnqueueAsyncJob(job);
			}
		}

		public override Boolean Cancel(Job job)
		{
			if (IsStopped)
				throw new ObjectDisposedException("this");

			// dequeue the job
			if (!UpdateJobStatus(job, JobStatus.Canceled, JobStatus.WaitingForActivation))
				return false;

			return true;
		}

		private Thread CreateThread(Boolean dedicated)
		{
			ParameterizedThreadStart threadProc;
			String threadName;
			String threadCounter = _threadNameCounter.Increment().ToString(System.Globalization.CultureInfo.InvariantCulture);

			if (dedicated)
			{
				threadProc = DedicatedThreadProc;
				threadName = Name + "_dedicated_" + threadCounter;
			}
			else
			{
				threadProc = ScheduleThreadProc;
				threadName = Name + "_scheduled_" + threadCounter;
			}

			Thread thread = new(threadProc)
			{
				// Thread unique name
				Name = threadName,

				// this will ensure to block the process from existing while the jobs are running
				IsBackground = true
			};

			return thread;
		}

		protected Boolean UpdateJobStatus(Job job, JobStatus newStatus, JobStatus prevStatus)
		{
			// this will update the job status only if newStatus != prevStatus
			// we pass newStatus == prevStatus only in case when the job has completed the execution and 
			// the only cause we're calling this API is to remove completed jobs from the pool
			Boolean result = (newStatus == prevStatus);
			if (!result)
				result = job.UpdateStatus(newStatus, prevStatus);

			switch (newStatus)
			{
				case JobStatus.WaitingForActivation:
					// this status is set by JobScheduler when enqueueing the Job
					// enqueue the job

					if (result && _jobsInPool.TryAdd(job, JobStatus.WaitingForActivation))
					{
						// update statistics
						_statsCalculator.Queued(job);
					}
					else
					{
						// we are allowed to enqueue the job if it's not yet available in the pool
						result = false;
					}
					break;

				case JobStatus.WaitingToRun:
					// this status is set by JobScheduler or the Job itself before starting the execution
					// update the job status in pool

					if (_jobsInPool.TryUpdate(job, newStatus, prevStatus))
					{
						// update statistics
						_statsCalculator.Started(job);
					}
					else
					{
						// when updating to any state we should ensure the previous state is correct
						result = false;
					}

					break;

				case JobStatus.Running:
				case JobStatus.WaitingForChildrenToComplete:
					// job Status in the scheduler pool is never set to Running or WaitingForChildrenToComplete
					// it's the Job itself updating the status when running itself or attached children
					// and afterwards it goes to one of the completion statuses (RanToCompletion, Canceled or Faulted)
					System.Diagnostics.Debug.Assert(false, $"Can't set JobStatus = {newStatus} in {this.GetType().FullName}");
					result = false;
					break;

				case JobStatus.RanToCompletion:
					// this state is set from the Job itself after successful run
					// remove the job from the pool

					if (_jobsInPool.TryRemove(job, out prevStatus))
					{
						// update statistics
						if (prevStatus == JobStatus.WaitingForActivation)
						{
							// considering that the JobStatus.WaitingToRun status might be set by the Job itself,
							// _statsCalculator may not be updated to the current Started status.
							// It's important to mark it started before moving to the completion state
							// so that _statsCalculator state machine could rely on correct status transitions.
							_statsCalculator.Started(job);
						}
						_statsCalculator.Succeeded(job);
					}
					else
					{
						// it's not available in the pool any longer
						result = false;
					}

					break;

				case JobStatus.Canceled:
					// this status is set by JobScheduler or the Job itself before the execution is started or during the run
					// remove the job

					// when updating to any state we should ensure the previous state is correct
					if (!_jobsInPool.TryRemove(job, out _))
					{
						// update statistics
						if (prevStatus == JobStatus.WaitingForActivation)
						{
							// considering that the JobStatus.WaitingToRun status might be set by the Job itself,
							// _statsCalculator may not be updated to the current Started status.
							// It's important to mark it started before moving to the completion state
							// so that _statsCalculator state machine could rely on correct status transitions.
							_statsCalculator.Started(job);
						}
						_statsCalculator.Canceled(job);
					}
					else
					{
						// it's not available in the pool any longer
						result = false;
					}

					break;

				case JobStatus.Faulted:
					// remove the job

					if (_jobsInPool.TryRemove(job, out prevStatus))
					{
						// update statistics
						if (prevStatus == JobStatus.WaitingForActivation)
						{
							// considering that the JobStatus.WaitingToRun status might be set by the Job itself,
							// _statsCalculator may not be updated to the current Started status.
							// It's important to mark it started before moving to the completion state
							// so that _statsCalculator state machine could rely on correct status transitions.
							_statsCalculator.Started(job);
						}
						_statsCalculator.Faulted(job);
					}
					else
					{
						// it's not available in the pool any longer
						result = false;
					}

					break;

				default:
					// none of these statuses can be set here
					System.Diagnostics.Debug.Assert(false, $"Can't set JobStatus = {newStatus} in {this.GetType().FullName}");
					result = false;
					break;
			}

			return result;
		}

		private Boolean RunJobSynchronously(Job job)
		{
			// No need to take care of continuations here, those will be queued with higher priority upon submission
			return RunJobCore(job);
		}

		private void StartLongRunningJob(Job job)
		{
			if (MaxLongRunningThreads <= 0)
				throw new NotSupportedException("jobScheduler does not support execution of long running jobs");

			Thread? thread = null;
			while (!IsStopped)
			{
				thread = _dedicatedThreads.AddIf(_fnLongRunningThreadAllocator, _fnLongRunningThreadAllocatorCondition);
				if (thread != null)
					break;

				// reset the wait handle to wait for the next thread creation slot
				_longRunningThreadsWaitHandle.Reset();

				// check if there's anything changed (i.e. it the scheduler stopped)
				if (IsStopped)
				{
					_longRunningThreadsWaitHandle.Set();
					break;
				}

				// check if there's anything changed (i.e. there's a now slot for the thread)
				thread = _dedicatedThreads.AddIf(_fnLongRunningThreadAllocator, _fnLongRunningThreadAllocatorCondition);
				if (thread != null)
				{
					_longRunningThreadsWaitHandle.Set();
					break;
				}

				// wait for a new available slot for a new thread
				_longRunningThreadsWaitHandle.WaitOne();
			}

			if (thread != null)
			{
				// start the thread
				thread.Start(job);
			}
			else
			{
				// cancel the job
				UpdateJobStatus(job, JobStatus.Canceled, JobStatus.WaitingForActivation);
			}
		}

		private void EnqueueAsyncJob(Job job)
		{
			_jobsQueue.Enqueue(job);

			// signal the threads to start processing jobs
			_poolThreadsWaitHandle.Set();

			// ensure that necessary number of pool threads are running
			AllocateThreadsIfNecessary();
		}

		private Boolean RunJobCore(Job job)
		{
			// check if the job has been dequeued before this call (already ran or canceled)
			if (!UpdateJobStatus(job, JobStatus.WaitingToRun, JobStatus.WaitingForActivation))
				return false;

			// run the job
			JobStatus newStatus = ExecuteJobProcedure(job);

			// check if the job has been dequeued before this call (already ran or canceled)
			if (!UpdateJobStatus(job, newStatus, newStatus))
				return false;

			// run the continuations
			ExecuteJobContinuations(job);

			return true;
		}

		private void DedicatedThreadProc(Object? param)
		{
			RunJobCore((Job)param!);

			// remove the current thread once it's completed
			_dedicatedThreads.Remove(Thread.CurrentThread);

			// unblock start of any waiting long running jobs
			_longRunningThreadsWaitHandle.Set();
		}

		private void ScheduleThreadProc(Object? param)
		{
			Boolean stopThisThread = false;

			while (!stopThisThread && !IsStopped)
			{
				Boolean waitTimedOut = !_poolThreadsWaitHandle.WaitOne(POOL_THEADS_IDLE_TIMEOUT_MS);
				if (IsStopped)
					break;

				// try to dequeue and run a job
				Job? job = _jobsQueue.Dequeue();
				if (job != null)
				{
					// Run the job
					RunJobCore(job);
					continue;
				}

				// check if there's no job left in the queue
				_poolThreadsWaitHandle.Reset();

				if (!_jobsQueue.IsEmpty)
				{
					// verify that resetting the wait handle was correct
					_poolThreadsWaitHandle.Set();
					continue;
				}

				// check if the thread should be stopped
				if (waitTimedOut)
					stopThisThread = ReleaseCurrentThreadIfNecessary();

				if (!_jobsQueue.IsEmpty)
				{
					// this will ensure existence of threads in the scheduler to process the left requests
					if (stopThisThread)
						AllocateThreadsIfNecessary();
				}
			}

			// reset all queued job statuses to canceled
			if (IsStopped)
			{
				Job? job;
				while ((job = _jobsQueue.Dequeue()) != null)
				{
					UpdateJobStatus(job, JobStatus.Canceled, JobStatus.WaitingForActivation);
				}
			}

			// remove current thread once done (if not already removed)
			_poolThreads.Remove(Thread.CurrentThread);
		}

		private Int32 AllocateThreadsIfNecessary()
		{
			Thread? thread = null;
			Int32 addedThreads = 0;

			do
			{
				thread = _poolThreads.AddIf(_fnScheduledThreadAllocator, _fnScheduledThreadAllocatorCondition);

				// start the thread
				if (thread != null)
				{
					thread.Start();
					addedThreads++;
				}
			} while (thread != null);

			return addedThreads;
		}

		private Boolean ReleaseCurrentThreadIfNecessary()
		{
			Boolean removed = false;

			// check if the job scheduler is stopped
			if (IsStopped)
			{
				_poolThreads.Remove(Thread.CurrentThread);
				removed = true;
			}

			// remove if the MaxThreads limit is exceeded
			if (!removed && _poolThreads.Count > MaxThreads)
			{
				removed = _poolThreads.RemoveIf(Thread.CurrentThread, _fnScheduledThreadMaxThresholdCondition);
			}

			// if the queue of jobs is empty consider releasing the current thread
			if (!removed && _jobsQueue.IsEmpty)
			{
				removed = _poolThreads.RemoveIf(Thread.CurrentThread, _fnScheduledThreadMinThresholdCondition);
			}

			return removed;
		}

		private void StopAllJobs()
		{
			if (IsDisposed)
				return;

			// set the disposed flag
			IsStopped = true;

			// unblock any waiting threads
			_minThreadCount = 0;
			_poolThreadsWaitHandle.Set();
			_longRunningThreadsWaitHandle.Set();

			// wait for all threads to exit
			System.Diagnostics.Stopwatch sw = new();

			// wait for pool threads
			Thread[] arrPoolThreads = _poolThreads.ToArray();
			foreach (Thread thread in arrPoolThreads)
			{
				Int32 elapsedMS = (Int32)sw.ElapsedMilliseconds;
				Int32 maxTimeToWait = JOB_EXECUTION_WAIT_TIMEOUT_MS - elapsedMS;
				if (maxTimeToWait > 0)
					thread.Join(maxTimeToWait);
				else
					break;
			}

			// wait for dedicated threads
			Thread[] arrDedicatedThreads = _dedicatedThreads.ToArray();
			foreach (Thread thread in arrDedicatedThreads)
			{
				Int32 elapsedMS = (Int32)sw.ElapsedMilliseconds;
				Int32 maxTimeToWait = JOB_EXECUTION_WAIT_TIMEOUT_MS - elapsedMS;
				if (maxTimeToWait > 0)
					thread.Join(maxTimeToWait);
				else
					break;
			}
		}

		public override String ToString()
		{
			return Name;
		}

		#region Runtime delegates

		private Func<Thread> _fnScheduledThreadAllocator;
		private Func<Boolean> _fnScheduledThreadAllocatorCondition;
		private Func<Boolean> _fnScheduledThreadMaxThresholdCondition;
		private Func<Boolean> _fnScheduledThreadMinThresholdCondition;

		private Func<Thread> _fnLongRunningThreadAllocator;
		private Func<Boolean> _fnLongRunningThreadAllocatorCondition;

		[MemberNotNull(nameof(_fnScheduledThreadAllocator))]
		[MemberNotNull(nameof(_fnScheduledThreadAllocatorCondition))]
		[MemberNotNull(nameof(_fnScheduledThreadMaxThresholdCondition))]
		[MemberNotNull(nameof(_fnScheduledThreadMinThresholdCondition))]
		[MemberNotNull(nameof(_fnLongRunningThreadAllocator))]
		[MemberNotNull(nameof(_fnLongRunningThreadAllocatorCondition))]
		private void InitRuntimeDelegates()
		{
			_fnScheduledThreadAllocator = new Func<Thread>(ScheduledThreadAllocator);
			_fnScheduledThreadAllocatorCondition = new Func<Boolean>(ScheduledThreadAllocatorCondition);
			_fnScheduledThreadMaxThresholdCondition = new Func<Boolean>(ScheduledThreadMaxThresholdCondition);
			_fnScheduledThreadMinThresholdCondition = new Func<Boolean>(ScheduledThreadMinThresholdCondition);

			_fnLongRunningThreadAllocator = new Func<Thread>(LongRunningThreadAllocator);
			_fnLongRunningThreadAllocatorCondition = new Func<Boolean>(LongRunningThreadAllocatorCondition);
		}

		private Thread ScheduledThreadAllocator()
		{
			return CreateThread(false);
		}
		private Boolean ScheduledThreadAllocatorCondition()
		{
			// check if the pool max size is reached
			if (_poolThreads.Count >= MaxThreads)
				return false;
			if (_poolThreads.Count < MinThreads)
				return true;

			// get number of jobs currently running or waiting in the queue
			// and check if new threads are necessary to complete all of those
			Int32 incompleteJobs = _statsCalculator.IncompleteJobs;
			if (incompleteJobs > _poolThreads.Count)
				return true;

			return false;
		}
		private Boolean ScheduledThreadMaxThresholdCondition()
		{
			return _poolThreads.Count > MaxThreads;
		}
		private Boolean ScheduledThreadMinThresholdCondition()
		{
			return _poolThreads.Count > MinThreads;
		}

		private Thread LongRunningThreadAllocator()
		{
			return CreateThread(true);
		}
		private Boolean LongRunningThreadAllocatorCondition()
		{
			return _dedicatedThreads.Count < MaxLongRunningThreads;
		}

		#endregion // Runtime delegates

		#region Queue of Jobs

		private class JobsQueue
		{
			public JobsQueue()
			{
				_queue = new PriorityQueue<Job, Int32>();
				_lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
			}

			private PriorityQueue<Job, Int32> _queue;
			private ReaderWriterLockSlim _lock;

			public Boolean IsEmpty
			{
				get
				{
					using var rLock = _lock.CreateRLocker();
					return _queue.Count == 0;
				}
			}
			public Int32 Count
			{
				get
				{
					using var rLock = _lock.CreateRLocker();
					return _queue.Count;
				}
			}

			public Job? Dequeue()
			{
				Job? result = null;

				using var wLock = _lock.CreateWLocker();
				if (!_queue.TryDequeue(out result, out Int32 _))
					result = null;

				return result;
			}
			public void Enqueue(Job job)
			{
				using var wLock = _lock.CreateWLocker();
				_queue.Enqueue(job, job.Depth);
			}
		}

		#endregion // Queue of Jobs
	}
}