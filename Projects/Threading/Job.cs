using Armat.Collections;
using Armat.Utils;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

// Awaiter related resources
// https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/expressions#awaitable-expressions
// https://devblogs.microsoft.com/pfxteam/executioncontext-vs-synchronizationcontext/

// Method Builder related resources
// https://github.com/dotnet/roslyn/blob/master/docs/features/task-types.md


namespace Armat.Threading;

// Job - the central asynchronous execution unit of Armat.Threading library
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "<Pending>")]
[System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(JobMethodBuilder))]
public class Job : IAsyncResult, IDisposable
{
	#region Constructors

	public Job(Action action)
		: this(action, null, CancellationToken.None, JobCreationOptions.None, null, null)
	{
	}
	public Job(Action action, CancellationToken cancellationToken)
		: this(action, null, cancellationToken, JobCreationOptions.None, null, null)
	{
	}
	public Job(Action action, JobCreationOptions creationOptions)
		: this(action, null, CancellationToken.None, creationOptions, null, null)
	{
	}
	public Job(Action action, CancellationToken cancellationToken, JobCreationOptions creationOptions,
		IJobScheduler? scheduler)
		: this(action, null, cancellationToken, creationOptions, scheduler, null)
	{
	}

	public Job(Action<Object?> action, Object? state)
		: this(action, state, CancellationToken.None, JobCreationOptions.None, null, null)
	{
	}
	public Job(Action<Object?> action, Object? state, CancellationToken cancellationToken)
		: this(action, state, cancellationToken, JobCreationOptions.None, null, null)
	{
	}
	public Job(Action<Object?> action, Object? state, JobCreationOptions creationOptions)
		: this(action, state, CancellationToken.None, creationOptions, null, null)
	{
	}
	public Job(Action<Object?> action, Object? state, CancellationToken cancellationToken, 
		JobCreationOptions creationOptions, IJobScheduler? scheduler)
		: this(action, state, cancellationToken, creationOptions, scheduler, null)
	{
	}

	internal Job()
		: this(NoAction, null, CancellationToken.None, JobCreationOptions.None, null, null)
	{
	}
	internal Job(JobStatus status)
	{
		// Special constructor for JobStatus.Completed job
		// We're calling the generic constructor to initialize Id = 0
		Debug.Assert(status == JobStatus.RanToCompletion);
		Id = COMPLTETED_JOB_ID;

		Procedure = NoAction;
		AsyncState = null;
		CancellationToken = CancellationToken.None;
		_executionOptions = (Int32)JobCreationOptions.None;

		Scheduler = null;
		Initiator = null;

		_status = (Int32)status;
	}
	internal Job(JobCreationOptions creationOptions)
			: this(NoAction, null, CancellationToken.None, creationOptions, null, null)
	{
	}
	internal Job(CancellationToken cancellationToken)
		: this(NoAction, null, cancellationToken, JobCreationOptions.None, null, null)
	{
	}
	internal Job(Exception exc)
		: this(NoAction, null, CancellationToken.None, JobCreationOptions.None, null, null)
	{
		if (exc != null)
		{
			_status = (Int32)JobStatus.Faulted;
			this.AppendException(exc);
		}
	}
	internal Job(Delegate procedure, Object? state, CancellationToken cancellationToken, 
		JobCreationOptions creationOptions, IJobScheduler? scheduler, Job? initiator)
	{
		Id = Interlocked.Increment(ref _idCounter);

		Procedure = procedure;
		AsyncState = state;
		CancellationToken = cancellationToken;
		_executionOptions = (Int32)creationOptions;

		Scheduler = scheduler;
		Initiator = initiator;

		if (!cancellationToken.IsCancellationRequested)
			_status = (Int32)JobStatus.Created;
		else
			_status = (Int32)JobStatus.Canceled;
	}

	// No need to manually call the Dispose here as per the same description as below:
	// https://devblogs.microsoft.com/pfxteam/do-i-need-to-dispose-of-tasks/
	~Job()
	{
		Dispose(false);
	}
	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
	protected virtual void Dispose(Boolean disposing)
	{
		if (disposing)
		{
			if (_waitHandle != null)
			{
				_waitHandle.Dispose();
				_waitHandle = null;
			}
		}
	}

	#endregion // Constructors

	#region Data Members & Properties

	private const Int64 COMPLTETED_JOB_ID = 0;
	private static readonly Job _completedJob = new(JobStatus.RanToCompletion);

	[ThreadStatic]
	private static Job? _currentJob;
	public static Job? Current
	{
		get
		{
			return _currentJob;
		}
		private set
		{
			_currentJob = value;
		}
	}

	private const Int32 ALL_CREATION_OPTIONS = 0x8005F;
	private const Int32 ALL_CONTINUATION_OPTIONS = 0x8707F;
	private const Int32 JOB_CONTINUATION_FLAG = 0x0100;
	protected const Int32 JOB_LIGHTWEIGHT_FLAG = 0x0200;
	protected const Int32 JOB_METHODBUILDERRESULT_FLAG = 0x0400;

	private static Int64 _idCounter = COMPLTETED_JOB_ID;
	public Int64 Id { get; }

	protected Delegate Procedure { get; set; }
	public Object? AsyncState { get; protected set; }
	public CancellationToken CancellationToken { get; }

	private Int32 _executionOptions = 0;
	public JobCreationOptions CreationOptions
	{
		get { return (JobCreationOptions)(_executionOptions & ALL_CREATION_OPTIONS); }
	}
	protected JobContinuationOptions ContinuationOptions
	{
		get { return (JobContinuationOptions)(_executionOptions & ALL_CONTINUATION_OPTIONS); }
	}

	public IJobScheduler? Scheduler { get; protected set; }

	private Job? _initiator = null;
	public Job? Initiator
	{
		get { return _initiator; }
		private set
		{
#if DEBUG
			Job? initTester = value;
			while (initTester != null && initTester != this)
				initTester = initTester.Initiator;
			Debug.Assert(initTester != this);
#endif

			_initiator = value;

			if (_initiator == null)
				Depth = 0;
			else
				Depth = _initiator.Depth + 1;
		}
	}

	public Job Root
	{
		get
		{
			Job result = this;
			while (result._initiator != null)
				result = result._initiator;

			return result;
		}
	}
	public Int32 Depth
	{
		get; private set;
	}

	public Boolean IsContinuation
	{
		get { return (_executionOptions & JOB_CONTINUATION_FLAG) != 0; }
	}
	public Boolean IsMethodBuilderResult
	{
		get { return (_executionOptions & JOB_METHODBUILDERRESULT_FLAG) != 0; }
	}

	private IndigentList<Job>? _continuations = null;
	private Int32 _execContinuationsBaseIndex = -1;
	public IReadOnlyCollection<Job>? Continuations
	{
		get
		{
			return _continuations;
		}
	}
	public Boolean HasContinuations
	{
		get { return _continuations != null && _continuations.Count > 0; }
	}

	private Int32 _status;
	public JobStatus Status
	{
		get { return (JobStatus)_status; }
	}
	public Boolean IsCompleted
	{
		get { return Status >= JobStatus.RanToCompletion; }
	}
	public Boolean IsCompletedSuccessfully
	{
		get { return Status == JobStatus.RanToCompletion; }
	}
	public Boolean IsCanceled
	{
		get { return Status == JobStatus.Canceled; }
	}
	public Boolean IsFaulted
	{
		get { return Status == JobStatus.Faulted; }
	}
	public Boolean CompletedSynchronously
	{
		get { return IsCompleted && (CreationOptions & JobCreationOptions.RunSynchronously) == JobCreationOptions.RunSynchronously; }
	}

	private List<ExceptionDispatchInfo>? _listExceptions = null;
	private AggregateException? _exception = null;
	public AggregateException? Exception
	{
		get
		{
			// return the current exception
			AggregateException? exc = _exception;
			if (exc != null)
				return exc;

			List<ExceptionDispatchInfo>? listExceptions = _listExceptions;
			if (listExceptions == null)
				return null;

			// create the aggregate exception out of _listExceptions
			lock (listExceptions)
			{
				if (listExceptions.Count == 0)
				{
					exc = new AggregateException();
				}
				else
				{
					IEnumerable<Exception> exceptions = listExceptions.Select(edi => edi.SourceException);
					exc = new AggregateException(exceptions);
				}
				Interlocked.CompareExchange<AggregateException?>(ref _exception, exc, null);
			}

			return _exception;
		}
	}

	private ManualResetEventSlim? _waitHandle = null;
	public ManualResetEventSlim AsyncWaitHandleSlim
	{
		get
		{
			if (_waitHandle == null)
			{
				// create the wait handle
				Interlocked.CompareExchange<ManualResetEventSlim?>(ref _waitHandle, new ManualResetEventSlim(false), null);

				// set it signaling if already completed
				if (IsCompleted)
					_waitHandle.Set();
			}

			return _waitHandle;
		}
	}
	public WaitHandle AsyncWaitHandle
	{
		get
		{
			return AsyncWaitHandleSlim.WaitHandle;
		}
	}

	private JobRuntimeContext _runtimeContext = JobRuntimeContext.Empty;
	public JobRuntimeContext RuntimeContext
	{
		get
		{
			return _runtimeContext;
		}
		protected set
		{
			_runtimeContext = value;
		}
	}

	#endregion // Data Members & Properties

	#region Run

