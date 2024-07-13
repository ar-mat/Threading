using System;
using System.Collections.Generic;

namespace Armat.Threading;

// The main interface to work with Job Schedulers
// It provides teh default job scheduler instance and
// allows to run jobs within
public interface IJobScheduler : IDisposable
{
	// the default IJobScheduler instance
	static IJobScheduler Default
	{
		get => JobSchedulerBase.Default;
	}

	// the current instance of IJobScheduler to be used for Jobs execution
	static IJobScheduler Current
	{
		get => JobSchedulerScope.Current ?? Default;
	}

	// enqueue a Job in the scheduler
	void Enqueue(Job job);
	// cancel the Job
	Boolean Cancel(Job job);
	// returns the number of pending Jobs in the queue
	Int32 PendingJobsCount { get; }
	JobSchedulerScope EnterScope();
}
