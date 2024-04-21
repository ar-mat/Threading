using System;
using System.Collections.Generic;

namespace Armat.Threading
{
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

		public static IJobScheduler Default
		{
			get
			{
				return JobScheduler.Default;
			}
		}
		[ThreadStatic]
		private static IJobScheduler? _current;
		public static IJobScheduler? Current
		{
			get
			{
				return _current;
			}
		}

		public abstract void Enqueue(Job job);
		public abstract Boolean Cancel(Job job);
		public abstract Int32 PendingJobsCount { get; }

		// this method must be used when executing jobs in a scheduler
		// in sets the IJobScheduler.Current property to this during the job execution
		protected JobStatus ExecuteJobProcedure(Job job)
		{
			if (job == null)
				throw new ArgumentNullException(nameof(job));

			JobStatus result;
			IJobScheduler? prevScheduler = _current;

			try
			{
				// set the current job executing scheduler
				_current = this;

				// execute main procedure of the job
				result = job.ExecuteProcedure();
			}
			finally
			{
				// reset job scheduler
				_current = prevScheduler;
			}

			return result;
		}
		// this method must be used when executing jobs in a scheduler
		// in sets the IJobScheduler.Current property to this during the job execution
		protected Int32 ExecuteJobContinuations(Job job)
		{
			if (job == null)
				throw new ArgumentNullException(nameof(job));

			Int32 result;
			IJobScheduler? prevScheduler = _current;

			try
			{
				// set the current job executing scheduler
				_current = this;

				// execute continuations of the job
				result = job.ExecuteContinuations();
			}
			finally
			{
				// reset job scheduler
				_current = prevScheduler;
			}

			return result;
		}
	}
}