	public void Run()
	{
		// determine the job scheduler for the current job
		IJobScheduler? scheduler = Scheduler;
		if (scheduler == null)
		{
			if ((CreationOptions & JobCreationOptions.HideScheduler) == JobCreationOptions.HideScheduler)
			{
				// use the default scheduler irrespective of the current one
				scheduler = IJobScheduler.Default;
			}
			else
			{
				Job? owner = Initiator ?? Current;
				if (owner != null)
					scheduler = owner.Scheduler ?? IJobScheduler.Current;
				else
					scheduler = IJobScheduler.Current;
			}
		}

		Run(scheduler);
	}
	public void Run(IJobScheduler jobScheduler)
	{
		Job? detectedInitiator = Initiator;
		if (detectedInitiator == null)
		{
			// be careful assigning the initiator
			// MethodBuilder jobs can't be initiators, or we will end up with cyclic references within jobs
			// The same JobMethodBuilder.ResultJob instance is used as a continuation for all jobs in a given method
			detectedInitiator = Current;
			while (detectedInitiator != null && detectedInitiator.IsMethodBuilderResult)
				detectedInitiator = detectedInitiator.Initiator;
		}

		// set the initiator if not set yet
		// note: if the JobCreationOptions.HideScheduler flag is set, we'll consider this job as a new root
		if ((CreationOptions & JobCreationOptions.HideScheduler) != JobCreationOptions.HideScheduler)
		{
			Initiator ??= detectedInitiator;
		}

		// verify the scheduler
		if (Scheduler != null)
		{
			if (Scheduler != jobScheduler)
				throw new ArgumentException("Job scheduler is not set correctly", nameof(jobScheduler));
		}
		else
		{
			Scheduler = jobScheduler;
		}

		// set job execution scope
		// JobMethodBuilder runtime context is set in JobMethodBuilder class itself
		if (!IsMethodBuilderResult)
		{
			if (detectedInitiator != null)
				_runtimeContext.Capture(detectedInitiator._runtimeContext);
			else
				_runtimeContext.Capture();
		}

		if ((_executionOptions & JOB_LIGHTWEIGHT_FLAG) != JOB_LIGHTWEIGHT_FLAG)
		{
			// queue in the scheduler
			jobScheduler.Enqueue(this);
		}
		else
		{
			// just run the job procedure with
			//		no trace in the job scheduler
			//		no status checks
			//		no status updates
			//		no exception handling
			//		no continuation

			Job? prevCurrent = Current;
			try
			{
				Current = this;

				InvokeProcedureAction();
			}
			finally
			{
				Current = prevCurrent;
			}
		}
	}
	public void RunSynchronously()
	{
		// ensure it will run synchronously
		_executionOptions |= (Int32)JobCreationOptions.RunSynchronously;

		// will run it instantaneously
		Run();

		// throw immediately if there's an error
		ThrowIfCompletedUnexpectedly(true);
	}
	public void RunSynchronously(IJobScheduler jobScheduler)
	{
		// ensure it will run synchronously
		_executionOptions |= (Int32)JobCreationOptions.RunSynchronously;

		// will run it instantaneously
		Run(jobScheduler);

		// throw immediately if there's an error
		ThrowIfCompletedUnexpectedly(true);
	}
	public Boolean Cancel()
	{
		Boolean canceled = false;
		IJobScheduler? scheduler = Scheduler;

		if (scheduler != null)
		{
			if (scheduler.Cancel(this))
				canceled = true;
		}
		else
		{
			if (UpdateStatus(JobStatus.Canceled, JobStatus.Created))
				canceled = true;
		}

		return canceled;
	}

	internal protected void AppendException(Exception exc)
	{
		Debug.Assert(exc != null);

		if (_listExceptions == null)
			Interlocked.CompareExchange<List<ExceptionDispatchInfo>?>(ref _listExceptions, new List<ExceptionDispatchInfo>(), null);

		lock (_listExceptions)
		{
			_exception = null;
			if (exc is TransientAggregateException transientExc)
			{
				foreach (Exception excInner in transientExc.InnerExceptions)
					_listExceptions.Add(ExceptionDispatchInfo.Capture(excInner));
			}
			else
			{
				_listExceptions.Add(ExceptionDispatchInfo.Capture(exc));
			}
		}
	}
	internal protected JobStatus UpdateStatus(JobStatus newStatus)
	{
		return (JobStatus)Interlocked.Exchange(ref _status, (Int32)newStatus);
	}
	internal protected Boolean UpdateStatus(JobStatus newStatus, JobStatus prevStatus)
	{
		return Interlocked.CompareExchange(ref _status, (Int32)newStatus, (Int32)prevStatus) == (Int32)prevStatus;
	}
	internal protected virtual void ResetStatus()
	{
		_waitHandle?.Reset();

		List<ExceptionDispatchInfo>? listExceptions = _listExceptions;
		if (listExceptions != null)
		{
			lock (listExceptions)
			{
				if (listExceptions == _listExceptions)
				{
					listExceptions.Clear();

					_exception = null;
					_listExceptions = null;
				}
			}
		}

		_execContinuationsBaseIndex = -1;

		UpdateStatus(JobStatus.Created);
	}
	internal JobStatus ExecuteProcedure()
	{
		JobStatus result = JobStatus.Created;

		try
		{
			// execute the current procedure
			result = ExecuteProcedureCore();
		}
		catch (Exception /*exc*/) { }

		return result;
	}
	internal Int32 ExecuteContinuations()
	{
		Int32 result = 0;
		Job? prevJob = Current;
		Current = this;

		try
		{
			// execute continuations
			result = ExecuteContinuationsCore(Status);
		}
		catch (Exception /*exc*/) { }
		finally
		{
			Current = prevJob;
		}

		return result;
	}
	internal protected void SignalCompletion()
	{
		_waitHandle?.Set();
	}

	protected JobStatus ExecuteProcedureCore()
	{
		// attached continuations are called children
		JobStatus result = JobStatus.Running;
		Boolean canRunChildren = (CreationOptions & JobCreationOptions.DenyChildAttach) != JobCreationOptions.DenyChildAttach;

		// Run this job
		try
		{
			// check if the task is canceled before the run
			ThrowIfCompletedUnexpectedly(true);

			// set the status to running
			if (!UpdateStatus(JobStatus.Running, JobStatus.WaitingToRun))
				throw new InvalidOperationException("The job has already ran");

			// execute current job procedure
			if (!IsContinuation)
				ExecuteProcedureImpl();
			else
				ExecuteContinuationImpl();

			if (!canRunChildren)
			{
				// ensure to set the right error if any
				ThrowIfCompletedUnexpectedly(true);

				// mark it complete
				UpdateStatus(JobStatus.RanToCompletion);
				result = JobStatus.RanToCompletion;
			}
		}
		catch (OperationCanceledException /*exc*/)
		{
			// cancellation is not a fault. Do not set the exception.
			//AppendException(exc);
			UpdateStatus(JobStatus.Canceled);
			result = JobStatus.Canceled;
		}
		catch (Exception exc)
		{
			AppendException(exc);
			UpdateStatus(JobStatus.Faulted);
			result = JobStatus.Faulted;
		}

		// Run attached continuations job
		if (canRunChildren)
		{
			try
			{
				// run attached children before completion
				UpdateStatus(JobStatus.WaitingForChildrenToComplete);
				result = JobStatus.WaitingForChildrenToComplete;

				// execute continuations
				ExecuteContinuationsCore(JobStatus.WaitingForChildrenToComplete);

				// ensure to set the right error if any
				ThrowIfCompletedUnexpectedly(true);

				// mark it complete
				UpdateStatus(JobStatus.RanToCompletion);
				result = JobStatus.RanToCompletion;
			}
			catch (OperationCanceledException /*exc*/)
			{
				// cancellation is not a fault. Do not set the exception.
				UpdateStatus(JobStatus.Canceled);
				result = JobStatus.Canceled;
			}
			catch (Exception /*exc*/)
			{
				// Note: Do not update the exception here, exceptions of children should be already aggregated
				UpdateStatus(JobStatus.Faulted);
				result = JobStatus.Faulted;
			}
		}

		// if there's a waiter for completion, trigger it
		SignalCompletion();

		return result;
	}
	protected void ExecuteProcedureImpl()
	{
		Job? prevJob = Current;
		Current = this;
		try
		{
			InvokeProcedureAction();
		}
		finally
		{
			Current = prevJob;
		}
	}
	protected virtual void InvokeProcedureAction()
	{
		// Invoke the delegate
		Debug.Assert(Procedure != null, "Null Procedure in ExecuteProcedureImpl()");

		if (Procedure is Action action)
		{
			action();
			return;
		}

		if (Procedure is Action<Object?> actionWithState)
		{
			actionWithState(AsyncState);
			return;
		}

		Debug.Fail("Invalid Procedure in Job");
	}

