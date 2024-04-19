using System;
using System.Collections.Generic;

namespace Armat.Threading
{
	public interface IJobScheduler : IDisposable
	{
		static IJobScheduler Default => JobSchedulerBase.Default;
		static IJobScheduler? Current => JobSchedulerBase.Current;

		void Enqueue(Job job);
		Boolean Cancel(Job job);
		Int32 PendingJobsCount { get; }
	}
}
