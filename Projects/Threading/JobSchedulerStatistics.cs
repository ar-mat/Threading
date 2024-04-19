using Armat.Utils.Extensions;

using System;
using System.Threading;

namespace Armat.Threading
{
	public struct JobSchedulerStatistics : IEquatable<JobSchedulerStatistics>
	{
		private Int32 _queuedJobs;
		private Int32 _runningJobs;
		private Int32 _succeededJobs;
		private Int32 _canceledJobs;
		private Int32 _faultedJobs;

		public Int32 QueuedJobs { get => _queuedJobs; set => _queuedJobs = value; }
		public Int32 RunningJobs { get => _runningJobs; set => _runningJobs = value; }
		public Int32 SucceededJobs { get => _succeededJobs; set => _succeededJobs = value; }
		public Int32 CanceledJobs { get => _canceledJobs; set => _canceledJobs = value; }
		public Int32 FaultedJobs { get => _faultedJobs; set => _faultedJobs = value; }
		public Int32 IncompleteJobs => QueuedJobs + RunningJobs;
		public Int32 CompletedJobs => SucceededJobs + CanceledJobs + FaultedJobs;

		public Boolean Equals(JobSchedulerStatistics other)
		{
			return _queuedJobs == other._queuedJobs &&
				_runningJobs == other._runningJobs &&
				_succeededJobs == other._succeededJobs &&
				_canceledJobs == other._canceledJobs &&
				_faultedJobs == other._faultedJobs;
		}
		public override Boolean Equals(Object? obj)
		{
			if (obj is JobSchedulerStatistics other)
				return Equals(other);

			return false;
		}
		public override Int32 GetHashCode()
		{
			return HashCode.Combine(_queuedJobs, _runningJobs, _succeededJobs, _canceledJobs, _faultedJobs);
		}
		public override String ToString()
		{
			return $"QueuedJobs: {QueuedJobs}\n" +
				$"RunningJobs: {RunningJobs}\n" +
				$"SucceededJobs: {SucceededJobs}\n" +
				$"CanceledJobs: {CanceledJobs}\n" +
				$"FaultedJobs: {FaultedJobs}";
		}

		public static Boolean operator ==(JobSchedulerStatistics left, JobSchedulerStatistics right)
		{
			return left.Equals(right);
		}
		public static Boolean operator !=(JobSchedulerStatistics left, JobSchedulerStatistics right)
		{
			return !left.Equals(right);
		}
	}
	public sealed class JobSchedulerStatisticsCalculator : IDisposable
	{
		private readonly ReaderWriterLockSlim _rwLock;
		private JobSchedulerStatistics _jobStatistics;
		private JobSchedulerStatistics _mbStatistics;

		public JobSchedulerStatisticsCalculator()
		{
			_rwLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
			_jobStatistics = new JobSchedulerStatistics();
			_mbStatistics = new JobSchedulerStatistics();
		}
		public void Dispose()
		{
			_rwLock.Dispose();
		}

		public JobSchedulerStatistics JobStatistics
		{
			get
			{
				JobSchedulerStatistics result;

				using var locker = _rwLock.CreateRLocker();

				result = _jobStatistics;

				return result;
			}
		}
		public JobSchedulerStatistics MethodBuilderStatistics
		{
			get
			{
				JobSchedulerStatistics result;

				using var locker = _rwLock.CreateRLocker();

				result = _mbStatistics;

				return result;
			}
		}

		public void Queued(Job job)
		{
			if (job == null)
				throw new ArgumentNullException(nameof(job));

			using var locker = _rwLock.CreateWLocker();

			if (!job.IsMethodBuilderResult)
			{
				++_jobStatistics.QueuedJobs;
			}
			else
			{
				++_mbStatistics.QueuedJobs;
			}
		}
		public void Started(Job job)
		{
			if (job == null)
				throw new ArgumentNullException(nameof(job));

			using var locker = _rwLock.CreateWLocker();

			if (!job.IsMethodBuilderResult)
			{
				--_jobStatistics.QueuedJobs;
				++_jobStatistics.RunningJobs;
			}
			else
			{
				--_mbStatistics.QueuedJobs;
				++_mbStatistics.RunningJobs;
			}
		}
		public void Succeeded(Job job)
		{
			if (job == null)
				throw new ArgumentNullException(nameof(job));

			using var locker = _rwLock.CreateWLocker();

			if (!job.IsMethodBuilderResult)
			{
				--_jobStatistics.RunningJobs;
				++_jobStatistics.SucceededJobs;
			}
			else
			{
				--_mbStatistics.RunningJobs;
				++_mbStatistics.SucceededJobs;
			}
		}
		public void Canceled(Job job)
		{
			if (job == null)
				throw new ArgumentNullException(nameof(job));

			using var locker = _rwLock.CreateWLocker();

			if (!job.IsMethodBuilderResult)
			{
				--_jobStatistics.RunningJobs;
				++_jobStatistics.CanceledJobs;
			}
			else
			{
				--_mbStatistics.RunningJobs;
				++_mbStatistics.CanceledJobs;
			}
		}
		public void Faulted(Job job)
		{
			if (job == null)
				throw new ArgumentNullException(nameof(job));

			using var locker = _rwLock.CreateWLocker();

			if (!job.IsMethodBuilderResult)
			{
				--_jobStatistics.RunningJobs;
				++_jobStatistics.FaultedJobs;
			}
			else
			{
				--_mbStatistics.RunningJobs;
				++_mbStatistics.FaultedJobs;
			}
		}

		public Int32 IncompleteJobs
		{
			get
			{
				using var locker = _rwLock.CreateRLocker();

				return _jobStatistics.IncompleteJobs + _mbStatistics.IncompleteJobs;
			}
		}

		public void Reset()
		{
			using var locker = _rwLock.CreateWLocker();

			_jobStatistics.QueuedJobs = 0;
			_jobStatistics.RunningJobs = 0;
			_jobStatistics.RunningJobs = 0;
			_jobStatistics.CanceledJobs = 0;
			_jobStatistics.FaultedJobs = 0;

			_mbStatistics.QueuedJobs = 0;
			_mbStatistics.RunningJobs = 0;
			_mbStatistics.RunningJobs = 0;
			_mbStatistics.CanceledJobs = 0;
			_mbStatistics.FaultedJobs = 0;
		}
	}
}