	protected Int32 ExecuteContinuationsCore(JobStatus executionStatus)
	{
		// note: do not extend the continuation base index so those will be processed
		// later on upcoming ExecuteContinuations call
		Boolean extendContinuationBaseIndex = executionStatus >= JobStatus.RanToCompletion;
		IReadOnlyCollection<Job>? continuations = GetPendingContinuations(extendContinuationBaseIndex);
		if (continuations == null || continuations.Count == 0)
			return 0;

		// determine async and sync continuation jobs to run
		JobCreationOptions creationOptions = CreationOptions;
		IReadOnlyCollection<Job>? asyncJobs = null, syncJobs = null;
		Int32 ranJobCounter = 0;

		foreach (Job job in continuations)
		{
			// check if the job has already ran
			JobStatus continuationStatus = job.Status;
			if (continuationStatus != JobStatus.Created)
				continue;

			// check if cancellation is pending
			JobContinuationOptions continuationOptions = job.ContinuationOptions;

			// check if the continuation should run
			if (!CanRunContinuation(executionStatus, continuationOptions))
				continue;

			// in state of JobStatus.WaitingForChildrenToComplete we should only run attached children synchronously
			if (executionStatus == JobStatus.WaitingForChildrenToComplete)
			{
				// this condition should be checked in ExecuteProcedureCore() method before calling tis one
				Debug.Assert((creationOptions & JobCreationOptions.DenyChildAttach) != JobCreationOptions.DenyChildAttach);

				// only attached continuations (children) should be executed
				if ((continuationOptions & JobContinuationOptions.AttachedToParent) != JobContinuationOptions.AttachedToParent)
					continue;
			}
			else if (executionStatus < JobStatus.RanToCompletion)
			{
				// the call should not reach here in any other state
				throw new InvalidOperationException("Cannot run continuations of not completed task");
			}

			if ((continuationOptions & JobContinuationOptions.RunSynchronously) == JobContinuationOptions.RunSynchronously)
			{
				if (syncJobs == null)
				{
					if (continuations.Count == 1)
						syncJobs = continuations;						// minor optimization in case if there's only one job to run
					else
						syncJobs = new IndigentList<Job>() { job };     // create a new list for continuations
				}
				else
				{
					((IList<Job>)syncJobs).Add(job);						// add another continuation
				}
			}
			else
			{
				if (asyncJobs == null)
				{
					if (continuations.Count == 1)
						asyncJobs = continuations;               // minor optimization in case if there's only one job to run
					else
						asyncJobs = new IndigentList<Job>() { job };     // create a new list for continuations
				}
				else
				{
					((IList<Job>)asyncJobs).Add(job);             // add another continuation
				}
			}
		}

		// first of all enqueue jobs to run in parallel
		if (asyncJobs != null)
		{
			foreach (Job job in asyncJobs)
			{
				// check if the continuation should still run
				if (!CanRunContinuation(executionStatus, job.ContinuationOptions))
					continue;

				try
				{
					job.Run();
					ranJobCounter++;
				}
				catch (Exception exc)
				{
					AppendException(exc);
				}
			}
		}

		// next run synchronous jobs consequently
		if (syncJobs != null)
		{
			foreach (Job job in syncJobs)
			{
				// check if the continuation should still run
				if (!CanRunContinuation(executionStatus, job.ContinuationOptions))
					continue;

				try
				{
					job.Run();
					ranJobCounter++;
				}
				catch (Exception exc)
				{
					AppendException(exc);
				}
			}
		}

		// wait for async children to complete
		if (asyncJobs != null && executionStatus == JobStatus.WaitingForChildrenToComplete)
		{
			foreach (Job job in asyncJobs)
			{
				try
				{
					job.WaitNoThrow();
					job.ThrowIfCompletedUnexpectedly(true);

				}
				catch (Exception exc)
				{
					AppendException(exc);
				}
			}
		}

		return ranJobCounter;
	}
	protected void ExecuteContinuationImpl()
	{
		Job? prevJob = Current;
		Current = this;
		try
		{
			InvokeContinuationAction();
		}
		finally
		{
			Current = prevJob;
		}
	}
	protected virtual void InvokeContinuationAction()
	{
		// Invoke the delegate
		Debug.Assert(Initiator != null, "Null Initiator in Job.ExecuteContinuationImpl()");
		Debug.Assert(Procedure != null, "Null Procedure in Job.ExecuteContinuationImpl()");

		if (Procedure is Action awaitAction)
		{
			// See SetAwaiterContinuation(Action continuation, JobAwaiterConfiguration configuration) method
			InvokeAwaitContinuationAction(awaitAction);
			return;
		}

		if (Procedure is Action<Job> action)
		{
			// See ContinueWith(Action<Job> continuationAction, ...) methods
			action(Initiator);
			return;
		}

		if (Procedure is Action<Job, Object?> actionWithState)
		{
			// See ContinueWith(Action<Job, Object> continuationAction, Object state, ...) methods
			actionWithState(Initiator, AsyncState);
			return;
		}

		Debug.Fail("Invalid Procedure in Continuation Job");
	}
	protected virtual void InvokeAwaitContinuationAction(Action awaitAction)
	{
		ExecuteAwaitContinuation(awaitAction, AsyncState as AwaiterConfiguration,
			(CreationOptions & JobCreationOptions.RunSynchronously) == JobCreationOptions.RunSynchronously);
	}

	protected void ThrowIfCompletedUnexpectedly(Boolean throwInnerException)
	{
		if (CancellationToken.IsCancellationRequested || IsCanceled)
		{
			throw new OperationCanceledException();
		}
		else
		{
			List<ExceptionDispatchInfo>? listExceptions = _listExceptions;
			if (listExceptions != null)
			{
				lock (listExceptions)
				{
					if (throwInnerException && listExceptions.Count == 1)
						listExceptions[0].Throw();
					else
						throw Exception!;   // exception is never null when _listExceptions != null
				}
			}
		}
	}
	protected static void ExecuteAwaitContinuation(Action action, AwaiterConfiguration? configuration, Boolean runSynchronously)
	{
		if (action == null)
			throw new ArgumentNullException(nameof(action));

		if (configuration?.SynchronizationContext != null && configuration.SynchronizationContext != SynchronizationContext.Current)
		{
			Job? currentJob = Current;
			ThreadRuntimeContext? currentThreadContext = ThreadRuntimeContext.Current;

			Tuple<Action, Job?, ThreadRuntimeContext?> actionWithContext = new(action, currentJob, currentThreadContext);
			if (runSynchronously)
				configuration.SynchronizationContext.Send(_fnActionExecutorSendOrPostCallback, actionWithContext);
			else
				configuration.SynchronizationContext.Post(_fnActionExecutorSendOrPostCallback, actionWithContext);
		}
		else if (configuration?.ExecutionContext != null)
		{
			ExecutionContext.Run(configuration.ExecutionContext, _fnActionExecutorContextCallback, action);
		}
		else
		{
			action();
		}
	}
	private static void SynchronizationContextRunProc(Object? state)
	{
		if (state is Tuple<Action, Job?, ThreadRuntimeContext?> actionWithContext)
		{
			// save previous context
			Job? prevJob = Current;
			ThreadRuntimeContext? prevThreadContext = ThreadRuntimeContext.Current;

			try
			{
				Action action = actionWithContext.Item1;
				Job? currentJob = actionWithContext.Item2;
				ThreadRuntimeContext? currentThreadContext = actionWithContext.Item3;

				// set current context
				Current = currentJob;
				ThreadRuntimeContext.Current = currentThreadContext;

				// run the action
				action();
			}
			finally
			{
				// restore previous context
				Current = prevJob;
				ThreadRuntimeContext.Current = prevThreadContext;
			}
		}
		else
		{
			Debug.Assert(false, "ActionExecutorProc should be called with an \"Action\" parameter");
		}
	}
	private static void ExecutionContextRunProc(Object? action)
	{
		if (action is Action fn)
		{
			fn();
		}
		else
		{
			Debug.Assert(false, "ActionExecutorProc should be called with an \"Action\" parameter");
		}
	}
	private static readonly SendOrPostCallback _fnActionExecutorSendOrPostCallback = new(SynchronizationContextRunProc);
	private static readonly ContextCallback _fnActionExecutorContextCallback = new(ExecutionContextRunProc);

	private IReadOnlyCollection<Job>? GetPendingContinuations(Boolean extendContinuationsBaseIndex)
	{
		IndigentList<Job>? listContinuations = _continuations;
		if (listContinuations == null)
		{
			// this will mark continuations executed once
			if (extendContinuationsBaseIndex)
				_execContinuationsBaseIndex = 0;
			return null;
		}

		Job[]? arrContinuations = null;
		lock (listContinuations)
		{
			if (_execContinuationsBaseIndex <= 0)
			{
				// provide the whole array
				arrContinuations = listContinuations.ToArray();
			}
			else if (_execContinuationsBaseIndex < listContinuations.Count)
			{
				// copy only the pending part
				arrContinuations = new Job[listContinuations.Count - _execContinuationsBaseIndex];
				listContinuations.CopyTo(_execContinuationsBaseIndex, arrContinuations, 0, arrContinuations.Length);
			}

			// extend the continuation base index
			if (extendContinuationsBaseIndex)
				_execContinuationsBaseIndex = listContinuations.Count;
		}

		return arrContinuations;
	}
	private Boolean CanRunContinuation(JobStatus executionStatus, JobContinuationOptions continuationOptions)
	{
		if ((continuationOptions & JobContinuationOptions.NotOnRanToCompletion) == JobContinuationOptions.NotOnRanToCompletion)
		{
			if (executionStatus == JobStatus.RanToCompletion)
				return false;
		}
		if ((continuationOptions & JobContinuationOptions.NotOnCanceled) == JobContinuationOptions.NotOnCanceled)
		{
			if (executionStatus == JobStatus.Canceled || CancellationToken.IsCancellationRequested)
				return false;
		}
		if ((continuationOptions & JobContinuationOptions.NotOnFaulted) == JobContinuationOptions.NotOnFaulted)
		{
			if (executionStatus == JobStatus.Faulted || Exception != null)
				return false;
		}

		return true;
	}

	#endregion // Run

	#region Wait

	public void WaitNoThrow()
	{
		if (IsCompleted)
			return;

		AsyncWaitHandleSlim.Wait();
	}
	public Boolean WaitNoThrow(Int32 millisecondsTimeout)
	{
		if (IsCompleted)
			return true;

		return AsyncWaitHandleSlim.Wait(millisecondsTimeout);
	}
	public void WaitNoThrow(CancellationToken cancellationToken)
	{
		if (IsCompleted)
			return;

		if (!cancellationToken.CanBeCanceled)
		{
#pragma warning disable CA2016 // Forward the 'CancellationToken' parameter to methods that take one
			AsyncWaitHandleSlim.Wait();
#pragma warning restore CA2016 // Forward the 'CancellationToken' parameter to methods that take one
			return;
		}

		WaitHandle.WaitAny(new WaitHandle[] { AsyncWaitHandleSlim.WaitHandle, cancellationToken.WaitHandle });
	}
	public Boolean WaitNoThrow(Int32 millisecondsTimeout, CancellationToken cancellationToken)
	{
		if (IsCompleted)
			return true;

		if (!cancellationToken.CanBeCanceled)
		{
#pragma warning disable CA2016 // Forward the 'CancellationToken' parameter to methods that take one
			return AsyncWaitHandleSlim.Wait(millisecondsTimeout);
#pragma warning restore CA2016 // Forward the 'CancellationToken' parameter to methods that take one
		}

		return WaitHandle.WaitAny(new WaitHandle[] { AsyncWaitHandleSlim.WaitHandle, cancellationToken.WaitHandle }, millisecondsTimeout) != WaitHandle.WaitTimeout;
	}
	public Boolean WaitNoThrow(TimeSpan timeout)
	{
		if (IsCompleted)
			return true;

		return AsyncWaitHandleSlim.Wait(timeout);
	}

