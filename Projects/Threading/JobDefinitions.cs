using System;
using System.Collections.Generic;

namespace Armat.Threading;

/// <summary>
/// Represents the current stage in the lifecycle of a <see cref="Job"/>.
/// </summary>
public enum JobStatus
{
	/// <summary>
	/// The job has been initialized but has not yet been scheduled.
	/// </summary>
	Created,
	/// <summary>
	/// The job is waiting to be activated and scheduled internally by the .NET Framework infrastructure.
	/// </summary>
	WaitingForActivation,
	/// <summary>
	/// The job has been scheduled for execution but has not yet begun executing.
	/// </summary>
	WaitingToRun,
	/// <summary>
	/// The job is running but has not yet completed.
	/// </summary>
	Running,
	/// <summary>
	/// The job has finished executing and is implicitly waiting for
	/// attached child jobs to complete.
	/// </summary>
	WaitingForChildrenToComplete,
	/// <summary>
	/// The job completed execution successfully.
	/// </summary>
	RanToCompletion,
	/// <summary>
	/// The job acknowledged cancellation by throwing an OperationCanceledException with its own CancellationToken
	/// while the token was in signaled state, or the job's CancellationToken was already signaled before the
	/// job started executing.
	/// </summary>
	Canceled,
	/// <summary>
	/// The job completed due to an unhandled exception.
	/// </summary>
	Faulted
}

/// <summary>
/// Specifies flags that control optional behavior for the creation and execution of jobs.
/// </summary>
// NOTE: These options are a subset of JobContinuationsOptions, thus before adding a flag check it is
// not already in use.
[Flags]
public enum JobCreationOptions
{
	/// <summary>
	/// Specifies that the default behavior should be used.
	/// </summary>
	None = 0x0,

	/// <summary>
	/// A hint to a <see cref="Armat.Threading.JobScheduler">JobScheduler</see> to schedule a
	/// job in as fair a manner as possible, meaning that jobs scheduled sooner will be more likely to
	/// be run sooner, and jobs scheduled later will be more likely to be run later.
	/// </summary>
	PreferFairness = 0x01,

	/// <summary>
	/// Specifies that a job will be a long-running, course-grained operation. It provides a hint to the
	/// <see cref="Armat.Threading.JobScheduler">JobScheduler</see> that oversubscription may be
	/// warranted.
	/// </summary>
	LongRunning = 0x02,

	/// <summary>
	/// Specifies that a job is attached to a parent in the job hierarchy.
	/// </summary>
	AttachedToParent = 0x04,

	/// <summary>
	/// Specifies that an InvalidOperationException will be thrown if an attempt is made to attach a child job to the created job.
	/// </summary>
	DenyChildAttach = 0x08,

	/// <summary>
	/// Prevents the ambient pool from being seen as the current scheduler in the created job.  This means that operations
	/// like StartNew or ContinueWith that are performed in the created job will see Armat.Threading.JobScheduler.Default as the current pool.
	/// </summary>
	HideScheduler = 0x10,

	// 0x20 is already being used in JobContinuationOptions

	/// <summary>
	/// Forces continuations added to the current job to be executed asynchronously.
	/// This option has precedence over JobContinuationOptions.ExecuteSynchronously
	/// </summary>
	RunContinuationsAsynchronously = 0x40,

	/// <summary>
	/// Specifies that the started job should be executed synchronously. With this option
	/// specified, the start will be run on the same thread that triggered the job.
	/// Only very short-running continuations should be executed synchronously.
	/// </summary>
	RunSynchronously = 0x80000
}

/// <summary>
/// Specifies flags that control optional behavior for the creation and execution of continuation jobs.
/// </summary>
// NOTE: These options are a superset of JobCreationOptions, thus before adding a flag check it is
// not already in use.
[Flags]
public enum JobContinuationOptions
{
	/// <summary>
	/// Default = "Continue on any, no job options, run asynchronously"
	/// Specifies that the default behavior should be used.  Continuations, by default, will
	/// be scheduled when the antecedent task completes, regardless of the task's final <see
	/// cref="Armat.Threading.JobStatus">JobStatus</see>.
	/// </summary>
	None = 0,

