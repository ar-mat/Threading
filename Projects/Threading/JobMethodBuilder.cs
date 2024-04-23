using System;
using System.Runtime.CompilerServices;

namespace Armat.Threading
{
	public interface IJobMethodBuilderAwaiter
	{
		Job Job { get; }
		void MethodBuilderOnCompleted(Job MethodBuilderJob);
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "<Pending>")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
	public struct JobMethodBuilder
	{
		private static AsyncTaskMethodBuilder _tplAsyncTaskMethodBuilder = AsyncTaskMethodBuilder.Create();

		private ResultJob _job;
		private JobRuntimeContext _runtimeContext;
		private Boolean _runtimeContextCaptured;

		public static JobMethodBuilder Create()
		{
			// create the method builder
			return new JobMethodBuilder();
		}

		public void Start<TStateMachine>(ref TStateMachine stateMachine)
			where TStateMachine : IAsyncStateMachine
		{
			using var context = new JobMethodBuilderContext();

			// it will just run the first stage of async Method and will restore 
			// initial ExecutionContext and SynchronizationContext once complete
			// it actually calls "stateMachine.MoveNext()" and ensures to restore the thread context once it returns
			_tplAsyncTaskMethodBuilder.Start(ref stateMachine);
		}

		public void SetStateMachine(IAsyncStateMachine stateMachine)
		{
			// this is an obsolete function, should never be called
			_tplAsyncTaskMethodBuilder.SetStateMachine(stateMachine);
		}

		public void SetException(Exception exception)
		{
			ResultJob job = GetOrCreateResult();

			job.AppendException(exception);
			job.UpdateStatus(JobStatus.Faulted);
			job.SignalCompletion();

			job.ExecuteContinuations();
		}

		public void SetResult()
		{
			ResultJob job = GetOrCreateResult();

			job.UpdateStatus(JobStatus.RanToCompletion);
			job.SignalCompletion();

			job.ExecuteContinuations();
		}

		public void AwaitOnCompleted<TAwaiter, TStateMachine>(
			ref TAwaiter awaiter, ref TStateMachine stateMachine)
			where TAwaiter : INotifyCompletion
			where TStateMachine : IAsyncStateMachine
		{
			ResultJob result = GetOrCreateResult();
			result.StateMachine = stateMachine;

			if (awaiter is IJobMethodBuilderAwaiter jobMethodBuilderAwaiter)
			{
				// do not allocate another job, just run this same job on every iteration
				// ensure to reset the status, so the job scheduler would run it as a new
				result.ResetStatus();
				result.UpdateJobScheduler(jobMethodBuilderAwaiter.Job);
				result.UpdateRuntimeContext(CaptureRuntimeContext());

				jobMethodBuilderAwaiter.MethodBuilderOnCompleted(result);
			}
			else
			{
				// use the default continuation
				result.CaptureContext();
				awaiter.OnCompleted(result.MoveToNextStage);
			}
		}

		public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
			ref TAwaiter awaiter, ref TStateMachine stateMachine)
			where TAwaiter : ICriticalNotifyCompletion
			where TStateMachine : IAsyncStateMachine
		{
			ResultJob result = GetOrCreateResult();
			result.StateMachine = stateMachine;

			if (awaiter is IJobMethodBuilderAwaiter jobMethodBuilderAwaiter)
			{
				// do not allocate another job, just run this same job on every iteration
				// ensure to reset the status, so the job scheduler would run it as a new
				result.ResetStatus();
				result.UpdateJobScheduler(jobMethodBuilderAwaiter.Job);
				result.UpdateRuntimeContext(CaptureRuntimeContext());

				jobMethodBuilderAwaiter.MethodBuilderOnCompleted(result);
			}
			else
			{
				// use the default continuation
				result.CaptureContext();
				awaiter.UnsafeOnCompleted(result.MoveToNextStage);
			}
		}

		public Job Task
		{
			get
			{
				return GetOrCreateResult();
			}
		}

		private ResultJob GetOrCreateResult()
		{
			if (_job == null)
				_job = new ResultJob();

			return _job;
		}

		private JobRuntimeContext CaptureRuntimeContext()
		{
			// ensure to do it only once
			if (_runtimeContextCaptured)
				return _runtimeContext;

			// capture current job context or thread context
			Job? currentJob = Job.Current;
			if (currentJob != null)
				_runtimeContext.Capture(currentJob.RuntimeContext);
			else
				_runtimeContext.Capture();

			// mark it captured
			_runtimeContextCaptured = true;

			return _runtimeContext;
		}