	public void Wait()
	{
		WaitNoThrow();

		ThrowIfCompletedUnexpectedly(false);
	}
	public Boolean Wait(Int32 millisecondsTimeout)
	{
		Boolean result = WaitNoThrow(millisecondsTimeout);

		ThrowIfCompletedUnexpectedly(false);

		return result;
	}
	public void Wait(CancellationToken cancellationToken)
	{
		WaitNoThrow(cancellationToken);

		ThrowIfCompletedUnexpectedly(false);
	}
	public Boolean Wait(Int32 millisecondsTimeout, CancellationToken cancellationToken)
	{
		Boolean result = WaitNoThrow(millisecondsTimeout, cancellationToken);

		ThrowIfCompletedUnexpectedly(false);

		return result;
	}
	public Boolean Wait(TimeSpan timeout)
	{
		Boolean result = WaitNoThrow(timeout);

		ThrowIfCompletedUnexpectedly(false);

		return result;
	}

	#endregion // Wait

	#region Continuation

	public Job ContinueWith(Action<Job> continuationAction)
	{
		return ContinueWith((Delegate)continuationAction, null, CancellationToken.None, ToContinuationOptions(CreationOptions), null);
	}
	public Job ContinueWith(Action<Job> continuationAction, IJobScheduler? scheduler)
	{
		return ContinueWith((Delegate)continuationAction, null, CancellationToken.None, ToContinuationOptions(CreationOptions), scheduler);
	}
	public Job ContinueWith(Action<Job> continuationAction, CancellationToken cancellationToken)
	{
		return ContinueWith((Delegate)continuationAction, null, cancellationToken, ToContinuationOptions(CreationOptions), null);
	}
	public Job ContinueWith(Action<Job> continuationAction, JobContinuationOptions continuationOptions)
	{
		return ContinueWith((Delegate)continuationAction, null, CancellationToken.None, continuationOptions, null);
	}
	public Job ContinueWith(Action<Job> continuationAction, CancellationToken cancellationToken, JobContinuationOptions continuationOptions, IJobScheduler? scheduler)
	{
		return ContinueWith((Delegate)continuationAction, null, cancellationToken, continuationOptions, scheduler);
	}

	public Job ContinueWith(Action<Job, Object?> continuationAction, Object? state)
	{
		return ContinueWith((Delegate)continuationAction, state, CancellationToken.None, ToContinuationOptions(CreationOptions), null);
	}
	public Job ContinueWith(Action<Job, Object?> continuationAction, Object? state, IJobScheduler? scheduler)
	{
		return ContinueWith((Delegate)continuationAction, state, CancellationToken.None, ToContinuationOptions(CreationOptions), scheduler);
	}
	public Job ContinueWith(Action<Job, Object?> continuationAction, Object? state, CancellationToken cancellationToken)
	{
		return ContinueWith((Delegate)continuationAction, state, cancellationToken, ToContinuationOptions(CreationOptions), null);
	}
	public Job ContinueWith(Action<Job, Object?> continuationAction, Object? state, JobContinuationOptions continuationOptions)
	{
		return ContinueWith((Delegate)continuationAction, state, CancellationToken.None, continuationOptions, null);
	}
	public Job ContinueWith(Action<Job, Object?> continuationAction, Object? state, CancellationToken cancellationToken, JobContinuationOptions continuationOptions, IJobScheduler? scheduler)
	{
		return ContinueWith((Delegate)continuationAction, state, cancellationToken, continuationOptions, scheduler);
	}

	public Job<TResult> ContinueWith<TResult>(Func<Job, TResult> continuationFunction)
	{
		return ContinueWith<TResult>((Delegate)continuationFunction, null, CancellationToken.None, ToContinuationOptions(CreationOptions), null);
	}
	public Job<TResult> ContinueWith<TResult>(Func<Job, TResult> continuationFunction, IJobScheduler? scheduler)
	{
		return ContinueWith<TResult>((Delegate)continuationFunction, null, CancellationToken.None, ToContinuationOptions(CreationOptions), scheduler);
	}
	public Job<TResult> ContinueWith<TResult>(Func<Job, TResult> continuationFunction, CancellationToken cancellationToken)
	{
		return ContinueWith<TResult>((Delegate)continuationFunction, null, cancellationToken, ToContinuationOptions(CreationOptions), null);
	}
	public Job<TResult> ContinueWith<TResult>(Func<Job, TResult> continuationFunction, JobContinuationOptions continuationOptions)
	{
		return ContinueWith<TResult>((Delegate)continuationFunction, null, CancellationToken.None, continuationOptions, null);
	}
	public Job<TResult> ContinueWith<TResult>(Func<Job, TResult> continuationFunction, CancellationToken cancellationToken, JobContinuationOptions continuationOptions, IJobScheduler? scheduler)
	{
		return ContinueWith<TResult>((Delegate)continuationFunction, null, cancellationToken, continuationOptions, scheduler);
	}

	public Job<TResult> ContinueWith<TResult>(Func<Job, Object?, TResult> continuationFunction, Object? state)
	{
		return ContinueWith<TResult>((Delegate)continuationFunction, state, CancellationToken.None, ToContinuationOptions(CreationOptions), null);
	}
	public Job<TResult> ContinueWith<TResult>(Func<Job, Object?, TResult> continuationFunction, Object? state, IJobScheduler? scheduler)
	{
		return ContinueWith<TResult>((Delegate)continuationFunction, state, CancellationToken.None, ToContinuationOptions(CreationOptions), scheduler);
	}
	public Job<TResult> ContinueWith<TResult>(Func<Job, Object?, TResult> continuationFunction, Object? state, CancellationToken cancellationToken)
	{
		return ContinueWith<TResult>((Delegate)continuationFunction, state, cancellationToken, ToContinuationOptions(CreationOptions), null);
	}
	public Job<TResult> ContinueWith<TResult>(Func<Job, Object?, TResult> continuationFunction, Object? state, JobContinuationOptions continuationOptions)
	{
		return ContinueWith<TResult>((Delegate)continuationFunction, state, CancellationToken.None, continuationOptions, null);
	}
	public Job<TResult> ContinueWith<TResult>(Func<Job, Object?, TResult> continuationFunction, Object? state, CancellationToken cancellationToken, JobContinuationOptions continuationOptions, IJobScheduler? scheduler)
	{
		return ContinueWith<TResult>((Delegate)continuationFunction, state, cancellationToken, continuationOptions, scheduler);
	}

	protected Job SetAwaiterContinuation(Action continuation, AwaiterConfiguration configuration)
	{
		JobContinuationOptions continuationOptions = JobContinuationOptions.OnlyOnRanToCompletion;

		if (configuration != null && configuration.AttachedToParent)
			continuationOptions |= JobContinuationOptions.AttachedToParent;

		return ContinueWith((Delegate)continuation, configuration, CancellationToken, continuationOptions, Scheduler);

		//// create the job, but do not register it as a continuation
		//JobCreationOptions creationOptions = ToCreationOptions(continuationOptions);
		//return new Job((Delegate)continuation, configuration, CancellationToken, creationOptions, JobScheduler, this);
	}
	protected Job ContinueWith(Delegate procedure, Object? state, CancellationToken cancellationToken, JobContinuationOptions continuationOptions, IJobScheduler? scheduler)
	{
		// create the job
		JobCreationOptions creationOptions = ToCreationOptions(continuationOptions);
		Job job = new(procedure, state, cancellationToken, creationOptions, scheduler, this);

		RegisterContinuation(job);

		return job;
	}
	protected Job<TResult> ContinueWith<TResult>(Delegate procedure, Object? state, CancellationToken cancellationToken, JobContinuationOptions continuationOptions, IJobScheduler? scheduler)
	{
		// create the job
		JobCreationOptions creationOptions = ToCreationOptions(continuationOptions);
		Job<TResult> job = new(procedure, state, cancellationToken, creationOptions, scheduler, this);

		RegisterContinuation(job);

		return job;
	}

	protected void RegisterContinuation(Job job)
	{
		RegisterContinuation(job, (JobContinuationOptions)JOB_CONTINUATION_FLAG);
	}
	protected virtual void RegisterContinuation(Job job, JobContinuationOptions extraOptions)
	{
		// allocate the list
		// In 90% of cases there will be only one continuation, so we keep a single Job instead of an array.
		if (_continuations == null)
			Interlocked.CompareExchange<IndigentList<Job>?>(ref _continuations, new IndigentList<Job>(), null);

		// add the item
		lock (_continuations)
		{
			// ensure to have the continuation flag on the job
			job._executionOptions |= (Int32)extraOptions;
			_continuations.Add(job);

			if (_execContinuationsBaseIndex != -1)
			{
				// the continuations are already executed and this one is left over
				// calling ExecuteContinuations will ensure to process this one as well
				// that also means that the current task has completed it's execution
				Debug.Assert(Status > JobStatus.Running);
				ExecuteContinuations();
			}
		}
	}

	private static JobContinuationOptions ToContinuationOptions(JobCreationOptions initiatorCreationOptions)
	{
		JobContinuationOptions continuationOptions = JobContinuationOptions.None;

		// continuations will run only if the current one completes with success
		continuationOptions |= JobContinuationOptions.OnlyOnRanToCompletion;

		// determine if continuations should run in synchronous or asynchronous manner
		if ((initiatorCreationOptions & JobCreationOptions.RunContinuationsAsynchronously) == JobCreationOptions.RunContinuationsAsynchronously)
		{
			continuationOptions |= JobContinuationOptions.RunContinuationsAsynchronously;
			continuationOptions &= ~JobContinuationOptions.RunSynchronously;
		}
		else if ((initiatorCreationOptions & JobCreationOptions.RunSynchronously) == JobCreationOptions.RunSynchronously)
		{
			continuationOptions |= JobContinuationOptions.RunSynchronously;
		}

		return continuationOptions;
	}
	private static JobCreationOptions ToCreationOptions(JobContinuationOptions continuationOptions)
	{
		// do not mask the options, those will be masked in appropriate accessors not to loose the flags
		return (JobCreationOptions)continuationOptions/* & ALL_CREATION_OPTIONS*/;
	}

	#endregion // Continuation

	#region await support