	/// <summary>
	/// A hint to a <see cref="Armat.Threading.JobScheduler">JobScheduler</see> to schedule a
	/// job in as fair a manner as possible, meaning that jobs scheduled sooner will be more likely to
	/// be run sooner, and jobs scheduled later will be more likely to be run later.
	/// </summary>
	PreferFairness = 0x01,

	/// <summary>
	/// Specifies that a job will be a long-running, course-grained operation. It provides a hint to the
	/// <see cref="Armat.Threading.JobScheduler">JobScheduler</see> that oversubscription may be
	/// warranted.
	/// </summary>
	LongRunning = 0x02,
	/// <summary>
	/// Specifies that a job is attached to a parent in the job hierarchy.
	/// </summary>
	AttachedToParent = 0x04,

	/// <summary>
	/// Specifies that an InvalidOperationException will be thrown if an attempt is made to attach a child job to the created job.
	/// </summary>
	DenyChildAttach = 0x08,
	/// <summary>
	/// Prevents the ambient scheduler from being seen as the current scheduler in the created job.  This means that operations
	/// like StartNew or ContinueWith that are performed in the created job will see JobScheduler.Default as the current scheduler.
	/// </summary>
	HideScheduler = 0x10,

	/// <summary>
	/// In the case of continuation cancellation, prevents completion of the continuation until the antecedent has completed.
	/// </summary>
	LazyCancellation = 0x20,

	RunContinuationsAsynchronously = 0x40,

	// These are specific to continuations

	/// <summary>
	/// Specifies that the continuation job should not be scheduled if its antecedent ran to completion.
	/// This option is not valid for multi-job continuations.
	/// </summary>
	NotOnRanToCompletion = 0x10000,
	/// <summary>
	/// Specifies that the continuation job should not be scheduled if its antecedent threw an unhandled
	/// exception. This option is not valid for multi-job continuations.
	/// </summary>
	NotOnFaulted = 0x20000,
	/// <summary>
	/// Specifies that the continuation job should not be scheduled if its antecedent was canceled. This
	/// option is not valid for multi-job continuations.
	/// </summary>
	NotOnCanceled = 0x40000,
	/// <summary>
	/// Specifies that the continuation job should be scheduled only if its antecedent ran to
	/// completion. This option is not valid for multi-job continuations.
	/// </summary>
	OnlyOnRanToCompletion = NotOnFaulted | NotOnCanceled,
	/// <summary>
	/// Specifies that the continuation job should be scheduled only if its antecedent threw an
	/// unhandled exception. This option is not valid for multi-job continuations.
	/// </summary>
	OnlyOnFaulted = NotOnRanToCompletion | NotOnCanceled,
	/// <summary>
	/// Specifies that the continuation job should be scheduled only if its antecedent was canceled.
	/// This option is not valid for multi-job continuations.
	/// </summary>
	OnlyOnCanceled = NotOnRanToCompletion | NotOnFaulted,
	/// <summary>
	/// Specifies that the continuation job should be executed synchronously. With this option
	/// specified, the continuation will be run on the same thread that causes the antecedent job to
	/// transition into its final state. If the antecedent is already complete when the continuation is
	/// created, the continuation will run on the thread creating the continuation. Only very
	/// short-running continuations should be executed synchronously.
	/// </summary>
	RunSynchronously = 0x80000
}