		private class ResultJob : Job
		{
			public ResultJob()
				: base(JobCreationOptions.RunSynchronously | JobCreationOptions.DenyChildAttach |
				(JobCreationOptions)JOB_METHODBUILDERRESULT_FLAG | (JobCreationOptions)JOB_LIGHTWEIGHT_FLAG)
			{
				// it was not possible to set the state to "this", so we have to update the
				// AsyncState property within the constructor
			}

			public IAsyncStateMachine StateMachine
			{
				set
				{
					if (Procedure == (Delegate)NoAction)
					{
						Procedure = (Action)value.MoveNext;
					}
					else
					{
						System.Diagnostics.Debug.Assert(((Action)Procedure).Target == value);
					}
				}
				get
				{
					return (IAsyncStateMachine)((Action)Procedure)?.Target!;
				}
			}

			public void UpdateJobScheduler(Job job)
			{
				if (Scheduler != null)
					return;

				Scheduler = job?.Scheduler;
			}

			public void UpdateRuntimeContext(JobRuntimeContext jobRuntimeContext)
			{
				RuntimeContext = jobRuntimeContext;
			}

			protected override void ExecuteProcedureImpl()
			{
				// this will call StateMachine.MoveNext without updating the Status
				// Status will be set by the JobMethodBuilder either
				// through SerResult or through SetException method
				MoveToNextStage();
			}

			protected override void RegisterContinuation(Job job, JobContinuationOptions extraOptions)
			{
				if (job == null)
					throw new ArgumentNullException(nameof(job));

				// run the only method builder job continuation synchronously
				// it will run as a continuation of the last step in the MethodBuilder
				// and will run in the same thread. No need to create another job for this.
				extraOptions |= JobContinuationOptions.RunSynchronously;

				base.RegisterContinuation(job, extraOptions);
			}

			public void CaptureContext()
			{
				// AsyncState is used to store Awaiter Configuration to be used for executing the action.
				// See implementation of ExecuteAwaitContinuationImpl method for more details
				AwaiterConfiguration config = new(this);
				config.CaptureExecutionContext();

				AsyncState = config;
			}