	[Obsolete("Awaiting a Job without configured awaiter is deprecated.")]
	public Awaiter GetAwaiter()
	{
		return new Awaiter(this);
	}
	public ConfiguredAwaiter ConfigureAwait()
	{
		return new ConfiguredAwaiter(this);
	}
	public ConfiguredAwaiter ConfigureAwait(Boolean continueOnCapturedContext)
	{
		return new ConfiguredAwaiter(this).ContinueOnCapturedContext(continueOnCapturedContext);
	}

	public class AwaiterConfiguration
	{
		private ExecutionContext? _executionContext = null;
		private Boolean _isExecutionContextSet = false;
		private SynchronizationContext? _synchronizationContext = null;
		private Boolean _isSynchronizationContextSet = false;
		private Boolean _attachedToParent = false;

		internal AwaiterConfiguration(Job owner)
		{
			Job = owner;
		}

		public Job Job { get; }

		public ExecutionContext? ExecutionContext
		{
			get { return _executionContext; }
			internal set { _executionContext = value; _isExecutionContextSet = true; }
		}
		public Boolean IsExecutionContextSet
		{
			get { return _isExecutionContextSet; }
		}
		public void CaptureExecutionContext()
		{
			_executionContext = ExecutionContext.Capture();
			_isExecutionContextSet = true;
		}
		public void ResetExecutionContext()
		{
			_executionContext = null;
			_isExecutionContextSet = false;
		}

		public SynchronizationContext? SynchronizationContext
		{
			get { return _synchronizationContext; }
			internal set { _synchronizationContext = value; _isSynchronizationContextSet = true; }
		}
		public Boolean IsSynchronizationContextSet
		{
			get { return _isSynchronizationContextSet; }
		}
		public void CaptureSynchronizationContext()
		{
			_synchronizationContext = SynchronizationContext.Current;
			_isSynchronizationContextSet = true;
		}
		public void ResetSynchronizationContext()
		{
			_synchronizationContext = null;
			_isSynchronizationContextSet = false;
		}

		public Boolean AttachedToParent
		{
			get { return _attachedToParent; }
			internal set { _attachedToParent = value; }
		}
	}

	public readonly struct ConfiguredAwaiter
	{
		internal ConfiguredAwaiter(Job owner)
		{
			Configuration = new AwaiterConfiguration(owner);
		}

		public AwaiterConfiguration Configuration { get; }
		public Awaiter GetAwaiter() { return new Awaiter(Configuration.Job, Configuration); }

		public ConfiguredAwaiter ContinueOnCapturedContext(Boolean captureCurrentThreadContext)
		{
			// Note: captured thread context should relate to the synchronization context only
			// execution context is captured automatically unless reset
			if (captureCurrentThreadContext)
			{
				//Configuration.CaptureExecutionContext();
				Configuration.CaptureSynchronizationContext();
			}
			else
			{
				//Configuration.ResetExecutionContext();
				Configuration.ResetSynchronizationContext();
			}

			return this;
		}
		public ConfiguredAwaiter ContinueOnExecutionContext(ExecutionContext executionContext)
		{
			Configuration.ExecutionContext = executionContext;

			return this;
		}
		public ConfiguredAwaiter ContinueOnSynchronizationContext(SynchronizationContext synchronizationContext)
		{
			Configuration.SynchronizationContext = synchronizationContext;

			return this;
		}
		public ConfiguredAwaiter ContinueAttachedToParent(Boolean attached)
		{
			Configuration.AttachedToParent = attached;

			return this;
		}
	}

	public readonly struct Awaiter :
		System.Runtime.CompilerServices.INotifyCompletion,
		System.Runtime.CompilerServices.ICriticalNotifyCompletion,
		IJobMethodBuilderAwaiter
	{
		internal Awaiter(Job job)
		{
			Job = job;
			Configuration = new AwaiterConfiguration(Job);
		}
		internal Awaiter(Job job, AwaiterConfiguration configuration)
		{
			Job = job;
			Configuration = configuration;
		}

		public Job Job { get; }
		public AwaiterConfiguration Configuration { get; }

		public Boolean IsCompleted { get { return Job.IsCompleted; } }
		public void GetResult()
		{
			Job.WaitNoThrow();

			Job.ThrowIfCompletedUnexpectedly(true);
		}
		public void OnCompleted(Action continuation)
		{
			CreateAwaiterContinuation(continuation);
		}
		public void UnsafeOnCompleted(Action continuation)
		{
			CreateUnsafeAwaiterContinuation(continuation);
		}
		public Job CreateAwaiterContinuation(Action continuation)
		{
			if (!Configuration.IsExecutionContextSet)
				Configuration.CaptureExecutionContext();

			return Job.SetAwaiterContinuation(continuation, Configuration);
		}
		public Job CreateUnsafeAwaiterContinuation(Action continuation)
		{
			return Job.SetAwaiterContinuation(continuation, Configuration);
		}
		public void MethodBuilderOnCompleted(Job continuation)
		{
			if (continuation == null)
			{
				Debug.Assert(false);
				throw new ArgumentNullException(nameof(continuation));
			}

			if (!Configuration.IsExecutionContextSet)
				Configuration.CaptureExecutionContext();

			// do not change initiator of the continuation job (JobMethodBuilder.ResultJob) or 
			// it will be executed by the job scheduler once this one is complete
			continuation.Initiator = Job;
			continuation.AsyncState = Configuration;

			Job.RegisterContinuation(continuation);
		}
	}

	#endregion // await support

	#region Static helpers

	#region Run

	// running
	public static Job Run(Action action)
	{
		Job result = new(action);

		result.Run();

		return result;
	}
	public static Job Run(Action action, CancellationToken cancellationToken)
	{
		Job result = new(action, cancellationToken);

		result.Run();

		return result;
	}
	public static Job Run(Action action, JobCreationOptions creationOptions)
	{
		Job result = new(action, creationOptions);

		result.Run();

		return result;
	}
	public static Job Run(Action action, CancellationToken cancellationToken, JobCreationOptions creationOptions, IJobScheduler? scheduler)
	{
		Job result = new(action, cancellationToken, creationOptions, scheduler);

		result.Run();

		return result;
	}

	public static Job Run(Action<Object?> action, Object? state)
	{
		Job result = new(action, state);

		result.Run();

		return result;
	}
	public static Job Run(Action<Object?> action, Object? state, CancellationToken cancellationToken)
	{
		Job result = new(action, state, cancellationToken);

		result.Run();

		return result;
	}
	public static Job Run(Action<Object?> action, Object? state, JobCreationOptions creationOptions)
	{
		Job result = new(action, state, creationOptions);

		result.Run();

		return result;
	}
	public static Job Run(Action<Object?> action, Object? state, CancellationToken cancellationToken, JobCreationOptions creationOptions, IJobScheduler? scheduler)
	{
		Job result = new(action, state, cancellationToken, creationOptions, scheduler);

		result.Run();

		return result;
	}

