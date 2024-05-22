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

During execution of a Job, there's a static property `Armat.Threading.Job.Current` to be used for identifying the instance of currently running Job. It's also possible to determine the Job for which the current one is a continuation (see the `Initiator` property of `Job` class). There's also another property `Armat.Threading.Job.Root` to find the upper level Job from which the asynchronous operation has begun.

I'm not going to describe the whole interface of `Armat.Threading.Job` class taking into account that it matches to the `System.Threading.Tasks.Task` in .Net CLR.

## Armat.Threading.JobScheduler class

The class `Armat.Threading.JobScheduler` provides the default implementation of asynchronous jobs scheduling mechanism declared via `Armat.Threading.IJobScheduler` interface. Beside the instantiation of Job Scheduler, it is recommended to use *IJobScheduler* interface for queueing the jobs. *IJobScheduler* interface defines the following properties and methods:

- `static IJobScheduler Default` property returns the default instance of *IJobScheduler*.
- `static IJobScheduler Current` property returns the instance of *IJobScheduler* running the *Job* on the current thread. This property will return `IJobScheduler Default` if the current thread is not running in a scope of a *Job*.
- `void Enqueue(Job job)` enqueues a *Job* in a scheduler. To successfully enqueue a *Job* in a *JobScheduler* one must have `Job.Status = JobStatus.Created` (never run before).
- `Boolean Cancel(Job job)` cancels *Job* execution in the *JobScheduler* before it begins. The method will fail (will return false) if the *Job* is already running or finished.
- `Int32 PendingJobsCount { get; }` property returns number of jobs currently waiting in the queue. It may be used to monitor the current load on the *JobScheduler*.

It is possible to provide a `JobSchedulerConfiguration` upon instantiation of `Armat.Threading.JobScheduler` class. This allows defining a name for the *JobScheduler* (to be used for naming the threads), limiting number of threads for asynchronous operations, as well as limiting size of the *Job* queues within the scheduler.