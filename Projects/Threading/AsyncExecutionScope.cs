
using System;
using System.Collections.Generic;

namespace Armat.Threading
{
	public class AsyncExecutionScope
	{
		protected AsyncExecutionScope(String message)
		{
			Message = message;
			_correlationID = -1;
		}

		// Key used to create JobRuntimeScope objects
		public static readonly String JobRuntimeScopeKey = typeof(AsyncExecutionScope).FullName;

		public static JobRuntimeScope Create(String message)
		{
			return JobRuntimeScope.Enter(JobRuntimeScopeKey, () => new AsyncExecutionScope(message));
		}
		public static AsyncExecutionScope Current()
		{
			return JobRuntimeScope.GetObject<AsyncExecutionScope>(JobRuntimeScopeKey);
		}

		// Message to be appended to the correlation ID
		public String Message { get; }

		// Numeric auto-incrementing Correlation ID
		private Int64 _correlationID;
		public Int64 CorrelationID
		{
			get
			{
				if (_correlationID == -1)
				{
					lock (this)
					{
						if (_correlationID == -1)
							_correlationID = GetNextCorrelationId();
					}
				}

				return _correlationID;
			}
		}

		// Asynchronous Method Tracer key - generally used for logging
		public override String ToString()
		{
			if (String.IsNullOrEmpty(Message))
				return String.Format("{0:00000000000000000000}", CorrelationID);

			return String.Format("{0:00000000000000000000}:{1}", CorrelationID, Message);
		}

		// Correlation ID generator
		private static readonly Utils.Counter _corrIdCounter = new(0);
		protected virtual Int64 GetNextCorrelationId()
		{
			return _corrIdCounter.Increment();
		}
	}

	public class AsyncExecutionScope<T> : AsyncExecutionScope
	{
		protected AsyncExecutionScope() : base(typeof(T).FullName)
		{
		}
		protected AsyncExecutionScope(String message) : base(message)
		{
		}

		// Key used to create JobRuntimeScope objects (separate key for every generic T type)
		public static readonly new String JobRuntimeScopeKey = typeof(AsyncExecutionScope<T>).FullName;

		public static JobRuntimeScope Create()
		{
			return JobRuntimeScope.Enter(JobRuntimeScopeKey, () => new AsyncExecutionScope<T>());
		}
		public static new JobRuntimeScope Create(String message)
		{
			return JobRuntimeScope.Enter(JobRuntimeScopeKey, () => new AsyncExecutionScope<T>(message));
		}
		public static new AsyncExecutionScope<T> Current()
		{
			return JobRuntimeScope.GetObject<AsyncExecutionScope<T>>(JobRuntimeScopeKey);
		}

		// Correlation ID generator (separate counter for every generic T type)
		private static readonly Utils.Counter _corrIdCounter = new(0);
		protected override Int64 GetNextCorrelationId()
		{
			return _corrIdCounter.Increment();
		}
	}
}