	public static Job<TResult> Run<TResult>(Func<TResult> function)
	{
		Job<TResult> result = new(function);

		result.Run();

		return result;
	}
	public static Job<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken)
	{
		Job<TResult> result = new(function, cancellationToken);

		result.Run();

		return result;
	}
	public static Job<TResult> Run<TResult>(Func<TResult> function, JobCreationOptions creationOptions)
	{
		Job<TResult> result = new(function, creationOptions);

		result.Run();

		return result;
	}
	public static Job<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken, JobCreationOptions creationOptions, IJobScheduler? scheduler)
	{
		Job<TResult> result = new(function, cancellationToken, creationOptions, scheduler);

		result.Run();

		return result;
	}

	public static Job<TResult> Run<TResult>(Func<Object?, TResult> function, Object? state)
	{
		Job<TResult> result = new(function, state);

		result.Run();

		return result;
	}
	public static Job<TResult> Run<TResult>(Func<Object?, TResult> function, Object? state, CancellationToken cancellationToken)
	{
		Job<TResult> result = new(function, state, cancellationToken);

		result.Run();

		return result;
	}
	public static Job<TResult> Run<TResult>(Func<Object?, TResult> function, Object? state, JobCreationOptions creationOptions)
	{
		Job<TResult> result = new(function, state, creationOptions);

		result.Run();

		return result;
	}
	public static Job<TResult> Run<TResult>(Func<Object?, TResult> function, Object? state, CancellationToken cancellationToken, JobCreationOptions creationOptions, IJobScheduler? scheduler)
	{
		Job<TResult> result = new(function, state, cancellationToken, creationOptions, scheduler);

		result.Run();

		return result;
	}

	#endregion // Run

	#region Special Jobs

	// action that never should be invoked
	private static void ActionNotSetProc()
	{
		throw new InvalidOperationException();
	}
	private static readonly Action _noAction = new (ActionNotSetProc);
	protected static Action NoAction
	{
		get { return _noAction; }
	}

	// action that does nothing
	private static void ActionVoid()
	{
	}
	private static readonly Action _voidAction = new(ActionVoid);
	protected static Action VoidAction
	{
		get { return _voidAction; }
	}

	public static Job CompletedJob
	{
		get { return _completedJob; }
	}

	public static Job<TResult> FromResult<TResult>(TResult result)
	{
		return new Job<TResult>(result);
	}

	public static Job FromCanceled(CancellationToken cancellationToken)
	{
		if (!cancellationToken.IsCancellationRequested)
			throw new ArgumentException("Cancellation should be requested in the token", nameof(cancellationToken));

		return new Job(cancellationToken);
	}
	public static Job<TResult> FromCanceled<TResult>(CancellationToken cancellationToken)
	{
		if (!cancellationToken.IsCancellationRequested)
			throw new ArgumentException("Cancellation should be requested in the token", nameof(cancellationToken));

		return new Job<TResult>(cancellationToken);
	}

	public static Job FromException(Exception exception)
	{
		return new Job(exception);
	}
	public static Job<TResult> FromException<TResult>(Exception exception)
	{
		return new Job<TResult>(exception);
	}

	// use await Job.Yield() to immediately return from an asynchronous method
	public static ConfiguredAwaiter Yield()
	{
		Job delayJob = new(VoidAction);

		delayJob.Run();

		return delayJob.ConfigureAwait(false);
	}
	// use await Job.Yield(scheduler) to immediately return from an asynchronous method
	public static ConfiguredAwaiter Yield(IJobScheduler jobScheduler)
	{
		Job delayJob = new(VoidAction);

		delayJob.Run(jobScheduler);

		return delayJob.ConfigureAwait(false);
	}

	#endregion // Special Jobs

	#region Delay

	// delay
	public static Job Delay(Int32 millisecondsDelay)
	{
		return Delay(millisecondsDelay, CancellationToken.None);
	}
	public static Job Delay(Int32 millisecondsDelay, CancellationToken cancellationToken)
	{
		Job delayJob = new(() =>
		{
			if (cancellationToken.CanBeCanceled)
				cancellationToken.WaitHandle.WaitOne(millisecondsDelay);
			else
				Thread.Sleep(millisecondsDelay);
		});

		delayJob.Run();

		return delayJob;
	}
	public static Job Delay(TimeSpan delay)
	{
		return Delay(delay, CancellationToken.None);
	}
	public static Job Delay(TimeSpan delay, CancellationToken cancellationToken)
	{
		Job delayJob = new(() =>
		{
			if (cancellationToken.CanBeCanceled)
				cancellationToken.WaitHandle.WaitOne(delay);
			else
				Thread.Sleep(delay);
		});

		delayJob.Run();

		return delayJob;
	}

	#endregion Delay

	#region Wait For Many Jobs

	public static void WaitAll(params Job[] jobs)
	{
		if (jobs.Length == 0)
			return;

		foreach (Job job in jobs)
		{
			job.WaitNoThrow();
		}
	}
	public static Boolean WaitAll(Job[] jobs, Int32 millisecondsTimeout)
	{
		if (jobs.Length == 0)
			return true;

		Stopwatch sw = Stopwatch.StartNew();
		foreach (Job job in jobs)
		{
			if (!job.WaitNoThrow(millisecondsTimeout))
				return false;
			if (sw.ElapsedMilliseconds >= millisecondsTimeout)
				return false;
		}

		return true;
	}
	public static void WaitAll(Job[] jobs, CancellationToken cancellationToken)
	{
		if (jobs.Length == 0)
			return;

		foreach (Job job in jobs)
		{
			job.WaitNoThrow(cancellationToken);
		}
	}
	public static Boolean WaitAll(Job[] jobs, Int32 millisecondsTimeout, CancellationToken cancellationToken)
	{
		if (jobs.Length == 0)
			return true;

		Stopwatch sw = Stopwatch.StartNew();
		foreach (Job job in jobs)
		{
			if (!job.WaitNoThrow(millisecondsTimeout, cancellationToken))
				return false;
			if (sw.ElapsedMilliseconds >= millisecondsTimeout)
				return false;
		}

		return true;
	}
	public static Boolean WaitAll(Job[] jobs, TimeSpan timeout)
	{
		if (jobs.Length == 0)
			return true;

		Stopwatch sw = Stopwatch.StartNew();
		foreach (Job job in jobs)
		{
			if (!job.WaitNoThrow(timeout))
				return false;
			if (sw.ElapsedMilliseconds >= timeout.Milliseconds)
				return false;
		}

		return true;
	}

	public static Int32 WaitAny(params Job[] jobs)
	{
		if (jobs.Length == 0)
			return -1;

		Int32 count = jobs.Length;
		for (Int32 index = 0; index < count; index++)
		{
			Job job = jobs[index];
			if (job.IsCompleted)
				return index;
		}

		List<WaitHandle> listWaitHandles = new(count);
		List<Int32> listIndexMap = new(count);
		for (Int32 index = 0; index < count; index++)
		{
			Job job = jobs[index];
			if (job.IsCompleted)
				return index;

			listWaitHandles.Add(job.AsyncWaitHandleSlim.WaitHandle);
			listIndexMap.Add(index);
		}

		Int32 result = WaitHandle.WaitAny(listWaitHandles.ToArray());
		return listIndexMap[result];
	}
	public static Int32 WaitAny(Job[] jobs, Int32 millisecondsTimeout)
	{
		if (jobs.Length == 0)
			return -1;

		Int32 count = jobs.Length;
		for (Int32 index = 0; index < count; index++)
		{
			Job job = jobs[index];
			if (job.IsCompleted)
				return index;
		}

		List<WaitHandle> listWaitHandles = new(count);
		List<Int32> listIndexMap = new(count);
		for (Int32 index = 0; index < count; index++)
		{
			Job job = jobs[index];
			if (job.IsCompleted)
				return index;

			listWaitHandles.Add(job.AsyncWaitHandleSlim.WaitHandle);
			listIndexMap.Add(index);
		}

		Int32 result = WaitHandle.WaitAny(listWaitHandles.ToArray(), millisecondsTimeout);
		return listIndexMap[result];
	}
	public static Int32 WaitAny(Job[] jobs, CancellationToken cancellationToken)
	{
		if (jobs.Length == 0)
			return -1;

		Int32 count = jobs.Length;
		for (Int32 index = 0; index < count; index++)
		{
			Job job = jobs[index];
			if (job.IsCompleted)
				return index;
		}

		List<WaitHandle> listWaitHandles = new(cancellationToken.CanBeCanceled ? count + 1 : count);
		List<Int32> listIndexMap = new(cancellationToken.CanBeCanceled ? count + 1 : count);
		for (Int32 index = 0; index < count; index++)
		{
			Job job = jobs[index];
			if (job.IsCompleted)
				return index;

			listWaitHandles.Add(job.AsyncWaitHandleSlim.WaitHandle);
			listIndexMap.Add(index);
		}
		if (!cancellationToken.CanBeCanceled)
		{
			listWaitHandles.Add(cancellationToken.WaitHandle);
			listIndexMap.Add(-1);
		}

		Int32 result = WaitHandle.WaitAny(listWaitHandles.ToArray());
		return listIndexMap[result];
	}
	public static Int32 WaitAny(Job[] jobs, Int32 millisecondsTimeout, CancellationToken cancellationToken)
	{
		if (jobs.Length == 0)
			return -1;

		Int32 count = jobs.Length;
		for (Int32 index = 0; index < count; index++)
		{
			Job job = jobs[index];
			if (job.IsCompleted)
				return index;
		}

		List<WaitHandle> listWaitHandles = new(cancellationToken.CanBeCanceled ? count + 1 : count);
		List<Int32> listIndexMap = new(cancellationToken.CanBeCanceled ? count + 1 : count);
		for (Int32 index = 0; index < count; index++)
		{
			Job job = jobs[index];
			if (job.IsCompleted)
				return index;

			listWaitHandles.Add(job.AsyncWaitHandleSlim.WaitHandle);
			listIndexMap.Add(index);
		}
		if (!cancellationToken.CanBeCanceled)
		{
			listWaitHandles.Add(cancellationToken.WaitHandle);
			listIndexMap.Add(-1);
		}

		Int32 result = WaitHandle.WaitAny(listWaitHandles.ToArray(), millisecondsTimeout);
		return listIndexMap[result];
	}
	public static Int32 WaitAny(Job[] jobs, TimeSpan timeout)
	{
		if (jobs.Length == 0)
			return -1;

		Int32 count = jobs.Length;
		for (Int32 index = 0; index < count; index++)
		{
			Job job = jobs[index];
			if (job.IsCompleted)
				return index;
		}

		List<WaitHandle> listWaitHandles = new(count);
		List<Int32> listIndexMap = new(count);
		for (Int32 index = 0; index < count; index++)
		{
			Job job = jobs[index];
			if (job.IsCompleted)
				return index;

			listWaitHandles.Add(job.AsyncWaitHandleSlim.WaitHandle);
			listIndexMap.Add(index);
		}

		Int32 result = WaitHandle.WaitAny(listWaitHandles.ToArray(), timeout);
		return listIndexMap[result];
	}

	public static Job WhenAll(params Job[] jobs)
	{
		return WhenAll(jobs, jobs.Length);
	}
	public static Job WhenAll(IEnumerable<Job> jobs)
	{
		return WhenAll(jobs, jobs.Count());
	}
	public static Job<TResult[]> WhenAll<TResult>(params Job<TResult>[] jobs)
	{
		return WhenAll(jobs, jobs.Length);
	}
	public static Job<TResult[]> WhenAll<TResult>(IEnumerable<Job<TResult>> jobs)
	{
		return WhenAll(jobs, jobs.Count());
	}
	public static Job<Job> WhenAny(params Job[] jobs)
	{
		return WhenAny(jobs, jobs.Length);
	}
	public static Job<Job> WhenAny(IEnumerable<Job> jobs)
	{
		return WhenAny(jobs, jobs.Count());
	}
	public static Job<Job<TResult>> WhenAny<TResult>(params Job<TResult>[] jobs)
	{
		return WhenAny(jobs, jobs.Length);
	}
	public static Job<Job<TResult>> WhenAny<TResult>(IEnumerable<Job<TResult>> jobs)
	{
		return WhenAny(jobs, jobs.Count());
	}

	#region Internal implementation for WhenAll & WhenAny

	private static IEnumerable<Exception>? GetJobsExceptions(IEnumerable<Job> jobs)
	{
		List<Exception>? exceptions = null;
		Exception exc;

		foreach (Job jobCompleted in jobs)
		{
			if (jobCompleted.Exception != null)
				exc = jobCompleted.Exception;               // report the exception
			else if (jobCompleted.Status == JobStatus.Canceled)
				exc = new OperationCanceledException();   // no other way of reporting the canceled job
			else
				continue;

			if (exceptions == null)
				exceptions = new List<Exception> { exc };
			else
				exceptions.Add(exc);
		}

		return exceptions;
	}
	private static TResult[] GetJobsResults<TResult>(IEnumerable<Job<TResult>> jobs, Int32 jobsCount)
	{
		TResult[] results = new TResult[jobsCount];
		Int32 index = 0;

		foreach (Job<TResult> jobCompleted in jobs)
			results[index++] = jobCompleted.Result;

		return results;
	}
	private static Job GetAnyCompletedJob(IEnumerable<Job> jobs)
	{
		foreach (Job job in jobs)
		{
			if (job.IsCompleted)
				return job;
		}

		Debug.Assert(false, "list of JObs cannot be empty");
		return Job.CompletedJob;
	}

	private static Job WhenAll(IEnumerable<Job> jobs, Int32 jobsCount)
	{
		if (jobsCount == 0)
			throw new ArgumentException("Number of jobs cannot be 0", nameof(jobs));

		Job waiter = new(() =>
		{
			// at this point execution of all jobs is complete

			// add the exceptions (if any) to the waiter job aggregate exception list
			IEnumerable<Exception>? exceptions = GetJobsExceptions(jobs);
			if (exceptions != null)
				throw new TransientAggregateException(exceptions);
		});

		// create a counter with jobsCount number of locks
		// every of job continuation will release the counter once, and the last one will run the waiter job
		LockCounter counter = new(jobsCount);

		// continuation job to release the counter once
		Job continuation = new(() =>
		{
			// the waiter job will run once all locks are released (all jobs completed)
			if (counter.Unlock() == 0)
				waiter.RunSynchronously();
		},
		JobCreationOptions.RunSynchronously | (JobCreationOptions)JOB_LIGHTWEIGHT_FLAG);
		waiter.Initiator = continuation;

		// register continuations to be called and initiate start of the waiter job
		foreach (Job job in jobs)
		{
			job.RegisterContinuation(continuation);

			continuation.Initiator ??= job;
		}

		return waiter;
	}
	private static Job<TResult[]> WhenAll<TResult>(IEnumerable<Job<TResult>> jobs, Int32 jobsCount)
	{
		if (jobsCount == 0)
			throw new ArgumentException("Number of jobs cannot be 0", nameof(jobs));

		Job<TResult[]> waiter = new(() =>
		{
			// at this point execution of all jobs is complete

			// add the exceptions (if any) to the waiter job aggregate exception list
			IEnumerable<Exception>? exceptions = GetJobsExceptions(jobs);
			if (exceptions != null)
				throw new TransientAggregateException(exceptions);

			// return the results
			return GetJobsResults(jobs, jobsCount);
		});

		// create a counter with jobsCount number of locks
		// every of job continuation will release the counter once, and the last one will run the waiter job
		LockCounter counter = new(jobsCount);

		// continuation job to release the counter once
		Job continuation = new(() =>
		{
			// the waiter job will run once all locks are released (all jobs completed)
			if (counter.Unlock() == 0)
				waiter.RunSynchronously();
		},
		JobCreationOptions.RunSynchronously | (JobCreationOptions)JOB_LIGHTWEIGHT_FLAG);
		waiter.Initiator = continuation;

		// register continuations to be called and initiate start of the waiter job
		foreach (Job job in jobs)
		{
			job.RegisterContinuation(continuation);

			continuation.Initiator ??= job;
		}

		return waiter;
	}
	private static Job<Job> WhenAny(IEnumerable<Job> jobs, Int32 jobsCount)
	{
		if (jobsCount == 0)
			throw new ArgumentException("Number of jobs cannot be 0", nameof(jobs));

		Job<Job> waiter = new(() =>
		{
			// at this point at least one job must be completed
			Job jobCompleted = GetAnyCompletedJob(jobs);

			// add the exception (if any) to the waiter job aggregate exception list
			Exception? exception = jobCompleted.Exception;
			if (exception != null)
				throw new TransientAggregateException(exception);

			return jobCompleted;
		});

		// create a counter with jobsCount number of locks
		// every of job continuation will release the counter once, and the last one will run the waiter job
		LockCounter counter = new(1);

		// continuation job to release the counter once
		Job continuation = new(() =>
		{
			// the waiter job will run once the first lock is released (any jobs completes)
			if (counter.Unlock() == 0)
				waiter.RunSynchronously();
		},
		JobCreationOptions.RunSynchronously | (JobCreationOptions)JOB_LIGHTWEIGHT_FLAG);
		waiter.Initiator = continuation;

		// register continuations to be called and initiate start of the waiter job
		foreach (Job job in jobs)
		{
			job.RegisterContinuation(continuation);

			continuation.Initiator ??= job;
		}

		return waiter;
	}
	private static Job<Job<TResult>> WhenAny<TResult>(IEnumerable<Job<TResult>> jobs, Int32 jobsCount)
	{
		if (jobsCount == 0)
			throw new ArgumentException("Number of jobs cannot be 0", nameof(jobs));

		Job<Job<TResult>> waiter = new(() =>
		{
			// at this point at least one job must be completed
			Job<TResult> jobCompleted = (Job<TResult>)GetAnyCompletedJob(jobs);

			// add the exception (if any) to the waiter job aggregate exception list
			Exception? exception = jobCompleted.Exception;
			if (exception != null)
				throw new TransientAggregateException(exception);

			return jobCompleted;
		});

		// create a counter with jobsCount number of locks
		// every of job continuation will release the counter once, and the last one will run the waiter job
		LockCounter counter = new(1);

		// continuation job to release the counter once
		Job continuation = new(() =>
		{
			// the waiter job will run once the first lock is released (any jobs completes)
			if (counter.Unlock() == 0)
				waiter.RunSynchronously();
		},
		JobCreationOptions.RunSynchronously | (JobCreationOptions)JOB_LIGHTWEIGHT_FLAG);
		waiter.Initiator = continuation;

		// register continuations to be called and initiate start of the waiter job
		foreach (Job job in jobs)
		{
			job.RegisterContinuation(continuation);

			continuation.Initiator ??= job;
		}

		return waiter;
	}

	#endregion // Internal implementation for WhenAll & WhenAny

	#endregion // Wait For Many Jobs

	#endregion // Static helpers

	#region Debugging

	public override String ToString()
	{
		return Procedure.Method.Name;
	}

	#endregion // Debugging
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "<Pending>")]
[System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(JobMethodBuilderT<>))]
public class Job<TResult> : Job
{
	#region Constructors