			public void MoveToNextStage()
			{
				// Invoke the delegate
				System.Diagnostics.Debug.Assert(StateMachine != null, "Null StateMachine in ExecuteProcedureImpl()");

				using var context = new JobMethodBuilderContext();

				// Calling the Procedure will actually execute StateMachine.MoveNext
				// AwaiterConfiguration from AsyncState is used to embrace
				//   the call of Action = MoveToNextStage within caller Execution Context
				ExecuteAction((Action)Procedure, AsyncState as AwaiterConfiguration, true);
			}
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "<Pending>")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "<Pending>")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "<Pending>")]
	public struct JobMethodBuilderT<TResult>
	{
		private static AsyncTaskMethodBuilder<TResult> _tplAsyncTaskMethodBuilder = AsyncTaskMethodBuilder<TResult>.Create();

		private ResultJob _job;
		private JobRuntimeContext _runtimeContext;
		private Boolean _runtimeContextCaptured;

		public static JobMethodBuilderT<TResult> Create()
		{
			// create the method builder
			return new JobMethodBuilderT<TResult>();
		}

		public void Start<TStateMachine>(ref TStateMachine stateMachine)
			where TStateMachine : IAsyncStateMachine
		{
			using var context = new JobMethodBuilderContext();

			// it will just run the first stage of async Method and will restore 
			// initial ExecutionContext and SynchronizationContext once complete
			// it actually calls "stateMachine.MoveNext()" and ensures to restore the thread context once it returns
			_tplAsyncTaskMethodBuilder.Start(ref stateMachine);
		}

		public void SetStateMachine(IAsyncStateMachine stateMachine)
		{
			// this is an obsolete function, should never be called
			_tplAsyncTaskMethodBuilder.SetStateMachine(stateMachine);
		}

		public void SetException(Exception exception)
		{
			ResultJob job = GetOrCreateResult();

			job.AppendException(exception);
			job.UpdateStatus(JobStatus.Faulted);
			job.SignalCompletion();

			job.ExecuteContinuations();
		}

		public void SetResult(TResult result)
		{
			ResultJob job = GetOrCreateResult();

			job.SetResult(result);
			job.UpdateStatus(JobStatus.RanToCompletion);
			job.SignalCompletion();

			job.ExecuteContinuations();
		}

		public void AwaitOnCompleted<TAwaiter, TStateMachine>(
			ref TAwaiter awaiter, ref TStateMachine stateMachine)
			where TAwaiter : INotifyCompletion
			where TStateMachine : IAsyncStateMachine
		{
			ResultJob result = GetOrCreateResult();
			result.StateMachine = stateMachine;

			if (awaiter is IJobMethodBuilderAwaiter jobMethodBuilderAwaiter)
			{
				// do not allocate another job, just run this same job on every iteration
				// ensure to reset the status, so the job scheduler would run it as a new
				result.ResetStatus();
				result.UpdateJobScheduler(jobMethodBuilderAwaiter.Job);
				result.UpdateRuntimeContext(CaptureRuntimeContext());

				jobMethodBuilderAwaiter.MethodBuilderOnCompleted(result);
			}
			else
			{
				// use the default continuation
				result.CaptureContext();
				awaiter.OnCompleted(result.MoveToNextStage);
			}
		}

		public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
			ref TAwaiter awaiter, ref TStateMachine stateMachine)
			where TAwaiter : ICriticalNotifyCompletion
			where TStateMachine : IAsyncStateMachine
		{
			ResultJob result = GetOrCreateResult();
			result.StateMachine = stateMachine;

			if (awaiter is IJobMethodBuilderAwaiter jobMethodBuilderAwaiter)
			{
				// do not allocate another job, just run this same job on every iteration
				// ensure to reset the status, so the job scheduler would run it as a new
				result.ResetStatus();
				result.UpdateJobScheduler(jobMethodBuilderAwaiter.Job);
				result.UpdateRuntimeContext(CaptureRuntimeContext());

				jobMethodBuilderAwaiter.MethodBuilderOnCompleted(result);
			}
			else
			{
				// use the default continuation
				result.CaptureContext();
				awaiter.UnsafeOnCompleted(result.MoveToNextStage);
			}
		}

		public Job<TResult> Task
		{
			get
			{
				return GetOrCreateResult();
			}
		}

		private ResultJob GetOrCreateResult()
		{
			if (_job == null)
				_job = new ResultJob();

			return _job;
		}

		private JobRuntimeContext CaptureRuntimeContext()
		{
			// ensure to do it only once
			if (_runtimeContextCaptured)
				return _runtimeContext;

			// capture current job context or thread context
			Job? currentJob = Job.Current;
			if (currentJob != null)
				_runtimeContext.Capture(currentJob.RuntimeContext);
			else
				_runtimeContext.Capture();

			// mark it captured
			_runtimeContextCaptured = true;

			return _runtimeContext;
		}

		private class ResultJob : Job<TResult>
		{
			public ResultJob()
				: base(JobCreationOptions.RunSynchronously | JobCreationOptions.DenyChildAttach |
				(JobCreationOptions)JOB_METHODBUILDERRESULT_FLAG | (JobCreationOptions)JOB_LIGHTWEIGHT_FLAG)
			{
				// it was not possible to set the state to "this", so we have to update the
				// AsyncState property within the constructor
			}

			public IAsyncStateMachine StateMachine
			{
				set
				{
					if (Procedure == (Delegate)NoFunction)
					{
						Procedure = (Action)value.MoveNext;
					}
					else
					{
						System.Diagnostics.Debug.Assert(((Action)Procedure).Target == value);
					}
				}
				get
				{
					return (IAsyncStateMachine)((Action)Procedure)?.Target!;
				}
			}

			public void UpdateJobScheduler(Job job)
			{
				if (Scheduler != null)
					return;

				Scheduler = job?.Scheduler;
			}

			public void UpdateRuntimeContext(JobRuntimeContext jobRuntimeContext)
			{
				RuntimeContext = jobRuntimeContext;
			}

			protected override void ExecuteProcedureImpl()
			{
				// this will call StateMachine.MoveNext without updating the Status
				// Status will be set by the JobMethodBuilder either
				// through SerResult or through SetException method
				MoveToNextStage();
			}

			protected override void RegisterContinuation(Job job, JobContinuationOptions extraOptions)
			{
				if (job == null)
					throw new ArgumentNullException(nameof(job));

				// run the only method builder job continuation synchronously
				// it will run as a continuation of the last step in the MethodBuilder
				// and will run in the same thread. No need to create another job for this.
				extraOptions |= JobContinuationOptions.RunSynchronously;

				base.RegisterContinuation(job, extraOptions);
			}

			public void CaptureContext()
			{
				// AsyncState is used to store Awaiter Configuration to be used for executing the action.
				// See implementation of ExecuteAwaitContinuationImpl method for more details
				AwaiterConfiguration config = new(this);
				config.CaptureExecutionContext();

				AsyncState = config;
			}

			public void MoveToNextStage()
			{
				// Invoke the delegate
				System.Diagnostics.Debug.Assert(StateMachine != null, "Null StateMachine in ExecuteProcedureImpl()");

				using var context = new JobMethodBuilderContext();

				// Calling the Procedure will actually execute StateMachine.MoveNext
				// AwaiterConfiguration from AsyncState is used to embrace
				//   the call of Action = MoveToNextStage within caller Execution Context
				ExecuteAction((Action)Procedure, AsyncState as AwaiterConfiguration, true);
			}
		}
	}
}
