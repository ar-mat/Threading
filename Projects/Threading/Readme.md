# Armat Threading Library

`Armat.Threading` library is an alternative implementation of *.Net Task Parallel Library (TPL)*. `Armat.Threading.Job` & `Armat.Threading.Job<T>` are the main classes to run asynchronous operations - similar to the `Task` and `Task<T>` in the `System.Threading.Tasks` namespace. Following are the main benefits of using `Armat.Threading` vs standard TPL:

- Ability to instantiate multiple asynchronous *Job* execution thread pools (instances of `Armat.Threading.JobScheduler`s) within a single .Net process.
- There's a support of *Job* runtime scope. *Job* runtime scopes can be used to carry user-defined information during an asynchronous *Job* execution and are spawned deep to the nested jobs.
- `Job` and `Job<T>` have consistent APIs with TPL `Task` and `Task<T>` classes, and should be easily used by developers familiar with TPL.
- The same *async-await* notation for *Tasks* is applicable to `Job` and `Job<T>` classes.
- Simpler and more extensible codebase from the once available in *.Net TPL*. May be useful to better understand asynchronous programming mechanisms in *.Net CLR* and extend it to meet your own application demands.

The library has been developed to allow creation of multiple independent thread pools in a single *.Net* application, and has been further enhanced to cover more real-life scenarios. It has been designed with performance and flexibility in mind and should produce comparable results with *.Net TPL* (see appropriate performance test results). Below are details about the major classes and some usage examples of `Armat.Threading` library.

## Armat.Threading.Job and Armat.Threading.Job\<T\> classes

`Armat.Threading.Job` and `Armat.Threading.Job<T>` correspond to the `System.Threading.Tasks.Task` and `System.Threading.Tasks.Task<T>` classes in *System.Threading.Tasks* namespace. Those have similar interfaces and can be used to trigger an asynchronous operation in .Net process.
By default it uses the current *JobScheduler* (see *Armat.Threading.IJobScheduler.Current* for more details) for executing the jobs. Below are some examples of scheduling asynchronous operations:

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
This makes possible to request job cancellation from the other thread. job execution actions should properly handle the Cancellation Request and stop the asynchronous execution.

- ***Create a Job with custom jobCreationOptions.***
There are several flags in JobCreationOptions enumeration which can be used in conjunction:

    - ***JobCreationOptions.LongRunning*** marks the Job as a long running one, thus requesting a dedicated  thread pool for its execution. The default implementation of Job Scheduler has a limited number of threads dedicated to long running jobs, and and will queue those if all threads are busy.

    - ***JobCreationOptions.AttachedToParent*** marks the job to run right after the parent job is complete using the same thread. While executing child Jobs with *JobCreationOptions.AttachedToParent* flag, the status of parent job becomes *JobStatus.WaitingForChildrenToComplete*.

    - ***JobCreationOptions.DenyChildAttach*** can be set on the parent job to ensure that none of the children will run attached to it. This flag overloads the *JobCreationOptions.AttachedToParent* flag set on children.

    - ***JobCreationOptions.HideScheduler*** indicates to use the default *JobScheduler* even if the initiator Job has been triggered in a different one. By default (if the flag *JobCreationOptions.HideScheduler* is not specified) the current Job runs within the scheduler of the initiator Job.

    - ***JobCreationOptions.RunContinuationsAsynchronously*** ensures to run Job continuations asynchronously irrespective of the *JobCreationOptions.RunSynchronously* flag on the continuation Jobs.

    - ***JobCreationOptions.RunSynchronously*** instructs *JobScheduler* to run the Job within the calling thread context. The call will not return unless Job is finished.

- ***Run a Job within a given JobScheduler.***
There may be multiple *JobScheduler* instances within a single .Net application. Any instance of *JobScheduler* can be user to run an asynchronous operation, and all continuations of a Job will derive (run within) the same *JobScheduler* unless explicitly specified otherwise.

- ***Pass an argument (state) to the Job.***
The state is defined as an *Object* and may be used to pass arguments to the asynchronous operation triggered by the *JobScheduler*.

- ***Get the result (return value) of a Job upon completion***
Generic implementations of Job\<TResult\> are running a function with a return value of type *TResult*. The result of a Job becomes available once it's completed successfully.