	public Job(Func<TResult> function)
		: this(function, null, CancellationToken.None, JobCreationOptions.None, null, null)
	{
	}
	public Job(Func<TResult> function, CancellationToken cancellationToken)
		: this(function, null, cancellationToken, JobCreationOptions.None, null, null)
	{
	}
	public Job(Func<TResult> function, JobCreationOptions creationOptions)
		: this(function, null, CancellationToken.None, creationOptions, null, null)
	{
	}
	public Job(Func<TResult> function, CancellationToken cancellationToken, JobCreationOptions creationOptions, IJobScheduler? scheduler)
		: this(function, null, cancellationToken, creationOptions, scheduler, null)
	{
	}

	public Job(Func<Object?, TResult> function, Object? state)
		: this(function, state, CancellationToken.None, JobCreationOptions.None, null, null)
	{
	}
	public Job(Func<Object?, TResult> function, Object? state, CancellationToken cancellationToken)
		: this(function, state, cancellationToken, JobCreationOptions.None, null, null)
	{
	}
	public Job(Func<Object?, TResult> function, Object? state, JobCreationOptions creationOptions)
		: this(function, state, CancellationToken.None, creationOptions, null, null)
	{
	}
	public Job(Func<Object?, TResult> function, Object? state, CancellationToken cancellationToken, JobCreationOptions creationOptions, IJobScheduler? scheduler)
		: this(function, state, cancellationToken, creationOptions, scheduler, null)
	{
	}

	internal Job()
		: this(NoFunction, null, CancellationToken.None, JobCreationOptions.None, null, null)
	{
	}
	internal Job(JobStatus status)
		: base(status)
	{
	}
	internal Job(JobCreationOptions creationOptions)
			: this(NoFunction, null, CancellationToken.None, creationOptions, null, null)
	{
	}
	internal Job(CancellationToken cancellationToken)
		: this(NoFunction, null, cancellationToken, JobCreationOptions.None, null, null)
	{
	}
	internal Job(Exception exc)
		: this(NoFunction, null, CancellationToken.None, JobCreationOptions.None, null, null)
	{
		if (exc != null)
		{
			UpdateStatus(JobStatus.Faulted);
			this.AppendException(exc);
		}
	}
	internal Job(TResult result)
		: this(NoFunction, null, CancellationToken.None, JobCreationOptions.None, null, null)
	{
		UpdateStatus(JobStatus.RanToCompletion);
		_result = result;
	}
	internal Job(Delegate procedure, Object? state, CancellationToken cancellationToken, JobCreationOptions creationOptions, IJobScheduler? scheduler, Job? initiator)
		: base(procedure, state, cancellationToken, creationOptions, scheduler, initiator)
	{
	}

	#endregion // Constructors

	#region Data Members & Properties

	private TResult? _result = default;
	public TResult Result
	{
		get
		{
			WaitNoThrow();
			ThrowIfCompletedUnexpectedly(true);

			return GetResult();
		}
	}
	private TResult GetResult()
	{
		// this is called ONLY once the Job is completed successfully!
		return _result!;
	}
	internal void SetResult(TResult result)
	{
		_result = result;
	}

	private static TResult FunctionNotSetProc()
	{
		throw new InvalidOperationException();
	}
	private static readonly Func<TResult> _noFunction = new (FunctionNotSetProc);
	protected static Func<TResult> NoFunction
	{
		get { return _noFunction; }
	}

	#endregion // Data Members & Properties

	#region Run

	internal protected override void ResetStatus()
	{
		_result = default;

		base.ResetStatus();
	}

	protected override void InvokeProcedureAction()
	{
		// Invoke the delegate
		Debug.Assert(Procedure != null, "Null Procedure in Job<TResult>.ExecuteProcedureImpl()");

		if (Procedure is Func<TResult> function)
		{
			SetResult(function());
			return;
		}

		if (Procedure is Func<Object?, TResult> functionWithState)
		{
			SetResult(functionWithState(AsyncState));
			return;
		}

		Debug.Fail("Invalid Procedure in Job");
	}

