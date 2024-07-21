
using System;
using System.Collections.Generic;

namespace Armat.Threading;

// the class represents a scope for asynchronous execution
// is auto-generates correlation IDs to be able to trace asynchronous code execution flow
// a name can be added to CorrelationIdScope object optionally for easier identification
public class CorrelationIdScope
{
	protected CorrelationIdScope()
	{
		Name = String.Empty;
		CorrelationID = GetNextCorrelationId();
	}
	protected CorrelationIdScope(String name)
	{
		Name = name;
		CorrelationID = GetNextCorrelationId();
	}

	// Key used to create JobRuntimeScope objects
	public static readonly String JobRuntimeScopeKey = typeof(CorrelationIdScope).FullName!;

	// creates a JobRuntimeScope with CorrelationIdScope value
	public static JobRuntimeScope Create()
	{
		return Create(JobRuntimeScopeKey, () => new CorrelationIdScope());
	}
	// creates a JobRuntimeScope with named CorrelationIdScope value
	public static JobRuntimeScope Create(String name)
	{
		return Create(JobRuntimeScopeKey, () => new CorrelationIdScope(name));
	}

	// creates a JobRuntimeScope with the given key and CorrelationIdScope value returned by the factory
	// this method can be used by derived classed to create a JobRuntimeScope
	protected static JobRuntimeScope Create(String runtimeScopeKey, Func<CorrelationIdScope> factory)
	{
		return JobRuntimeScope.Enter(runtimeScopeKey, factory);
	}

	// returns the current CorrelationIdScope if any, or null otherwise
	public static CorrelationIdScope? Current()
	{
		return JobRuntimeScope.GetValue<CorrelationIdScope>(JobRuntimeScopeKey);
	}
	public static Int64 CurrentId()
	{
		CorrelationIdScope? idScope = Current();
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
