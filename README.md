# Armat Threading Library

`Armat.Threading` library is an alternative implementation of *.Net Task Parallel Library (TPL)*. `Armat.Threading.Job` & `Armat.Threading.Job<T>` are the main classes to execute asynchronous operations - similar to the `Task` and `Task<T>` in the `System.Threading.Tasks` namespace. Following are the main benefits of using `Armat.Threading` vs standard TPL:

- Ability to instantiate multiple asynchronous Job execution thread pools (instances of `Armat.Threading.JobScheduler`s) within a single .Net process.
- There's a support of *Job* runtime scope. Job runtime scopes can be used to carry user-defined information during an asynchronous Job execution and are spawned deep to the nested Jobs.
- `Job` and `Job<T>` have consistent APIs with TPL `Task` and `Task<T>` classes, and should be easily used by developers familiar with TPL.
- The same *async-await* notation for *Tasks* is applicable to `Job` and `Job<T>` classes.
- Simpler and more extensible codebase from the once available in *.Net TPL*. May be useful to better understand asynchronous programming mechanisms in *.Net CLR* and extend it to meet your own application demands.

The library has been developed to allow creation of multiple independent thread pools in a single *.Net* application, and has been further enhanced to cover more real-life scenarios. It has been designed with performance and flexibility in mind and should produce comparable results with *.Net TPL* (see appropriate performance test results). Below are details about the major classes and some usage examples of `Armat.Threading` library.

## Armat.Threading.Job and Armat.Threading.Job\<T\> classes

`Armat.Threading.Job` and `Armat.Threading.Job<T>` correspond to the `System.Threading.Tasks.Task` and `System.Threading.Tasks.Task<T>` classes in *System.Threading.Tasks* namespace. Those have similar interfaces and can be used to trigger an asynchronous operation in .Net process.
by default tt uses the current Job Scheduler (see *Armat.Threading.IJobScheduler.Current* for more details) for executing the Jobs. Below are some examples of scheduling asynchronous Jobs:

```
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
            Console.WriteLine("Running job asynchronously with arg {0}", number");
            return new T(0.1248));
        }, 0.1248).ConfigureAwait(false);
```

There are several overloads of instantiating and running the Jobs. It is possible to
- ***Run a Job with a given cancellation token.***
This makes possible to request job cancellation from the other thread. job execution actions should properly handle the Cancellation Request and stop the asynchronous execution.

- ***Create a Job with custom jobCreationOptions.***
There are several flags in JobCreationOptions enumeration which can be used in conjunction:

    - ***JobCreationOptions.LongRunning*** marks the Job as a long running one, thus requesting a dedicated thread from the scheduler for its execution. The default implementation of Job Scheduler has a limited number of threads dedicated to long running jobs, and may block the calling thread if all are busy.