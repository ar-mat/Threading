# Armat Threading Library

[![NuGet](https://img.shields.io/nuget/v/armat.threading.svg)](https://www.nuget.org/packages/armat.threading/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

`Armat.Threading` is a powerful alternative to the *.NET Task Parallel Library (TPL)* that provides fine-grained control over asynchronous execution. The `Job` and `Job<T>` classes offer functionality similar to `Task` and `Task<T>`, with enhanced capabilities for multi-scheduler scenarios and context propagation.

## Why Choose Armat.Threading?

- 🔀 **Multiple Thread Pools**: Create independent thread pools (`JobScheduler` instances) within a single application for isolated workload management
- 🔗 **Context Propagation**: `JobRuntimeScope` enables seamless passing of contextual data through async call chains
- 🎯 **Familiar API**: Consistent with `System.Threading.Tasks` APIs for easy adoption by .NET developers
- ⚡ **Async-Await Support**: Full compatibility with C# async-await patterns
- 🔧 **Extensible**: Clear, maintainable codebase designed for customization
- 📊 **Observable**: Built-in statistics and monitoring capabilities
- 🚀 **Performance**: Designed to match or exceed TPL performance

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
  - [Job and Job\<T\>](#job-and-jobt)
  - [JobScheduler](#jobscheduler)
  - [JobRuntimeScope](#jobruntimescope)
  - [CorrelationIdScope](#correlationidscope)
- [Advanced Usage](#advanced-usage)
- [Resources](#resources)
- [License](#license)

## Installation

Install the package via NuGet:

```bash
dotnet add package armat.threading
```

Or via Package Manager:

```powershell
Install-Package armat.threading
```

## Quick Start

Here's a simple example to get you started:

```cs
using Armat.Threading;

// Run a simple asynchronous job
await Job.Run(() =>
{
    Console.WriteLine($"Running on thread {Thread.CurrentThread.ManagedThreadId}");
}).ConfigureAwait(false);

// Run a job with a return value
int result = await Job<int>.Run(() =>
{
    return 42;
}).ConfigureAwait(false);

Console.WriteLine($"Result: {result}");
```

## Core Concepts

### Job and Job\<T\>

`Job` and `Job<T>` are the fundamental types for asynchronous operations in Armat.Threading, equivalent to `Task` and `Task<T>` in the standard .NET TPL.

#### Key Features

- Full async-await support with configurable context capture
- Cancellation token support
- Exception handling and aggregation
- Job continuations and parent-child relationships
- Custom scheduling options

#### Important

⚠️ Always use `ConfigureAwait()` when awaiting Jobs. The parameterless `GetAwaiter()` method is obsolete.

#### Basic Examples

```cs
    // run a Job with no args and no return value
    await Job.Run(
        () =>
        {
            Console.WriteLine("Running job asynchronously");
        }).ConfigureAwait(false);

    // run a Job with args but no return value
    await Job.Run(
        (Object? state) =>
        {
            Double number = (Double)state!;
            Console.WriteLine("Running job asynchronously with arg {0}", number);
        }, 0.1248).ConfigureAwait(false);

    // run a Job with no args and returning T
    T result = await Job<T>.Run(
        () =>
        {
            Console.WriteLine("Running job asynchronously");
            return default(T);
        }).ConfigureAwait(false);

    // run a Job with args and returning T
    T result = await Job<T>.Run(
        (Object? state) =>
        {
            Double number = (Double)state!;
            Console.WriteLine("Running job asynchronously with arg {0}", number);
            return new T(number);
        }, 0.1248).ConfigureAwait(false);
```

There are several overloads of instantiating and running the jobs. It is possible to

- ***Run a Job with a given cancellation token.***
This makes possible to request *Job* cancellation from the other thread. *Job* execution methods should properly handle the cancellation request and stop the asynchronous execution.

- ***Run a Job within a given JobScheduler.***
There may be multiple *JobScheduler* instances within a single .Net application. *JobScheduler*s can be used to run asynchronous operation, and all continuations of a *Job* will derive (run within) the same *JobScheduler* unless explicitly specified otherwise.

- ***Pass an argument (state) to the Job.***
The state is defined as an *Object* and may be used to pass arguments to the asynchronous operation triggered by the *JobScheduler*.

- ***Get the result (return value) of a Job upon completion.***
Generic implementations of *Job\<TResult\>* are running a function with a return value of type *TResult*. The result of a *Job* becomes available once it's completed successfully.

- ***Create a Job with custom JobCreationOptions.***

The `JobCreationOptions` enumeration provides flags that can be combined to control job execution behavior:

| Option | Description |
|--------|-------------|
| `LongRunning` | Marks the job as long-running, requesting a dedicated thread. The scheduler has a limited pool for long-running jobs and queues them when all threads are busy. |
| `AttachedToParent` | Runs the job immediately after its parent completes on the same thread. Parent status becomes `WaitingForChildrenToComplete` during child execution. |
| `DenyChildAttach` | Prevents child jobs from attaching to this parent job. This flag overrides the `AttachedToParent` flag given to the child jobs. |
| `HideScheduler` | Forces use of the default `JobScheduler` even if the initiator job runs in a different scheduler. |
| `RunContinuationsAsynchronously` | Ensures continuations run asynchronously regardless of continuation flags. |
| `RunSynchronously` | Executes the job synchronously in the calling thread, blocking until completion. |

During execution of a *Job*, there's a static property `Armat.Threading.Job.Current` to be used for identifying the instance of currently running *Job*.

The `Initiator` property returns the the instance of parent *Job* in context of which this one has been triggered. The property `Armat.Threading.Job.Root` returns the top level Job from which the asynchronous operation has begun.

I'm not going to describe the whole interface of `Armat.Threading.Job` class taking into account that it generally matches to the `System.Threading.Tasks.Task` from .Net CLR.

Below is a simple example of running an asynchronous operation using jobs:

```cs
    public Int32 Sum(Int32[] array)
    {
        // create the Job
        Job<Int32> asyncJob = new Job<Int32>(SumFn, array);

        // Run asynchronously
        asyncJob.Run();

        // wait for it to finish and return the result
        return asyncJob.Result;
    }

    private Int32 SumFn(Object? args)
    {
        Int32[] array = (Int32[])args;

        return array.Sum();
    }
```

### JobScheduler

The `JobScheduler` class manages thread pools and job execution queues. It derives from `JobSchedulerBase` and implements the `IJobScheduler` interface.

#### Architecture Overview

```
┌─────────────────────────────────────┐
│      IJobScheduler Interface        │
├─────────────────────────────────────┤
│  + Default (static)                 │
│  + Current (static)                 │
│  + Enqueue(job)                     │
│  + Cancel(job)                      │
│  + EnterScope()                     │
└─────────────────────────────────────┘
              ▲
              │ implements
              │
┌─────────────┴───────────────────────┐
│    JobSchedulerBase (abstract)      │
├─────────────────────────────────────┤
│  + EnterScope()                     │
│  # UpdateJobStatus()                │
│  # ExecuteJobProcedure()            │
└─────────────────────────────────────┘
              ▲
              │ extends
              │
┌─────────────┴───────────────────────┐
│         JobScheduler                │
├─────────────────────────────────────┤
│  + Statistics                       │
│  + MethodBuilderStatistics          │
│  - Thread Pool Management           │
│  - Job Queue Management             │
└─────────────────────────────────────┘
```

#### IJobScheduler Interface

- `static IJobScheduler Default { get; }` static property returns the default instance of *IJobScheduler*.
- `static IJobScheduler Current { get; }` static property returns the instance of *IJobScheduler* which is currently running a *Job* on the caller thread, or `IJobScheduler.Default` otherwise. See `JobSchedulerScope` for more information about how to change the `Current` job scheduler.
- `void Enqueue(Job job)` enqueues a *Job* in the scheduler. To successfully enqueue a *Job* in a *JobScheduler*, the job must have `Job.Status = JobStatus.Created` (never run before).
- `Boolean Cancel(Job job)` cancels *Job* execution in the *JobScheduler* before it begins. The method returns false if the *Job* is already running or finished.
- `Int32 PendingJobsCount { get; }` property returns the number of jobs currently waiting in the queue. Use this to monitor the current load on the *JobScheduler*.
- `JobSchedulerScope EnterScope()` updates the `IJobScheduler.Current` property to point to this instance for the executing thread. The scope is reset when the returned `JobSchedulerScope` is disposed.

**Armat.Threading.JobSchedulerBase abstract class**

The class `Armat.Threading.JobSchedulerBase` provides means for changing the *Default* and *Current* Job Schedulers:

- `public static IJobScheduler Default { get; protected set; }` gets or sets the default IJobScheduler. If not set, returns `JobScheduler.WithDefaultConfiguration`.
    - Note: To protect setting the default JobScheduler by arbitrary code, the setter is made protected, requiring a public setter in derived classes.
    - Note: JobScheduler.Default can be set only once during process lifetime.
- `public JobSchedulerScope EnterScope()` updates the `IJobScheduler.Current` property to point to this job scheduler in context of the calling thread.
    - Note: The class `JobSchedulerScope` implements IDisposable interface and automatically restores the previous `IJobScheduler.Current` value upon disposal.

The below example illustrates how to set the *Current* *JobScheduler* within a given scope.
```cs
    private Int32 ImplicitJobExecutionInCustomScheduler()
    {
        // this is the scheduler to be used in scope of the given method
        using JobScheduler ajs = new("Async Job Scheduler (ajs)");

        // After this line all Jobs will be executed by ajs scheduler (unless overridden by another one)
        // The scope will be disposed when exiting the method, and the previous IJobScheduler.Current will be restored
        using var scope = ajs.EnterScope();

        // create the Job
        Job<Int32> asyncJob = new Job<Int32>(SumFn, new Int32[] { 1, 2, 3 });

        // Run asynchronously
        asyncJob.Run();

        // wait for it to finish and return the result
        return asyncJob.Result;
	}

    private Int32 SumFn(Object? args)
    {
        Int32[] array = (Int32[])args;

        return array.Sum();
    }
```

**Armat.Threading.JobScheduler class**

`Armat.Threading.JobScheduler` is the default implementation of the `Armat.Threading.IJobScheduler` interface.
It can be constructed with a `JobSchedulerConfiguration` argument to configure:
- Scheduler name (used for naming threads)
- Minimum and maximum number of threads for asynchronous operations
- Maximum number of long-running threads
- Maximum size of job queues within the scheduler

The scheduler provides convenient constructors for common scenarios and access to a default configuration via `JobScheduler.WithDefaultConfiguration`.

On top of implementing the *IJobScheduler* interface, the `JobScheduler` class provides properties to retrieve statistics of Jobs queued within the scheduler. See the `Statistics` and `MethodBuilderStatistics` properties for detailed performance metrics.

### JobRuntimeScope

The `JobRuntimeScope` class enables context propagation through asynchronous call chains. It maintains key-value pairs that automatically flow through nested async operations, regardless of call depth or threading complexity.

#### Use Cases

- **Correlation Tracking**: Maintain correlation IDs across distributed async operations
- **Execution Tracing**: Track and log asynchronous execution flows
- **Context Delivery**: Pass user identity, tenant info, or other context to nested methods
- **Request Scoping**: Maintain request-specific data in web applications

#### How It Works

1. Create a scope with `JobRuntimeScope.Enter<T>()`
2. The scope captures current context and makes data available to all nested calls
3. Data remains accessible through `GetValue<T>()` at any depth
4. Scope automatically cleans up when disposed (via `using` statement)

Some examples of using JobRuntimeScope are available as unit tests [here](https://github.com/ar-mat/Threading/blob/master/Projects/ThreadingTest/RuntimeScope.cs). Below is another example of the same:
```cs
    private async Job DoSomething()
    {
        // run some user scoped operation
        await RunUserScopedOperation().ConfigureAwait(false);

        // user data is null here because there's no JobRuntimeScope defined at this level
        UserData? userData = JobRuntimeScope.GetValue<UserData>();
    }
    private async Job RunUserScopedOperation()
    {
        // create the scope with some UserData information
        // 'using' keyword guarantees to have the scope Disposed when exiting the method
        using var scope = JobRuntimeScope.Enter<UserData>(() => new UserData("abc", "123"));

        // user data refers to the one declared above
        UserData? userData1 = JobRuntimeScope.GetValue<UserData>();

        // run any asynchronous operation
        // UserData will be accessible in all inner synchronous or asynchronous methods
        await AsyncOperation().ConfigureAwait(false);

        // user data remains the same as above
        UserData? userData2 = JobRuntimeScope.GetValue<UserData>();
    }
    private async Job AsyncOperation()
    {
        // user data remains the same as created in the caller method
        UserData? userData3 = JobRuntimeScope.GetValue<UserData>();

        // running some asynchronous operations
        await Job.Yield();

        // user data remains the same as created in the caller method
        UserData? userData3 = JobRuntimeScope.GetValue<UserData>();
    }
```

Following are members of the `Armat.Threading.JobRuntimeScope` class:

- `public static JobRuntimeScope Enter(String key, Func<Object> factory)` creates a *JobRuntimeScope* with the given *key* and uses the factory method to initialize the Value property.
    - Note: If the *key* already exists in the current scope, `JobRuntimeScope.Null` is returned to prevent duplicate scopes, and the factory method is never invoked.
- `public static JobRuntimeScope Enter<T>(String key, Func<T> factory) where T : class` is a generic overload of the *Enter* method.
- `public static JobRuntimeScope Enter<T>(Func<T> factory) where T : class` is a generic overload that uses the full name of type *T* as the key.
- `public static Object? GetValue(String key)` returns the value for the given key in the current scope. Returns *null* if a scope for the key is not found.
- `public static T? GetValue<T>(String key) where T : class` returns the value for the given key in the current scope. Returns *null* if a scope for the key is not found and casts the value to type *T*.
- `public static T? GetValue<T>() where T : class` is a generic overload that uses the full name of type *T* as the key.
- `public void Leave()` leaves the current scope by removing it from the thread context.
- `public void Dispose()` leaves the current scope as described in the *Leave()* method. Use with the *using* keyword to ensure proper disposal when exiting the method scope.
- `public Boolean IsNull { get; }` returns *true* for *NULL* (invalid) scope instances.
- `public String Key { get; }` property returns the *Key* of the *JobRuntimeScope*.
- `public Object Value { get; }` property returns the *Value* of the *JobRuntimeScope*.

**CorrelationIdScope class**

The class `CorrelationIdScope` is a value type for `JobRuntimeScope` that generates auto-incrementing IDs for correlation across asynchronous operations (for logging, tracing, or other needs). It provides convenient factory methods for instantiating `JobRuntimeScope` with a new `CorrelationIdScope` value:

- `CorrelationIdScope.Create()` creates a new scope with an auto-generated correlation ID
- `CorrelationIdScope.Create(String name)` creates a new scope with a named correlation ID
- `CorrelationIdScope.Current()` retrieves the current correlation scope (if any)
- `CorrelationIdScope.CurrentId()` retrieves just the numeric ID (or `InvalidID` if no scope exists)

The generic version `CorrelationIdScope<T>` allows for multiple independent correlation ID sequences in the same application.

Example usage:
```cs
    private async Job RunCorrelationIDTest(Int32 testNum)
    {
        // create correlation ID and the appropriate scope
        using var scope = CorrelationIdScope.Create();

        // any asynchronous code execution
        await Job.Yield();

        // correlation ID is available here
        Console.WriteLine("RunCorrelationIDTest: Correlation ID for test {0} is {1}",
            testNum,
            CorrelationIdScope.Current()!.CorrelationID);

        // nested method calls
        await NestedAsyncMethodCall(testNum, CorrelationIdScope.Current()!.CorrelationID, 1).ConfigureAwait(false);
    }

    private async Job NestedAsyncMethodCall(Int32 testNum, Int64 expectedCorrID, Int32 depth)
    {
        // any asynchronous code execution
        await Job.Yield();

        // correlation ID remains the same as in the caller method above
        Console.WriteLine("NestedAsyncMethodCall<{0}>: Correlation ID for test {1} is {2}",
            depth,
            testNum,
            CorrelationIdScope.Current()!.CorrelationID);

        // go even deeper
        if (depth < 3)
            await NestedAsyncMethodCall(testNum, expectedCorrID, depth + 1).ConfigureAwait(false);
    }
```
Appropriate test is available [here](https://github.com/ar-mat/Threading/blob/master/Projects/ThreadingTest/RuntimeScope.cs).

## Advanced Usage

### Creating Custom Job Schedulers

```cs
using Armat.Threading;

// Create a scheduler with custom configuration
var config = new JobSchedulerConfiguration
{
    Name = "HighPriorityScheduler",
    MinThreads = 2,
    MaxThreads = 8,
    MaxLongRunningThreads = 2,
    MaxJobsQueueSize = 1000,
    MaxLongRunningJobsQueueSize = 100
};

using var scheduler = new JobScheduler(config);

// Run jobs in the custom scheduler
await Job.Run(() =>
{
    Console.WriteLine("Running in custom scheduler");
}, CancellationToken.None, JobCreationOptions.None, scheduler)
.ConfigureAwait(false);
```

### Monitoring Job Statistics

```cs
var scheduler = new JobScheduler("MonitoredScheduler");

// Get real-time statistics
var stats = scheduler.Statistics;
Console.WriteLine($"Queued: {stats.QueuedCount}");
Console.WriteLine($"Running: {stats.RunningCount}");
Console.WriteLine($"Completed: {stats.CompletedCount}");
Console.WriteLine($"Failed: {stats.FaultedCount}");
```

### Error Handling

```cs
try
{
    await Job.Run(() =>
    {
        throw new InvalidOperationException("Something went wrong");
    }).ConfigureAwait(false);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Job failed: {ex.Message}");
}
```

### Combining Multiple Jobs

```cs
// Wait for all jobs to complete
var job1 = Job.Run(() => Thread.Sleep(100));
var job2 = Job.Run(() => Thread.Sleep(200));
var job3 = Job.Run(() => Thread.Sleep(150));

Job.WaitAll(job1, job2, job3);
Console.WriteLine("All jobs completed");

// Wait for any job to complete
int completedIndex = Job.WaitAny(job1, job2, job3);
Console.WriteLine($"Job {completedIndex} completed first");

// Use WhenAll for async scenarios
await Job.WhenAll(job1, job2, job3).ConfigureAwait(false);

// Use WhenAny for async scenarios
var completedJob = await Job.WhenAny(job1, job2, job3).ConfigureAwait(false);
```

## Summary

The `Armat.Threading` library provides a powerful and flexible asynchronous code execution scheduler that serves as an alternative to the .NET Task Parallel Library. Key highlights:

- **Full async-await support** with `Job` and `Job<T>` types
- **Multiple independent thread pools** within a single application
- **Context propagation** via `JobRuntimeScope` for tracing and contextual data
- **Production-ready** with comprehensive unit tests and performance benchmarks
- **.NET 8.0** target framework

Contributions are welcome! Please report bugs, suggest improvements, or submit pull requests.

## Resources

- **NuGet Package**: [armat.threading](https://www.nuget.org/packages/armat.threading/)
- **Source Code**: [GitHub Repository](https://github.com/ar-mat/Threading)
- **Unit Tests**: [Threading Tests](https://github.com/ar-mat/Threading/tree/master/Projects/ThreadingTest)
- **Article**: [Powerful Alternative to .NET TPL on CodeProject](https://www.codeproject.com/articles/Powerful-Alternative-to-NET-TPL)
- **Project Website**: [armat.am/products/threading](http://armat.am/products/threading)

## License

This project is licensed under the MIT License.