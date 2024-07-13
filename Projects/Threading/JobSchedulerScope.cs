
using System;
using System.Transactions;

using static System.Formats.Asn1.AsnWriter;

namespace Armat.Threading;


// Used to control the value of IJobScheduler.Current for the given method scope
// It ensures to reset IJobScheduler.Current to the previous one when disposed
public readonly struct JobSchedulerScope : IDisposable
{
	internal JobSchedulerScope(IJobScheduler newScheduler)
	{
		NewScheduler = newScheduler;
		PreviousScheduler = _current;
	}

	// persists the value of IJobScheduler.Current for the caller thread
	[ThreadStatic]
	private static IJobScheduler? _current;
	internal static IJobScheduler? Current
	{
		get => _current;
		private set => _current = value;
	}

	// points to the new scheduler being set
	public IJobScheduler NewScheduler { get; }
	// points to the previous scheduler to restore when leaving the scope
	public IJobScheduler? PreviousScheduler { get; }

	internal void Enter()
	{
		Current = NewScheduler;
	}
	internal void Leave()
	{
		if (Current != NewScheduler)
			throw new ArgumentException("Improper job scheduler scope");

		Current = PreviousScheduler;
	}

	public void Dispose()
	{
		if (Current == NewScheduler)
			Leave();
	}
}
