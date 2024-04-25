using System;

using Xunit;
using Xunit.Abstractions;

namespace Armat.Threading;

[TestCaseOrderer("Armat.Test.PriorityOrderer", "ArmatUtilsTest")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "<Pending>")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
public class JobSchedulerUnitTest_WaitMany
{
	#region Initialization

	public JobSchedulerUnitTest_WaitMany(ITestOutputHelper output)
	{
		Output = Executor.CreateOutputInterceptor(output);
	}

	private OutputInterceptor Output { get; set; }

	#endregion // Initialization

	#region 007-010 - Verify WaitAll and WaitAny

	[Fact]
	public void RunJSL_010_Compare_TPL_vs_JSL_Wait()
	{
		System.Diagnostics.Stopwatch _stopWatchJobsWaitAll = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_011_VerifyJobsWaitAll();
		Int64 waitAllJobsDuration = _stopWatchJobsWaitAll.ElapsedMilliseconds;
		Output.WriteLine($"Job.WaitAll duration = {waitAllJobsDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchTasksWaitAll = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_012_VerifyTasksWaitAll();
		Int64 waitAllTasksDuration = _stopWatchTasksWaitAll.ElapsedMilliseconds;
		Output.WriteLine($"Task.WaitAll duration = {waitAllTasksDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchJobsWaitAny = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_013_VerifyJobsWaitAny();
		Int64 waitAnyJobsDuration = _stopWatchJobsWaitAny.ElapsedMilliseconds;
		Output.WriteLine($"Job.WaitAny duration = {waitAnyJobsDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchTasksWaitAny = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_014_VerifyTasksWaitAny();
		Int64 waitAnyTasksDuration = _stopWatchTasksWaitAny.ElapsedMilliseconds;
		Output.WriteLine($"Task.WaitAny duration = {waitAnyTasksDuration} ms");

		Int32 expectedDurationErrorPerc = 10;
		Boolean succeededWaitAll = Math.Abs(waitAllJobsDuration - waitAllTasksDuration) < (waitAllTasksDuration * expectedDurationErrorPerc) / 100;
		Boolean succeededWaitAny = Math.Abs(waitAnyJobsDuration - waitAnyTasksDuration) < (waitAnyTasksDuration * expectedDurationErrorPerc) / 100;

		Assert.True(succeededWaitAll, "Test with WaitAll");
		Assert.True(succeededWaitAny, "Test with WaitAny");
	}


	//[Fact]
	private void RunJSL_011_VerifyJobsWaitAll()
	{
		Int32 expectedDurationMS = 4_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		Job runnable1Sec = Job.Run(Executor.SleepAndReturnVoid(1_000));
		Job runnable2Sec = Job.Run(Executor.SleepAndReturnVoid(2_000));
		Job runnable3Sec = Job.Run(Executor.SleepAndReturnVoid(3_000));
		Job runnable4Sec = Job.Run(Executor.SleepAndReturnVoid(4_000));

		Job.WaitAll(runnable2Sec, runnable1Sec, runnable4Sec, runnable3Sec);
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_012_VerifyTasksWaitAll()
	{
		Int32 expectedDurationMS = 4_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		System.Threading.Tasks.Task runnable1Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(1_000));
		System.Threading.Tasks.Task runnable2Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(2_000));
		System.Threading.Tasks.Task runnable3Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(3_000));
		System.Threading.Tasks.Task runnable4Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(4_000));

		System.Threading.Tasks.Task.WaitAll(runnable2Sec, runnable1Sec, runnable4Sec, runnable3Sec);
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_013_VerifyJobsWaitAny()
	{
		Int32 expectedDurationMS = 1_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		Job runnable1Sec = Job.Run(Executor.SleepAndReturnVoid(1_000));
		Job runnable2Sec = Job.Run(Executor.SleepAndReturnVoid(2_000));
		Job runnable3Sec = Job.Run(Executor.SleepAndReturnVoid(3_000));
		Job runnable4Sec = Job.Run(Executor.SleepAndReturnVoid(4_000));

		Job.WaitAny(runnable2Sec, runnable1Sec, runnable4Sec, runnable3Sec);
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_014_VerifyTasksWaitAny()
	{
		Int32 expectedDurationMS = 1_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		System.Threading.Tasks.Task runnable1Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(1_000));
		System.Threading.Tasks.Task runnable2Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(2_000));
		System.Threading.Tasks.Task runnable3Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(3_000));
		System.Threading.Tasks.Task runnable4Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(4_000));

		System.Threading.Tasks.Task.WaitAny(runnable2Sec, runnable1Sec, runnable4Sec, runnable3Sec);
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	#endregion // 007-010 - Verify WaitAll and WaitAny

	#region 007-020 - Verify WhenAll and WhenAny

	[Fact]
	public void RunJSL_020_Compare_TPL_vs_JSL_When()
	{
		System.Diagnostics.Stopwatch _stopWatchJobsWhenAll = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_021_VerifyJobsWhenAll();
		Int64 whenAllJobsDuration = _stopWatchJobsWhenAll.ElapsedMilliseconds;
		Output.WriteLine($"Job.WhenAll duration = {whenAllJobsDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchTasksWhenAll = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_022_VerifyTasksWhenAll();
		Int64 whenAllTasksDuration = _stopWatchTasksWhenAll.ElapsedMilliseconds;
		Output.WriteLine($"Task.WhenAll duration = {whenAllTasksDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchJobsWhenAny = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_023_VerifyJobsWhenAny();
		Int64 whenAnyJobsDuration = _stopWatchJobsWhenAny.ElapsedMilliseconds;
		Output.WriteLine($"Job.WhenAny duration = {whenAnyJobsDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchTasksWhenAny = System.Diagnostics.Stopwatch.StartNew();
		RunJSL_024_VerifyTasksWhenAny();
		Int64 whenAnyTasksDuration = _stopWatchTasksWhenAny.ElapsedMilliseconds;
		Output.WriteLine($"Task.WhenAny duration = {whenAnyTasksDuration} ms");

		Int32 expectedDurationErrorPerc = 10;
		Boolean succeededWhenAll = Math.Abs(whenAllJobsDuration - whenAllTasksDuration) < (whenAllTasksDuration * expectedDurationErrorPerc) / 100;
		Boolean succeededWhenAny = Math.Abs(whenAnyJobsDuration - whenAnyTasksDuration) < (whenAnyTasksDuration * expectedDurationErrorPerc) / 100;

		Assert.True(succeededWhenAll, "Test with WhenAll");
		Assert.True(succeededWhenAny, "Test with WhenAny");
	}

	//[Fact]
	private void RunJSL_021_VerifyJobsWhenAll()
	{
		Int32 expectedDurationMS = 4_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		Job runnable1Sec = Job.Run(Executor.SleepAndReturnVoid(1_000));
		Job runnable2Sec = Job.Run(Executor.SleepAndReturnVoid(2_000));
		Job runnable3Sec = Job.Run(Executor.SleepAndReturnVoid(3_000));
		Job runnable4Sec = Job.Run(Executor.SleepAndReturnVoid(4_000));

		Job.WhenAll(runnable2Sec, runnable1Sec, runnable4Sec, runnable3Sec).Wait();
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_022_VerifyTasksWhenAll()
	{
		Int32 expectedDurationMS = 4_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		System.Threading.Tasks.Task runnable1Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(1_000));
		System.Threading.Tasks.Task runnable2Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(2_000));
		System.Threading.Tasks.Task runnable3Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(3_000));
		System.Threading.Tasks.Task runnable4Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(4_000));

		System.Threading.Tasks.Task.WhenAll(runnable2Sec, runnable1Sec, runnable4Sec, runnable3Sec).Wait();
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_023_VerifyJobsWhenAny()
	{
		Int32 expectedDurationMS = 1_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		Job runnable1Sec = Job.Run(Executor.SleepAndReturnVoid(1_000));
		Job runnable2Sec = Job.Run(Executor.SleepAndReturnVoid(2_000));
		Job runnable3Sec = Job.Run(Executor.SleepAndReturnVoid(3_000));
		Job runnable4Sec = Job.Run(Executor.SleepAndReturnVoid(4_000));

		Job.WhenAny(runnable2Sec, runnable1Sec, runnable4Sec, runnable3Sec).Wait();
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_024_VerifyTasksWhenAny()
	{
		Int32 expectedDurationMS = 1_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		System.Threading.Tasks.Task runnable1Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(1_000));
		System.Threading.Tasks.Task runnable2Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(2_000));
		System.Threading.Tasks.Task runnable3Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(3_000));
		System.Threading.Tasks.Task runnable4Sec = System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(4_000));

		System.Threading.Tasks.Task.WhenAny(runnable2Sec, runnable1Sec, runnable4Sec, runnable3Sec).Wait();
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	#endregion // 007-020 - Verify WhenAll and WhenAny
}
