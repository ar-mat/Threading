using System;
using System.Collections.Generic;
using System.Threading;

using Xunit;
using Xunit.Abstractions;

namespace Armat.Threading;

[TestCaseOrderer("Armat.Test.PriorityOrderer", "ArmatUtilsTest")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "<Pending>")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
public class JobSchedulerUnitTest_Performance
{
	#region Initialization

	public JobSchedulerUnitTest_Performance(ITestOutputHelper output)
	{
		Output = Executor.CreateOutputInterceptor(output);
	}

	private OutputInterceptor Output { get; set; }

	#endregion // Initialization

	#region 008-010 & 008-020 - Compare Jobs vs Tasks overhead

	[Fact]
	public void RunJSL_010_Compare_TPL_vs_JSL_PerformaneSequential()
	{
		//RunJSL_011_VerifyJobsPerformanceSequential();
		//RunJSL_012_VerifyTasksPerformanceSequential();

		System.Diagnostics.Stopwatch _stopWatchJobsPerfromance = System.Diagnostics.Stopwatch.StartNew();
		Int64 resultJob = RunJSL_011_VerifyJobsPerformanceSequential();
		Int64 jobsDuration = _stopWatchJobsPerfromance.ElapsedMilliseconds;
		Output.WriteLine($"Job result = {resultJob}, duration = {jobsDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchTasksPerformance = System.Diagnostics.Stopwatch.StartNew();
		Int64 resultTask = RunJSL_012_VerifyTasksPerformanceSequential();
		Int64 tasksDuration = _stopWatchTasksPerformance.ElapsedMilliseconds;
		Output.WriteLine($"Task result = {resultTask}, duration = {tasksDuration} ms");

		Int32 expectedDurationErrorPerc = 200;
		Boolean succeededPerformance = Math.Abs(jobsDuration - tasksDuration) < (tasksDuration * expectedDurationErrorPerc) / 100;

		Assert.True(resultJob == resultTask, "Sanity test");
		Assert.True(succeededPerformance, "Performance test");
	}

	[Fact]
	public void RunJSL_020_Compare_TPL_vs_JSL_PerformaneParallel()
	{
		//RunJSL_021_VerifyJobsPerformanceParallel();
		//RunJSL_022_VerifyTasksPerformanceParallel();

		System.Diagnostics.Stopwatch _stopWatchJobsPerfromance = System.Diagnostics.Stopwatch.StartNew();
		Int64 resultJob = RunJSL_021_VerifyJobsPerformanceParallel();
		Int64 jobsDuration = _stopWatchJobsPerfromance.ElapsedMilliseconds;
		Output.WriteLine($"Job result = {resultJob}, duration = {jobsDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchTasksPerformance = System.Diagnostics.Stopwatch.StartNew();
		Int64 resultTask = RunJSL_022_VerifyTasksPerformanceParallel();
		Int64 tasksDuration = _stopWatchTasksPerformance.ElapsedMilliseconds;
		Output.WriteLine($"Task result = {resultTask}, duration = {tasksDuration} ms");

		// TODO: Jobs are bout 5 times slow then tasks in this case, need to optimize...
		Int32 expectedDurationErrorPerc = 1000;
		Boolean succeededPerformance = Math.Abs(jobsDuration - tasksDuration) < (tasksDuration * expectedDurationErrorPerc) / 100;

		Assert.True(resultJob == resultTask, "Sanity test");
		Assert.True(succeededPerformance, "Performance test");
	}

	//[Fact]
	private Int64 RunJSL_011_VerifyJobsPerformanceSequential()
	{
		using JobScheduler scheduler = new("Performance JS", 1, 4, 1);
		using var scope = scheduler.EnterScope();

		return SumJob(0, 10000, false).Result;
	}

	//[Fact]
	private Int64 RunJSL_012_VerifyTasksPerformanceSequential()
	{
#pragma warning disable IDE0018 // Inline variable declaration
		Int32 prevMaxWorkerThreads, prevMaxCompPortThreads;
#pragma warning restore IDE0018 // Inline variable declaration
		ThreadPool.GetMaxThreads(out prevMaxWorkerThreads, out prevMaxCompPortThreads);

		try
		{
			ThreadPool.SetMaxThreads(4, 1);

			return SumTask(0, 10000, false).Result;
		}
		finally
		{
			ThreadPool.SetMaxThreads(prevMaxWorkerThreads, prevMaxCompPortThreads);
		}
	}

	//[Fact]
	private Int64 RunJSL_021_VerifyJobsPerformanceParallel()
	{
		using JobScheduler scheduler = new("Performance JS", 1, 4, 1);
		using var scope = scheduler.EnterScope();

		return SumJob(0, 10000, true).Result;
	}

	//[Fact]
	private Int64 RunJSL_022_VerifyTasksPerformanceParallel()
	{
#pragma warning disable IDE0018 // Inline variable declaration
		Int32 prevMaxWorkerThreads, prevMaxCompPortThreads;
#pragma warning restore IDE0018 // Inline variable declaration
		ThreadPool.GetMaxThreads(out prevMaxWorkerThreads, out prevMaxCompPortThreads);

		try
		{
			ThreadPool.SetMaxThreads(4, 1);

			return SumTask(0, 10000, true).Result;
		}
		finally
		{
			ThreadPool.SetMaxThreads(prevMaxWorkerThreads, prevMaxCompPortThreads);
		}
	}

	private static Int64 SumTuple(Object args)
	{
		Tuple<Int64, Int64> tuple = args as Tuple<Int64, Int64>;

		return tuple.Item1 + tuple.Item2;
	}
	private static Int64 SumRunnable(Job prevRunnable, Object args)
	{
		Job<Int64> runnable = (Job<Int64>)prevRunnable;

		return runnable.Result + (Int32)args;
	}
	private static Int64 SumRunnable(System.Threading.Tasks.Task prevRunnable, Object args)
	{
		System.Threading.Tasks.Task<Int64> runnable = (System.Threading.Tasks.Task<Int64>)prevRunnable;

		return runnable.Result + (Int32)args;
	}

	private static async Job<Int64> SumJob(Int32 min, Int32 max, Boolean runParallelly)
	{
		Int64 sum = 0;

		if (runParallelly)
		{
			List<Job<Int64>> listJobs = new();

			for (Int32 index = min; index <= max; index += 4)
			{
				Job<Int64> firstRunnable = new(SumTuple, new Tuple<Int64, Int64>(index, index + 1));
				Job<Int64> nextRunnable = firstRunnable.ContinueWith(SumRunnable, (Object)(index + 2));
				Job<Int64> nextRunnable2 = nextRunnable.ContinueWith(SumRunnable, (Object)(index + 3));

				firstRunnable.Run();
				listJobs.Add(nextRunnable2);
			}

			Int64[] results = await Job.WhenAll(listJobs);
			foreach (Int64 value in results)
				sum += value;
		}
		else
		{
			for (Int32 index = min; index <= max; index += 4)
			{
				Job<Int64> firstRunnable = new(SumTuple, new Tuple<Int64, Int64>(index, index + 1));
				Job<Int64> nextRunnable = firstRunnable.ContinueWith(SumRunnable, (Object)(index + 2));
				Job<Int64> nextRunnable2 = nextRunnable.ContinueWith(SumRunnable, (Object)(index + 3));

				firstRunnable.Run();
				Int64 sumOf4Items = await nextRunnable2;
				sum += sumOf4Items;
			}
		}

		return sum;
	}
	private static async System.Threading.Tasks.Task<Int64> SumTask(Int32 min, Int32 max, Boolean runParallelly)
	{
		Int64 sum = 0;

		if (runParallelly)
		{
			List<System.Threading.Tasks.Task<Int64>> listTasks = new();

			for (Int32 index = min; index <= max; index += 4)
			{
				System.Threading.Tasks.Task<Int64> firstRunnable = new(SumTuple, new Tuple<Int64, Int64>(index, index + 1));
				System.Threading.Tasks.Task<Int64> nextRunnable = firstRunnable.ContinueWith(SumRunnable, (Object)(index + 2), System.Threading.Tasks.TaskScheduler.Current);
				System.Threading.Tasks.Task<Int64> nextRunnable2 = nextRunnable.ContinueWith(SumRunnable, (Object)(index + 3), System.Threading.Tasks.TaskScheduler.Current);

				firstRunnable.Start();
				listTasks.Add(nextRunnable2);
			}

			Int64[] results = await System.Threading.Tasks.Task.WhenAll(listTasks);
			foreach (Int64 value in results)
				sum += value;
		}
		else
		{
			for (Int32 index = min; index <= max; index += 4)
			{
				System.Threading.Tasks.Task<Int64> firstRunnable = new(SumTuple, new Tuple<Int64, Int64>(index, index + 1));
				System.Threading.Tasks.Task<Int64> nextRunnable = firstRunnable.ContinueWith(SumRunnable, (Object)(index + 2), System.Threading.Tasks.TaskScheduler.Current);
				System.Threading.Tasks.Task<Int64> nextRunnable2 = nextRunnable.ContinueWith(SumRunnable, (Object)(index + 3), System.Threading.Tasks.TaskScheduler.Current);

				firstRunnable.Start();
				Int64 sumOf4Items = await nextRunnable2;
				sum += sumOf4Items;
			}
		}

		return sum;
	}

	#endregion // 006-010 - Verify WaitAll and WaitAny

	#region 008-030 - Compare TPL vs JSL on Using await in loop

	[Fact]
	public void RunJSL_030_Compare_TPL_vs_JSL_AwaitInLoop()
	{
		//RunJSL_021_JobAwaitInLoop();
		//RunJSL_022_TaskAwaitInLoop();

		System.Diagnostics.Stopwatch _stopWatchJobsPerfromance = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_031_JobAwaitInLoop();
		Int64 jobsDuration = _stopWatchJobsPerfromance.ElapsedMilliseconds;
		Output.WriteLine($"Job duration = {jobsDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchTasksPerformance = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_032_TaskAwaitInLoop();
		Int64 tasksDuration = _stopWatchTasksPerformance.ElapsedMilliseconds;
		Output.WriteLine($"Task duration = {tasksDuration} ms");

		Int32 expectedDurationErrorPerc = 150;
		Boolean succeededPerformance = Math.Abs(jobsDuration - tasksDuration) < (tasksDuration * expectedDurationErrorPerc) / 100;

		Assert.True(succeededPerformance, "Test with loops");
	}

	//[Fact]
	private void RunJSL_031_JobAwaitInLoop()
	{
		//String methodName = System.Reflection.MethodBase.GetCurrentMethod().Name;

		JobAwaitInLoop().Wait();
	}

	private static async Job JobAwaitInLoop()
	{
		for (Int32 i = 0; i < 4000; i++)
			await Job.Delay(1);
	}

	//[Fact]
	private void RunJSL_032_TaskAwaitInLoop()
	{
		//String methodName = System.Reflection.MethodBase.GetCurrentMethod().Name;

		TaskAwaitInLoop().Wait();
	}

	private static async System.Threading.Tasks.Task TaskAwaitInLoop()
	{
		for (Int32 i = 0; i < 4000; i++)
			await System.Threading.Tasks.Task.Delay(1);
	}

	#endregion // 008-020 - Compare TPL vs JSL on Using await in loop

	#region 008-040 - Compare TPL vs JSL on Using await in function

	[Fact]
	public void RunJSL_040_Compare_TPL_vs_JSL_AwaitInFunc()
	{
		//RunJSL_021_JobAwaitInFunc();
		//RunJSL_022_TaskAwaitInFunc();

		System.Diagnostics.Stopwatch _stopWatchJobsPerfromance = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_041_JobAwaitInFunc();
		Int64 jobsDuration = _stopWatchJobsPerfromance.ElapsedMilliseconds;
		Output.WriteLine($"Job duration = {jobsDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchTasksPerformance = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_042_TaskAwaitInFunc();
		Int64 tasksDuration = _stopWatchTasksPerformance.ElapsedMilliseconds;
		Output.WriteLine($"Task duration = {tasksDuration} ms");

		Int32 expectedDurationErrorPerc = 150;
		Boolean succeededPerformance = Math.Abs(jobsDuration - tasksDuration) < (tasksDuration * expectedDurationErrorPerc) / 100;

		Assert.True(succeededPerformance, "Test with loops");
	}

	//[Fact]
	private void RunJSL_041_JobAwaitInFunc()
	{
		//String methodName = System.Reflection.MethodBase.GetCurrentMethod().Name;

		JobAwaitInFunc().Wait();
	}

	private static async Job JobAwaitInFunc()
	{
		for (Int32 i = 0; i < 4000; i++)
			await JobAwaitFunc();
	}

	private static async Job JobAwaitFunc()
	{
		Executor.SleepAndReturnVoid(1);
		await Job.Delay(1);
		Executor.SleepAndReturnVoid(1);
	}


	//[Fact]
	private void RunJSL_042_TaskAwaitInFunc()
	{
		//String methodName = System.Reflection.MethodBase.GetCurrentMethod().Name;

		TaskAwaitInFunc().Wait();
	}

	private static async System.Threading.Tasks.Task TaskAwaitInFunc()
	{
		for (Int32 i = 0; i < 4000; i++)
			await TaskAwaitFunc();
	}

	private static async Job TaskAwaitFunc()
	{
		Executor.SleepAndReturnVoid(1);
		await System.Threading.Tasks.Task.Delay(1);
		Executor.SleepAndReturnVoid(1);
	}

	#endregion // 008-030 - Compare TPL vs JSL on Using await in function
}
