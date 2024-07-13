using System;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;

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
	private static IJobScheduler? _default = null;
	public static IJobScheduler Default
	{
		get
		{
			return _default ?? JobScheduler.Default;
		}
		protected set
		{
			// allow setting default Job Scheduler ONLY once
			if (_default != null)
				throw new InvalidOperationException("Default job Scheduler is not allowed to be changed");

			_default = value;
		}
	}

	public abstract void Enqueue(Job job);
	public abstract Boolean Cancel(Job job);
	public abstract Int32 PendingJobsCount { get; }

	// the only implementation
	public JobSchedulerScope EnterScope()
	{
		JobSchedulerScope scope = new(this);
		scope.Enter();

		return scope;
	}

	// updates the status of Job
	// can be used in derived scheduler implementations to control Job status (Job.UpdateStatus is internal to this library)
	protected Boolean UpdateJobStatus(Job job, JobStatus newStatus, JobStatus prevStatus)
	{
		if (newStatus == prevStatus)
			return true;

		// verify the scheduler
		if (job.Scheduler != this)
			throw new InvalidOperationException();

		return job.UpdateStatus(newStatus, prevStatus);
	}
	// this method must be used when executing jobs in a scheduler
	// in sets the IJobScheduler.Current property to this during the job execution
	// can be used in derived scheduler implementations to control Job status (Job.ExecuteProcedure is internal to this library)
	protected JobStatus ExecuteJobProcedure(Job job)
	{
		// verify the scheduler
		if (job.Scheduler != this)
			throw new InvalidOperationException();

		// this will set JobScheduler.Current = this for the current thread
		using var scope = EnterScope();

		// execute main procedure of the job
		return job.ExecuteProcedure();
	}
	// this method must be used when executing job continuations in a scheduler
	// in sets the IJobScheduler.Current property to this during the job execution
	// can be used in derived scheduler implementations to control Job status (Job.ExecuteJobContinuations is internal to this library)
	protected Int32 ExecuteJobContinuations(Job job)
	{
		// verify the scheduler
		if (job.Scheduler != this)
			throw new InvalidOperationException();

		// this will set JobScheduler.Current = this for the current thread
		using var scope = EnterScope();

		// execute continuations of the job
		return job.ExecuteContinuations();
	}
}