During execution of a Job, there's a static property `Armat.Threading.Job.Current` to be used for identifying the instance of currently running Job. It's also possible to determine the Job for which the current one is a continuation (see the `Initiator` property of `Job` class). The property `Armat.Threading.Job.Root` returns the top level Job from which the asynchronous operation has begun.

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

The class `Armat.Threading.JobScheduler` derives from `Armat.Threading.JobSchedulerBase` and provides the default implementation of asynchronous jobs scheduling mechanism declared via `Armat.Threading.IJobScheduler` interface. It is recommended to use *IJobScheduler* interface for queueing the jobs. *IJobScheduler* interface defines the following properties and methods:

**Armat.Threading.IJobScheduler interface**

- `static IJobScheduler Default` property returns the default instance of *IJobScheduler*.
- `static IJobScheduler Current` property returns the instance of *IJobScheduler* running the *Job* on the current thread. This property will return `IJobScheduler Default` if the current thread is not running in a scope of a *Job*. See `JobSchedulerBase` for more information about how to change the `Current` job scheduler.
- `void Enqueue(Job job)` enqueues a *Job* in a scheduler. To successfully enqueue a *Job* in a *JobScheduler* one must have `Job.Status = JobStatus.Created` (never run before).
- `Boolean Cancel(Job job)` cancels *Job* execution in the *JobScheduler* before it begins. The method will fail (will return false) if the *Job* is already running or finished.
- `Int32 PendingJobsCount { get; }` property returns number of jobs currently waiting in the queue. It may be used to monitor the current load on the *JobScheduler*.

**Armat.Threading.JobSchedulerBase abstract class**

The class `Armat.Threading.JobSchedulerBase` has methods for changing the `IJobScheduler.Current` property by the following methods:

- `public JobSchedulerRuntimeScope EnterScope()` updates the `IJobScheduler.Current` property to point to the current job scheduler in context of the calling thread.
- `public void LeaveScope(in JobSchedulerRuntimeScope scope)` restores the `IJobScheduler.Current` property to the previous value (the one before entering the scope). Note that class `JobSchedulerRuntimeScope` implements IDisposable interface, and it automatically calls the *LeaveScope* method upon disposal.

**Armat.Threading.JobScheduler class**

By providing a `JobSchedulerConfiguration` instance upon construction of `Armat.Threading.JobScheduler` class, one may define a name for the *JobScheduler* (to be used for naming the threads), limit number of threads for asynchronous operations, as well as limit size of the *Job* queues within the scheduler.

On top of implementing *IJobScheduler* interface `Armat.Threading.JobScheduler` class also provides properties to retrieve statistics of Jobs queued within the scheduler. See `Statistics` and `MethodBuilderStatistics` properties for more information.

## JobRuntimeScope class

The class represents a scope of an asynchronous operation. It begins at the moment of instantiation of `JobRuntimeScope` object and ends upon its disposal (generally after completing the asynchronous operation). It represents a pair of *Key* and a *Value* that is available through a static accessor during an asynchronous code execution. All nested *Job* invocations derive the *JobRuntimeScope* of the initiator.

Using `JobRuntimeScope` one can deliver any number of parameters to the nested asynchronous methods, thus providing contextual information about the invocation. Some good examples of using `JobRuntimeScope` are:

- identifying correlation of Jobs
- tracing asynchronous code execution
- delivering contextual information to the nested methods

Following are members of `Armat.Threading.JobRuntimeScope` class:

- `public static JobRuntimeScope Enter(String key, Func<Object> factory)` instantiates an object of type *JobRuntimeScope* with the given *key* and uses the factory method to initialize the Value property. Note: In case if the *key* already exists in the scope, a `JobRuntimeScope.Null` will be returned.
- `public static JobRuntimeScope Enter<T>(String key, Func<T> factory)` is an overloaded generic version *JobRuntimeScope.Enter* method.
- `public static JobRuntimeScope Enter<T>(Func<T> factory)` is an overloaded generic version *JobRuntimeScope.Enter* method which uses T type as a key for creating the scope.
- `public static JobRuntimeScope EnterNew(String key, Func<Object> factory)` instantiates an object of type *JobRuntimeScope* with the given *key* and uses the factory method to initialize the Value property. Note: In case if the *key* already exists in the scope, a `JobRuntimeScope.Null` will be returned to indicate a failure result.
- `public static JobRuntimeScope EnterNew<T>(String key, Func<T> factory)` is an overloaded generic version *JobRuntimeScope.EnterNew* method.
- `public static JobRuntimeScope EnterNew<T>(Func<T> factory)` is an overloaded generic version *JobRuntimeScope.EnterNew* method which T type as a key for creating the scope.
- `public static Object? GetValue(String key)` returns value for the given key in the current scope. Will return *null* if a scope for the key is not found.
- `public static T? GetValue<T>(String key)` returns value for the given key in the current scope. Will return *null* if a scope for the key is not found or the *Value* is not assignable to *T*.
- `public static T? GetValue<T>()` returns value for the T type in the current scope. Will return *null* value if a scope for type *T* is not found or the *Value* is not assignable to *T*.
- `public void Leave()` leaves the current scope by removing the appropriate key.
- `public void Dispose()` leaves the current scope as described in *Leave()*. Useful for instantiating scoped to a method objects via *using* statement.
- `public Boolean IsNull` returns *true* for *NULL* (invalid) scope instances.
- `public String Key { get; }` property returns the *Key* of *JobRuntimeScope*.
- `public Object Value { get; }` property returns the *Value* of *JobRuntimeScope*.

Some examples of using JobRuntimeScope are available as unit tests [here](https://github.com/ar-mat/Threading/blob/master/Projects/ThreadingTest/RuntimeScope.cs). Below is another example of the same:
```cs
    private async Job DoSomething()
    {
        // run some user scoped operation
        await RunUserScopedOperation().ConfigureAwait(false);

        // user data is null here because the scope has been Disposed before exiting the above method
        UserData? userData = JobRuntimeScope.GetValue<UserData>();
    }
    private async Job RunUserScopedOperation()
    {
        // create the scope with some UserData information
        // 'using' keyword guarantees to have the scope Disposed when exiting the method
        using var scope = JobRuntimeScope.Enter<UserData>(() => new UserData("abc", "123"));

        // run any asynchronous operation
        // UserData will be accessible in all inner synchronous or asynchronous methods
        await AsyncOperationA().ConfigureAwait(false);

        // user data remains the same as above
        UserData? userData = JobRuntimeScope.GetValue<UserData>();
    }
    private async Job AsyncOperationA()
    {
        // running some asynchronous operations
        await Job.Yield();

        // user data remains the same as created in the caller method
        UserData? userData = JobRuntimeScope.GetValue<UserData>();
    }
```

The class `CorrelationIDScope` is one of the possible types to be used as a value for `JobRuntimeScope`. It generates auto-incrementing IDs to be used for correlation across asynchronous operations (for logging, tracing or any other needs). It provides convenient factory methods for instantiating `JobRuntimeScope` with a new `CorrelationIDScope` value as described below:
```cs
    private async Job RunCorrelationIDTest(Int32 testNum)
    {
        // create correlation ID and teh appropriate scope
        using var scope = CorrelationIDScope.Create();

        // any asynchronous code execution
        await Job.Yield();

        // correlation ID is available here
        Output.WriteLine("RunCorrelationIDTest: Correlation ID for test {0} is {1}",
            testNum,
            CorrelationIDScope.Current()!.CorrelationID);

        // nested method calls
        await NestedAsyncMethodCall(testNum, corrIDHolder.CorrelationID, 1).ConfigureAwait(false);
    }

    private async Job NestedAsyncMethodCall(Int32 testNum, Int64 expectedCorrID, Int32 depth)
    {
        // any asynchronous code execution
        await Job.Yield();

        // correlation ID remains the same as in the caller method above
        Output.WriteLine("NestedAsyncMethodCall<{0}>: Correlation ID for test {1} is {2}",
            depth,
            testNum,
            CorrelationIDScope.Current()!.CorrelationID);

        // go even deeper
        if (depth < 3)
            await NestedAsyncMethodCall(testNum, expectedCorrID, depth + 1).ConfigureAwait(false);
    }
```

# Summary

I hope the asynchronous code execution scheduler will inspire you to use it in own projects. I have one, and works quite well fine for me (I'm currently working to publish it for a wider audience).
I would appreciate any contributions in form of bug reports, improvement ideas or pull requests from the community.