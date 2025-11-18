# Armat Threading Library

`Armat.Threading` library is an alternative implementation of *.Net Task Parallel Library (TPL)*. `Armat.Threading.Job` & `Armat.Threading.Job<T>` are the main classes to run asynchronous operations - similar to the `Task` and `Task<T>` in the `System.Threading.Tasks` namespace. Following are the main benefits of using `Armat.Threading`:

- Ability to create multiple thread pools (instances of `Armat.Threading.JobScheduler`s) within a single .Net process.
- Introduction of the notion of `JobRuntimeScope` enables carrying user-defined data (context) across hierarchy of asynchronous operations.
- Consistency of public APIs exposed by `Armat.Threading.Job` and `System.Threading.Tasks.Task` classes ensures easy adoption by .Net developers.
- Support of *async-await* notation for `Job` and `Job<T>` classes (fully compatible with .Net *Tasks*).
- Simpler and more extensible codebase from the one available in *.Net TPL*. `Armat.Threading` library sources may be used for better understanding asynchronous programming mechanisms in *.Net*, and extend it to fulfill application demands.

The library has been developed to allow creation of multiple independent thread pools in a single *.Net* application, and has been further enhanced to cover more real-life scenarios. It has been designed with performance and flexibility in mind and should produce comparable results with *.Net TPL* (see appropriate performance unit tests). Below are details about the major classes and some usage examples of `Armat.Threading` library.

## Armat.Threading.Job and Armat.Threading.Job\<T\> classes

`Armat.Threading.Job` and `Armat.Threading.Job<T>` correspond to the `System.Threading.Tasks.Task` and `System.Threading.Tasks.Task<T>` classes in *.Net CLR*. Those have similar interfaces and can be used to trigger an asynchronous operations in .Net process.
Below are some examples of scheduling asynchronous operations:

```cs
    // run a Job with no args and no return value
    await Job.Run(
        () ->
        {
            Console.WriteLine("Running job asynchronously");
        }).ConfigureAwait(false);

    // run a Job with args but no return value
    await Job.Run(
        (double number) ->
        {
            Console.WriteLine("Running job asynchronously with arg {0}", number);
        }, 0.1248).ConfigureAwait(false);

    // run a Job with no args and returning T
    T result = await Job<T>.Run(
        () ->
        {
            Console.WriteLine("Running job asynchronously");
            return default(T);
        }).ConfigureAwait(false);

    // run a Job with args and returning T
    T result = await Job<T>.Run(
        (double number) ->
        {
            Console.WriteLine("Running job asynchronously with arg {0}", number);
            return new T(0.1248));
        }, 0.1248).ConfigureAwait(false);
```

There are several overloads of instantiating and running the jobs. It is possible to

- ***Run a Job with a given cancellation token.***
This makes possible to request *Job* cancellation from the other thread. *Job* execution methods should properly handle the cancellation request and stop the asynchronous execution.

- ***Run a Job within a given JobScheduler.***
There may be multiple *JobScheduler* instances within a single .Net application. Any instance of *JobScheduler* can be used to run asynchronous operation, and all continuations of a *Job* will derive (run within) the same *JobScheduler* unless explicitly specified otherwise.

- ***Pass an argument (state) to the Job.***
The state is defined as an *Object* and may be used to pass arguments to the asynchronous operation triggered by the *JobScheduler*.

- ***Get the result (return value) of a Job upon completion.***
Generic implementations of *Job\<TResult\>* are running a function with a return value of type *TResult*. The result of a *Job* becomes available once it's completed successfully.

- ***Create a Job with custom JobCreationOptions.***
There are several flags in *JobCreationOptions* enumeration which can be used in conjunction:

    - ***JobCreationOptions.LongRunning*** marks the *Job* as a long running one, thus requesting a dedicated  thread pool for its execution. The default implementation of *JobScheduler* has a limited number of threads for long running jobs, and will queue the execution if all are busy.

    - ***JobCreationOptions.AttachedToParent*** marks the job to run right after the parent job completes using the same thread. While executing child Jobs with *JobCreationOptions.AttachedToParent* flag, the status of parent job becomes *JobStatus.WaitingForChildrenToComplete*.

    - ***JobCreationOptions.DenyChildAttach*** can be set on the parent job to ensure that none of the children will run attached to it. This flag overloads the *JobCreationOptions.AttachedToParent* flag applied to the children.

    - ***JobCreationOptions.HideScheduler*** indicates to use the default instance of *JobScheduler* even if the initiator Job has been triggered in a different one. By default (if the flag *JobCreationOptions.HideScheduler* is not specified) the current *Job* runs in the same scheduler as the initiator.

    - ***JobCreationOptions.RunContinuationsAsynchronously*** ensures to run *Job* continuations asynchronously irrespective of the *JobCreationOptions.RunSynchronously* flag on the continuation Jobs.

    - ***JobCreationOptions.RunSynchronously*** instructs *JobScheduler* to run the *Job* within the calling thread. Job run method invocation will not return until the *Job* is finished.

