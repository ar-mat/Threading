using System;
using System.Collections.Generic;

namespace Armat.Threading;

// abstract base class for IJobScheduler implementations
// it's holding / managing the Default and Current Job Schedulers
public abstract class JobSchedulerBase : IJobScheduler
{
	protected JobSchedulerBase()
	{
	}
	~JobSchedulerBase()
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
		IsDisposed = true;
	}
	public Boolean IsDisposed
	{
		get; private set;
	}

	// the default IJobScheduler instance
	public static IJobScheduler Default
	{
		get
		{
			return JobScheduler.Default;
		}
	}

	[ThreadStatic]
	private static IJobScheduler? _current;
	// the current instance of IJobScheduler to be used for Jobs execution
	public static IJobScheduler Current
	{
		get
		{
			return _current ?? Default;
		}
	}
	// Enter the scope of current Job Scheduler (Current = this)
	public JobSchedulerRuntimeScope EnterScope()
	{
		JobSchedulerRuntimeScope scope = new(this, _current);
		_current = scope.Current;

		return scope;
	}
	// Leave the scope of current Job Scheduler (Current = [previous])
	public void LeaveScope(in JobSchedulerRuntimeScope scope)
	{
		if (scope.Current != this)
			throw new ArgumentException("Inconsistent job scheduler", nameof(scope));

		_current = scope.Previous;
	}

	public abstract void Enqueue(Job job);
	public abstract Boolean Cancel(Job job);
	public abstract Int32 PendingJobsCount { get; }

	// this method must be used when executing jobs in a scheduler
	// in sets the IJobScheduler.Current property to this during the job execution
	protected JobStatus ExecuteJobProcedure(Job job)
	{
		using var scope = EnterScope();

		// execute main procedure of the job
		return job.ExecuteProcedure();
	}
	// this method must be used when executing jobs in a scheduler
	// in sets the IJobScheduler.Current property to this during the job execution
	protected Int32 ExecuteJobContinuations(Job job)
	{
		using var scope = EnterScope();

		// execute continuations of the job
		return job.ExecuteContinuations();
	}
}
