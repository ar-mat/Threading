using System;
using System.Collections.Generic;

namespace Armat.Threading
{
	// The main interface to work with Job Schedulers
	// It provides teh default job scheduler instance and
	// allows to run jobs within
	public interface IJobScheduler : IDisposable
	{
		static IJobScheduler Default => JobSchedulerBase.Default;
		static IJobScheduler? Current => JobSchedulerBase.Current;

		void Enqueue(Job job);
		Boolean Cancel(Job job);
		Int32 PendingJobsCount { get; }
	}
}