During execution of a *Job*, there's a static property `Armat.Threading.Job.Current` to be used for identifying the instance of currently running *Job*.

There's an `Initiator` property referring to the instance of parent *Job* in context of which this one has been triggered. The property `Armat.Threading.Job.Root` returns the top level Job from which the asynchronous operation has begun.

I'm not going to describe the whole interface of `Armat.Threading.Job` class taking into account that it matches to the `System.Threading.Tasks.Task` from .Net CLR.

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

## Armat.Threading.JobScheduler class

The class `Armat.Threading.JobScheduler` derives from `Armat.Threading.JobSchedulerBase`. It provides the default implementation of asynchronous jobs scheduling mechanism declared by `Armat.Threading.IJobScheduler` interface. It is recommended to use *IJobScheduler* interface for queueing the jobs. Job Scheduler classes are defined as follows:

**Armat.Threading.IJobScheduler interface**

- `static IJobScheduler Default { get; }` static property returns the default instance of *IJobScheduler*.
- `static IJobScheduler Current` static property returns the instance of *IJobScheduler* which is currently running a *Job* on the caller thread, or the `IJobScheduler.Default` otherwise. See `JobSchedulerBase` for more information about how to change the `Current` job scheduler.
- `void Enqueue(Job job)` enqueues a *Job* in a scheduler. To successfully enqueue a *Job* in a *JobScheduler* one must have `Job.Status = JobStatus.Created` (never run before).
- `Boolean Cancel(Job job)` cancels *Job* execution in the *JobScheduler* before it begins. The method will fail (will return false) if the *Job* is already running or finished.
- `Int32 PendingJobsCount { get; }` property returns number of jobs currently waiting in the queue. It may be used to monitor the current load on the *JobScheduler*.

**Armat.Threading.JobSchedulerBase abstract class**

The class `Armat.Threading.JobSchedulerBase` provides means for changing the *Default* and *Current* Job Schedulers as shown below:

- `public static IJobScheduler Default { get; protected set; }` gets or sets the default IJobScheduler. If not set, the `JobScheduler.Default` is returned.
    - Note: To protect setting the default JobScheduler by an arbitrary code, the setter is made protected, thus requiring a public setter in a derived class.
    - Note: JobScheduler.Default can be set only once during process lifetime.
- `public JobSchedulerRuntimeScope EnterScope()` updates the `IJobScheduler.Current` property to point to the current job scheduler in context of the calling thread.
- `public void LeaveScope(in JobSchedulerRuntimeScope scope)` restores the `IJobScheduler.Current` property to the previous value (the one before entering the scope).
    - Note: The class `JobSchedulerRuntimeScope` implements IDisposable interface, and it automatically calls the *LeaveScope* method upon disposal.

