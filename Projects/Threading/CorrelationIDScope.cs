
using System;
using System.Collections.Generic;

namespace Armat.Threading;

// the class represents a scope for asynchronous execution
// is auto-generates correlation IDs to be able to trace asynchronous code execution flow
// a name can be added to CorrelationIDScope object optionally for easier identification
public class CorrelationIDScope
{
	protected CorrelationIDScope()
	{
		Name = String.Empty;
		CorrelationID = GetNextCorrelationId();
	}
	protected CorrelationIDScope(String name)
	{
		Name = name;
		CorrelationID = GetNextCorrelationId();
	}

	// Key used to create JobRuntimeScope objects
	public static readonly String JobRuntimeScopeKey = typeof(CorrelationIDScope).FullName!;

	// creates a JobRuntimeScope with CorrelationIDScope value
	public static JobRuntimeScope Create()
	{
		return Create(JobRuntimeScopeKey, () => new CorrelationIDScope());
	}
	// creates a JobRuntimeScope with named CorrelationIDScope value
	public static JobRuntimeScope Create(String name)
	{
		return Create(JobRuntimeScopeKey, () => new CorrelationIDScope(name));
	}

	// creates a JobRuntimeScope with the given key and CorrelationIDScope value returned by the factory
	// this method can be used by derived classed to create a JobRuntimeScope
	protected static JobRuntimeScope Create(String runtimeScopeKey, Func<CorrelationIDScope> factory)
	{
		return JobRuntimeScope.Enter(runtimeScopeKey, factory);
	}

	// returns the current CorrelationIDScope if any, or null otherwise
	public static CorrelationIDScope? Current()
	{
		return JobRuntimeScope.GetValue<CorrelationIDScope>(JobRuntimeScopeKey);
	}
	public static Int64 CurrentId()
	{
		CorrelationIDScope? idScope = Current();
		return idScope == null ? InvalidID : idScope.CorrelationID;
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
	public const Int64 InvalidID = 0;
	private static readonly Utils.Counter _correlationIdCounter = new(InvalidID);
	private static Int64 GetNextCorrelationId()
	{
		return _correlationIdCounter.Increment();
	}
}