// Type of exceptions handled by the Jobs in a special manner
// Inner exceptions of TransientAggregateException are added to the Job Exception list in a expanded way
// i.e. When thrown, instead of reporting one TransientAggregateException, the Job will report the InnerExeptions as it's own
public class TransientAggregateException : AggregateException
{
	//
	// Summary:
	//     Initializes a new instance of the Armat.Threading.TransientAggregateException class with a system-supplied
	//     message that describes the error.
	public TransientAggregateException() : base() { }
	//
	// Summary:
	//     Initializes a new instance of the Armat.Threading.TransientAggregateException class with references
	//     to the inner exceptions that are the cause of this exception.
	//
	// Parameters:
	//   innerExceptions:
	//     The exceptions that are the cause of the current exception.
	//
	// Exceptions:
	//   T:System.ArgumentNullException:
	//     The innerExceptions argument is null.
	//
	//   T:System.ArgumentException:
	//     An element of innerExceptions is null.
	public TransientAggregateException(IEnumerable<Exception> innerExceptions) : base(innerExceptions) { }
	//
	// Summary:
	//     Initializes a new instance of the Armat.Threading.TransientAggregateException class with references
	//     to the inner exceptions that are the cause of this exception.
	//
	// Parameters:
	//   innerExceptions:
	//     The exceptions that are the cause of the current exception.
	//
	// Exceptions:
	//   T:System.ArgumentNullException:
	//     The innerExceptions argument is null.
	//
	//   T:System.ArgumentException:
	//     An element of innerExceptions is null.
	public TransientAggregateException(params Exception[] innerExceptions) : base(innerExceptions) { }
	//
	// Summary:
	//     Initializes a new instance of the Armat.Threading.TransientAggregateException class with a specified
	//     message that describes the error.
	//
	// Parameters:
	//   message:
	//     The message that describes the exception. The caller of this constructor is required
	//     to ensure that this string has been localized for the current system culture.
	public TransientAggregateException(String message) : base(message) { }
	//
	// Summary:
	//     Initializes a new instance of the Armat.Threading.TransientAggregateException class with a specified
	//     error message and references to the inner exceptions that are the cause of this
	//     exception.
	//
	// Parameters:
	//   message:
	//     The error message that explains the reason for the exception.
	//
	//   innerExceptions:
	//     The exceptions that are the cause of the current exception.
	//
	// Exceptions:
	//   T:System.ArgumentNullException:
	//     The innerExceptions argument is null.
	//
	//   T:System.ArgumentException:
	//     An element of innerExceptions is null.
	public TransientAggregateException(String message, IEnumerable<Exception> innerExceptions) : base(message, innerExceptions) { }
	//
	// Summary:
	//     Initializes a new instance of the Armat.Threading.TransientAggregateException class with a specified
	//     error message and a reference to the inner exception that is the cause of this
	//     exception.
	//
	// Parameters:
	//   message:
	//     The message that describes the exception. The caller of this constructor is required
	//     to ensure that this string has been localized for the current system culture.
	//
	//   innerException:
	//     The exception that is the cause of the current exception. If the innerException
	//     parameter is not null, the current exception is raised in a catch block that
	//     handles the inner exception.
	//
	// Exceptions:
	//   T:System.ArgumentNullException:
	//     The innerException argument is null.
	public TransientAggregateException(String message, Exception innerException) : base(message, innerException) { }
	//
	// Summary:
	//     Initializes a new instance of the Armat.Threading.TransientAggregateException class with a specified
	//     error message and references to the inner exceptions that are the cause of this
	//     exception.
	//
	// Parameters:
	//   message:
	//     The error message that explains the reason for the exception.
	//
	//   innerExceptions:
	//     The exceptions that are the cause of the current exception.
	//
	// Exceptions:
	//   T:System.ArgumentNullException:
	//     The innerExceptions argument is null.
	//
	//   T:System.ArgumentException:
	//     An element of innerExceptions is null.
	public TransientAggregateException(String message, params Exception[] innerExceptions) : base(message, innerExceptions) { }
	//
	// Summary:
	//     Initializes a new instance of the Armat.Threading.TransientAggregateException class with serialized
	//     data.
	//
	// Parameters:
	//   info:
	//     The object that holds the serialized object data.
	//
	//   context:
	//     The contextual information about the source or destination.
	//
	// Exceptions:
	//   T:System.ArgumentNullException:
	//     The info argument is null.
	//
	//   T:System.Runtime.Serialization.SerializationException:
	//     The exception could not be deserialized correctly.
	protected TransientAggregateException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