The below example illustrates how to set the *Current* *JobScheduler* within a given scope.
```cs
    private Int64 ImplicitJobExecutionInCustomScheduler()
    {
        // this is the scheduler to be used in scope of the given method
        using JobScheduler ajs = new("Async Job Scheduler (ajs)");

        // After this line all Jobs will be executed by ajs scheduler (unless overridden by another one)
        // ajs will be disposed when existing the scope, and the previous IJobScheduler.Current will be restored
        using var scope = scheduler.EnterScope();

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

`Armat.Threading.JobScheduler` is teh default implementation of `Armat.Threading.IJobScheduler` interface.
It can be constructed with `JobSchedulerConfiguration` argument to provide the name for *JobScheduler* (to be used for naming the threads), limit number of threads for asynchronous operations, as well as limit size of the *Job* queues within the scheduler.

On top of implementing *IJobScheduler* interface `Armat.Threading.JobScheduler` class also provides properties to retrieve statistics of Jobs queued within the scheduler. See `Statistics` and `MethodBuilderStatistics` properties for more information.

## JobRuntimeScope class

The class represents a scope of an asynchronous operation. It begins at the moment of instantiation of `JobRuntimeScope` object and ends with its disposal (generally after completing the asynchronous operation). It represents a pair of String *Key* and an Object *Value*. *JobRuntimeScope* can be retrieved via static accessors during an asynchronous code execution (irrespective of the number & depth of asynchronous calls).

Using `JobRuntimeScope` one can deliver parameters to the nested asynchronous methods, thus providing contextual information about the invocation. Some good examples of using `JobRuntimeScope` are:

- identifying correlation of Jobs
- tracing asynchronous code execution
- delivering contextual information to the nested methods

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

Following are members of `Armat.Threading.JobRuntimeScope` class:

- `public static JobRuntimeScope Enter(String key, Func<Object> factory)` instantiates an object of *JobRuntimeScope* type with the given *key* and uses the factory method to initialize the Value property.
    - Note: In case if the *key* is found in the current scope, the existing *JobRuntimeScope* is returned, and the factory method is never invoked to create a new one.
- `public static JobRuntimeScope Enter<T>(String key, Func<T> factory)` is an overloaded generic version *JobRuntimeScope.Enter* method.
- `public static JobRuntimeScope Enter<T>(Func<T> factory)` is an overloaded generic version *JobRuntimeScope.Enter* method which uses T type as a key for creating the scope.
- `public static JobRuntimeScope EnterNew(String key, Func<Object> factory)` instantiates an object of *JobRuntimeScope* type with the given *key* and uses the factory method to initialize the Value property.
    - Note: In case if the *key* already exists in the scope, a `JobRuntimeScope.Null` is returned to indicate a failure result, and the factory method is never invoked to create a new one.
- `public static JobRuntimeScope EnterNew<T>(String key, Func<T> factory)` is an overloaded generic version *JobRuntimeScope.EnterNew* method.
- `public static JobRuntimeScope EnterNew<T>(Func<T> factory)` is an overloaded generic version *JobRuntimeScope.EnterNew* method which T type as a key for creating the scope.
- `public static Object? GetValue(String key)` returns value for the given key in the current scope. Will return *null* if a scope for the key is not found.
- `public static T? GetValue<T>(String key)` returns value for the given key in the current scope. Will return *null* if a scope for the key is not found and will throw an exception if the *Value* is not assignable to *T*.
- `public static T? GetValue<T>()` is an overloaded generic version *JobRuntimeScope.GetValue* method which uses T type as a key.
- `public void Leave()` leaves the current scope by removing the appropriate key.
- `public void Dispose()` leaves the current scope as described in *Leave()* method. May be used with *using* keyword for ensuring proper disposal when exiting the method scope.
- `public Boolean IsNull` returns *true* for *NULL* (invalid) scope instances.
- `public String Key { get; }` property returns the *Key* of *JobRuntimeScope*.
- `public Object Value { get; }` property returns the *Value* of *JobRuntimeScope*.

**CorrelationIdScope class**

The class `CorrelationIdScope` is one of possible value types for `JobRuntimeScope`. It generates auto-incrementing IDs to be used for correlation across asynchronous operations (for logging, tracing or any other needs). It provides convenient factory methods for instantiating `JobRuntimeScope` with a new `CorrelationIdScope` value as shown below:
```cs
    private async Job RunCorrelationIDTest(Int32 testNum)
    {
        // create correlation ID and the appropriate scope
        using var scope = CorrelationIdScope.Create();

        // any asynchronous code execution
        await Job.Yield();

        // correlation ID is available here
        Output.WriteLine("RunCorrelationIDTest: Correlation ID for test {0} is {1}",
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
        Output.WriteLine("NestedAsyncMethodCall<{0}>: Correlation ID for test {1} is {2}",
            depth,
            testNum,
            CorrelationIdScope.Current()!.CorrelationID);

        // go even deeper
        if (depth < 3)
            await NestedAsyncMethodCall(testNum, expectedCorrID, depth + 1).ConfigureAwait(false);
    }
```
Appropriate test is available [here](https://github.com/ar-mat/Threading/blob/master/Projects/ThreadingTest/RuntimeScope.cs).

# Summary

I hope the asynchronous code execution scheduler will inspire you to use it in own projects. I have one, and works quite well for me (I'm currently working to publish it for a wider audience).
I would appreciate any contributions in form of bug reports, improvement ideas or pull requests from the community.
You may find more information in my CodeProject article [here](https://www.codeproject.com/Articles/5383493/Powerful-Alternative-to-Net-TPL).