	protected override void InvokeContinuationAction()
	{
		// Invoke the delegate
		Debug.Assert(Initiator != null, "Null Initiator in Job<TResult>.ExecuteContinuationImpl()");
		Debug.Assert(Procedure != null, "Null Procedure in Job<TResult>.ExecuteContinuationImpl()");

		if (Procedure is Action awaitAction)
		{
			// See JobMethodBuilder.ResultJob.StateMachine.set, JobMethodBuilderT<TResult>.ResultJob.StateMachine.set
			// See SetAwaiterContinuation(Action continuation, JobAwaiterConfiguration configuration) method
			InvokeAwaitContinuationAction(awaitAction);
			return;
		}

		if (Procedure is Func<Job, TResult> function)
		{
			// See ContinueWith(Func<Job, TResult> continuationFunction, ...) methods
			SetResult(function(Initiator));
			return;
		}

		if (Procedure is Func<Job, Object?, TResult> functionWithState)
		{
			// See ContinueWith(Func<Job, Object, TResult> continuationFunction, Object state, ...) methods
			SetResult(functionWithState(Initiator, AsyncState));
			return;
		}

		Debug.Fail("Invalid Procedure in Continuation Job");
	}

	#endregion// Run

	#region Continuation

	public Job ContinueWith(Action<Job<TResult>> continuationAction)
	{
		return base.ContinueWith(j => continuationAction((Job<TResult>)j));
	}
	public Job ContinueWith(Action<Job<TResult>> continuationAction, IJobScheduler? scheduler)
	{
		return base.ContinueWith(j => continuationAction((Job<TResult>)j), scheduler);
	}
	public Job ContinueWith(Action<Job<TResult>> continuationAction, CancellationToken cancellationToken)
	{
		return base.ContinueWith(j => continuationAction((Job<TResult>)j), cancellationToken);
	}
	public Job ContinueWith(Action<Job<TResult>> continuationAction, JobContinuationOptions continuationOptions)
	{
		return base.ContinueWith(j => continuationAction((Job<TResult>)j), continuationOptions);
	}
	public Job ContinueWith(Action<Job<TResult>> continuationAction, CancellationToken cancellationToken, JobContinuationOptions continuationOptions, IJobScheduler? scheduler)
	{
		return base.ContinueWith(j => continuationAction((Job<TResult>)j), cancellationToken, continuationOptions, scheduler);
	}

	public Job ContinueWith(Action<Job<TResult>, Object?> continuationAction, Object? state)
	{
		return base.ContinueWith((j, s) => continuationAction((Job<TResult>)j, s), state);
	}
	public Job ContinueWith(Action<Job<TResult>, Object?> continuationAction, Object? state, IJobScheduler? scheduler)
	{
		return base.ContinueWith((j, s) => continuationAction((Job<TResult>)j, s), state, scheduler);
	}
	public Job ContinueWith(Action<Job<TResult>, Object?> continuationAction, Object? state, CancellationToken cancellationToken)
	{
		return base.ContinueWith((j, s) => continuationAction((Job<TResult>)j, s), state, cancellationToken);
	}
	public Job ContinueWith(Action<Job<TResult>, Object?> continuationAction, Object? state, JobContinuationOptions continuationOptions)
	{
		return base.ContinueWith((j, s) => continuationAction((Job<TResult>)j, s), state, continuationOptions);
	}
	public Job ContinueWith(Action<Job<TResult>, Object?> continuationAction, Object? state, CancellationToken cancellationToken, JobContinuationOptions continuationOptions, IJobScheduler? scheduler)
	{
		return base.ContinueWith((j, s) => continuationAction((Job<TResult>)j, s), state, cancellationToken, continuationOptions, scheduler);
	}

	public Job<TNewResult> ContinueWith<TNewResult>(Func<Job<TResult>, TNewResult> continuationFunction)
	{
		return base.ContinueWith<TNewResult>(j => continuationFunction((Job<TResult>)j));
	}
	public Job<TNewResult> ContinueWith<TNewResult>(Func<Job<TResult>, TNewResult> continuationFunction, IJobScheduler? scheduler)
	{
		return base.ContinueWith<TNewResult>(j => continuationFunction((Job<TResult>)j), scheduler);
	}
	public Job<TNewResult> ContinueWith<TNewResult>(Func<Job<TResult>, TNewResult> continuationFunction, CancellationToken cancellationToken)
	{
		return base.ContinueWith<TNewResult>(j => continuationFunction((Job<TResult>)j), cancellationToken);
	}
	public Job<TNewResult> ContinueWith<TNewResult>(Func<Job<TResult>, TNewResult> continuationFunction, JobContinuationOptions continuationOptions)
	{
		return base.ContinueWith<TNewResult>(j => continuationFunction((Job<TResult>)j), continuationOptions);
	}
	public Job<TNewResult> ContinueWith<TNewResult>(Func<Job<TResult>, TNewResult> continuationFunction, CancellationToken cancellationToken, JobContinuationOptions continuationOptions, IJobScheduler? scheduler)
	{
		return base.ContinueWith<TNewResult>(j => continuationFunction((Job<TResult>)j), cancellationToken, continuationOptions, scheduler);
	}

	public Job<TNewResult> ContinueWith<TNewResult>(Func<Job<TResult>, Object?, TNewResult> continuationFunction, Object? state)
	{
		return base.ContinueWith<TNewResult>((j, s) => continuationFunction((Job<TResult>)j, s), state);
	}
	public Job<TNewResult> ContinueWith<TNewResult>(Func<Job<TResult>, Object?, TNewResult> continuationFunction, Object? state, IJobScheduler? scheduler)
	{
		return base.ContinueWith<TNewResult>((j, s) => continuationFunction((Job<TResult>)j, s), state, scheduler);
	}
	public Job<TNewResult> ContinueWith<TNewResult>(Func<Job<TResult>, Object?, TNewResult> continuationFunction, Object? state, CancellationToken cancellationToken)
	{
		return base.ContinueWith<TNewResult>((j, s) => continuationFunction((Job<TResult>)j, s), state, cancellationToken);
	}
	public Job<TNewResult> ContinueWith<TNewResult>(Func<Job<TResult>, Object?, TNewResult> continuationFunction, Object? state, JobContinuationOptions continuationOptions)
	{
		return base.ContinueWith<TNewResult>((j, s) => continuationFunction((Job<TResult>)j, s), state, continuationOptions);
	}
	public Job<TNewResult> ContinueWith<TNewResult>(Func<Job<TResult>, Object?, TNewResult> continuationFunction, Object? state, CancellationToken cancellationToken, JobContinuationOptions continuationOptions, IJobScheduler? scheduler)
	{
		return base.ContinueWith<TNewResult>((j, s) => continuationFunction((Job<TResult>)j, s), state, cancellationToken, continuationOptions, scheduler);
	}

	#endregion // Continuation

	#region await support

	[Obsolete("Awaiting a Job without configured awaiter is deprecated.")]
	public new AwaiterT GetAwaiter()
	{
		return new AwaiterT(this);
	}
	public new ConfiguredAwaiterT ConfigureAwait()
	{
		return new ConfiguredAwaiterT(this);
	}
	public new ConfiguredAwaiterT ConfigureAwait(Boolean continueOnCapturedContext)
	{
		return new ConfiguredAwaiterT(this).ContinueOnCapturedContext(continueOnCapturedContext);
	}

	public readonly struct ConfiguredAwaiterT
	{
		internal ConfiguredAwaiterT(Job<TResult> owner)
		{
			Configuration = new AwaiterConfiguration(owner);
		}

		public AwaiterConfiguration Configuration { get; }
		public AwaiterT GetAwaiter() { return new AwaiterT((Job<TResult>)Configuration.Job, Configuration); }

		public ConfiguredAwaiterT ContinueOnCapturedContext(Boolean captureCurrentThreadContext)
		{
			// Note: captured thread context should relate to the synchronization context only
			// execution context is captured automatically unless reset
			if (captureCurrentThreadContext)
			{
				//Configuration.CaptureExecutionContext();
				Configuration.CaptureSynchronizationContext();
			}
			else
			{
				//Configuration.ResetExecutionContext();
				Configuration.ResetSynchronizationContext();
			}

			return this;
		}
		public ConfiguredAwaiterT ContinueOnExecutionContext(ExecutionContext executionContext)
		{
			Configuration.ExecutionContext = executionContext;

			return this;
		}
		public ConfiguredAwaiterT ContinueOnSynchronizationContext(SynchronizationContext synchronizationContext)
		{
			Configuration.SynchronizationContext = synchronizationContext;

			return this;
		}
		public ConfiguredAwaiterT ContinueAttachedToParent(Boolean attached)
		{
			Configuration.AttachedToParent = attached;

			return this;
		}
	}

	//
	// Summary:
	//     Provides an object that waits for the completion of an asynchronous job.
	public readonly struct AwaiterT :
		System.Runtime.CompilerServices.INotifyCompletion,
		System.Runtime.CompilerServices.ICriticalNotifyCompletion,
		IJobMethodBuilderAwaiter
	{
		internal AwaiterT(Job<TResult> job)
		{
			_awaiter = new Awaiter(job);
		}
		internal AwaiterT(Job<TResult> job, AwaiterConfiguration configuration)
		{
			_awaiter = new Awaiter(job, configuration);
		}

		private readonly Awaiter _awaiter;
		Job IJobMethodBuilderAwaiter.Job { get { return _awaiter.Job; } }
		public Job<TResult> Job { get { return (Job<TResult>)_awaiter.Job; } }
		public AwaiterConfiguration Configuration { get { return _awaiter.Configuration; } }

		public Boolean IsCompleted { get { return Job.IsCompleted; } }
		public TResult GetResult()
		{
			//Job.GetResult();

			return Job.Result;
		}
		public void OnCompleted(Action continuation)
		{
			_awaiter.OnCompleted(continuation);
		}
		public void UnsafeOnCompleted(Action continuation)
		{
			_awaiter.UnsafeOnCompleted(continuation);
		}
		public Job CreateAwaiterContinuation(Action continuation)
		{
			return _awaiter.CreateAwaiterContinuation(continuation);
		}
		public Job CreateUnsafeAwaiterContinuation(Action continuation)
		{
			return _awaiter.CreateUnsafeAwaiterContinuation(continuation);
		}
		public void MethodBuilderOnCompleted(Job continuation)
		{
			_awaiter.MethodBuilderOnCompleted(continuation);
		}
	}

	#endregion // await support
}
