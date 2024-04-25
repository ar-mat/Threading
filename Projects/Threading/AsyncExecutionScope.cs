
using System;
using System.Collections.Generic;

namespace Armat.Threading;

// the class represents a scope for asynchronous execution
// is auto-generates correlation IDs to be able to trace asynchronous code execution flow
// a name can be added to AsyncExecutionScope object optionally for easier identification
public class AsyncExecutionScope
{
	protected AsyncExecutionScope()
	{
		Name = String.Empty;
		CorrelationID = GetNextCorrelationId();
	}
	protected AsyncExecutionScope(String name)
	{
		Name = name;
		CorrelationID = GetNextCorrelationId();
	}

	// Key used to create JobRuntimeScope objects
	public static readonly String JobRuntimeScopeKey = typeof(AsyncExecutionScope).FullName!;

	// creates a JobRuntimeScope with AsyncExecutionScope value
	public static JobRuntimeScope Create()
	{
		return Create(JobRuntimeScopeKey, () => new AsyncExecutionScope());
	}
	// creates a JobRuntimeScope with named AsyncExecutionScope value
	public static JobRuntimeScope Create(String name)
	{
		return Create(JobRuntimeScopeKey, () => new AsyncExecutionScope(name));
	}

	// creates a JobRuntimeScope with the given key and AsyncExecutionScope value returned by the factory
	// this method can be used by derived classed to create a JobRuntimeScope
	protected static JobRuntimeScope Create(String runtimeScopeKey, Func<AsyncExecutionScope> factory)
	{
		return JobRuntimeScope.Enter(runtimeScopeKey, factory);
	}

	// returns the current AsyncExecutionScope if any, or null otherwise
	public static AsyncExecutionScope? Current()
	{
		return JobRuntimeScope.GetValue<AsyncExecutionScope>(JobRuntimeScopeKey);
	}

	// Name to be appended to the correlation ID if not empty
	public String Name { get; init; }

	// Numeric auto-incrementing Correlation ID
	public Int64 CorrelationID { get; init; }

	// Asynchronous Method Tracer key - generally used for logging
	public override String ToString()
	{
		if (String.IsNullOrEmpty(Name))
			return String.Format("{0:00000000000000000000}", CorrelationID);

		return String.Format("{0:00000000000000000000}:{1}", CorrelationID, Name);
	}

	// Correlation ID generator
	private static readonly Utils.Counter _correlationIdCounter = new(0);
	private static Int64 GetNextCorrelationId()
	{
		return _correlationIdCounter.Increment();
	}
}
