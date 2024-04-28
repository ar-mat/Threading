using Armat.Utils.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Xunit;
using Xunit.Abstractions;

namespace Armat.Threading;

[TestCaseOrderer("Armat.Test.PriorityOrderer", "ArmatUtilsTest")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "<Pending>")]
public class JobSchedulerUnitTest_AwaiterConfig
{
	#region Initialization

	public JobSchedulerUnitTest_AwaiterConfig(ITestOutputHelper output)
	{
		Output = Executor.CreateOutputInterceptor(output);

		ThreadLocalDataSlotValue = null;
	}

	private OutputInterceptor Output { get; set; }

	private void RunJSL_AwaiterConfig(String callerName, IEnumerable<WorkerType> listWorkerTypes)
	{
		RunJSL_AwaiterConfig(callerName, listWorkerTypes.Select(workerType => new WorkerRunOptions(workerType)));
	}

	private void RunJSL_AwaiterConfig(String callerName, IEnumerable<WorkerRunOptions> listWorkerOptions)
	{
		Executor.TriggerAwaitRunner(callerName, WorkerType.Void, listWorkerOptions, Output);
	}

	#endregion // Initialization

	#region 005-01X - Compare TPL vs JSL awaiter with or without configurations

	// compares Job, JobT, Task, TaskT behavior in case if
	// awaiting with NO ConfigureAwait for an asynchronous invocation
	[Fact]
	public void RunJSL_011_Compare_TPL_vs_JSL_AwaiterConfig_Default()
	{
		Boolean succeeded;
		String[] result;
		String[] result_AwaiterConfig_Job, result_AwaiterConfig_JobT, result_AwaiterConfig_Task, result_AwaiterConfig_TaskT;
		Int32 expectedDurationMS = 4_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		// AwaiterConfig_Job
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerType[] { WorkerType.Job });
		result = Output.GetLines();
		result_AwaiterConfig_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaiterConfig_JobT
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerType[] { WorkerType.JobT });
		result = Output.GetLines();
		result_AwaiterConfig_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaiterConfig_Task
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerType[] { WorkerType.Task });
		result = Output.GetLines();
		result_AwaiterConfig_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaiterConfig_TaskT
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerType[] { WorkerType.TaskT });
		result = Output.GetLines();
		result_AwaiterConfig_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeeded =
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_Job) &&
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_JobT) &&
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_Task) &&
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_TaskT);

		Assert.True(succeeded, "Test with Awaiter Config = None");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	// compares Job, JobT, Task, TaskT behavior in case if
	// awaiting with ConfigureAwait(true) for an asynchronous invocation
	[Fact]
	public void RunJSL_012_Compare_TPL_vs_JSL_AwaiterConfig_True()
	{
		Boolean succeeded;
		String[] result;
		String[] result_AwaiterConfig_Job, result_AwaiterConfig_JobT, result_AwaiterConfig_Task, result_AwaiterConfig_TaskT;
		Int32 expectedDurationMS = 4_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;
		
		// AwaiterConfig_Job
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerRunOptions[] { new WorkerRunOptions(WorkerType.Job, true) });
		result = Output.GetLines();
		result_AwaiterConfig_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaiterConfig_JobT
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerRunOptions[] { new WorkerRunOptions(WorkerType.JobT, true) });
		result = Output.GetLines();
		result_AwaiterConfig_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaiterConfig_Task
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerRunOptions[] { new WorkerRunOptions(WorkerType.Task, true) });
		result = Output.GetLines();
		result_AwaiterConfig_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaiterConfig_TaskT
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerRunOptions[] { new WorkerRunOptions(WorkerType.TaskT, true) });
		result = Output.GetLines();
		result_AwaiterConfig_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeeded =
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_Job) &&
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_JobT) &&
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_Task) &&
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_TaskT);

		Assert.True(succeeded, "Test with Awaiter Config = True");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	// compares Job, JobT, Task, TaskT behavior in case if
	// awaiting with ConfigureAwait(false) for an asynchronous invocation
	[Fact]
	public void RunJSL_013_Compare_TPL_vs_JSL_AwaiterConfig_False()
	{
		Boolean succeeded;
		String[] result;
		String[] result_AwaiterConfig_Job, result_AwaiterConfig_JobT, result_AwaiterConfig_Task, result_AwaiterConfig_TaskT;
		Int32 expectedDurationMS = 4_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;
		
		// AwaiterConfig_Job
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerRunOptions[] { new WorkerRunOptions(WorkerType.Job, false) });
		result = Output.GetLines();
		result_AwaiterConfig_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaiterConfig_JobT
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerRunOptions[] { new WorkerRunOptions(WorkerType.JobT, false) });
		result = Output.GetLines();
		result_AwaiterConfig_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaiterConfig_Task
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerRunOptions[] { new WorkerRunOptions(WorkerType.Task, false) });
		result = Output.GetLines();
		result_AwaiterConfig_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaiterConfig_TaskT
		Output.Reset();
		RunJSL_AwaiterConfig(methodName, new WorkerRunOptions[] { new WorkerRunOptions(WorkerType.TaskT, false) });
		result = Output.GetLines();
		result_AwaiterConfig_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeeded =
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_Job) &&
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_JobT) &&
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_Task) &&
			result_AwaiterConfig_Job.ContentsEquals(result_AwaiterConfig_TaskT);

		Assert.True(succeeded, "Test with Awaiter Config = False");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	#endregion // 005-00X - Compare TPL vs JSL awaiter with or without configurations

	#region 005-02X - Thread Locals

	[ThreadStatic]
	private static String? ThreadStaticString;
	private LocalDataStoreSlot? ThreadLocalDataSlotValue { get; set; }

	[Fact]
	public void RunJSL_020_Compare_TPL_vs_JSL_ThreadLocals()
	{
		Boolean succeeded;
		String[] result;
		String[] result_VerifyThreadLocals_Job, result_VerifyThreadLocals_Task;
		Int32 expectedDurationMS = 1_000, expectedDurationErrorPerc = 100;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		// VerifyThreadLocals_Job
		Output.Reset();
		RunJSL_021_VerifyThreadLocals_Job();
		result = Output.GetLines();
		result_VerifyThreadLocals_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// VerifyThreadLocals_Task
		Output.Reset();
		RunJSL_022_VerifyThreadLocals_Task();
		result = Output.GetLines();
		result_VerifyThreadLocals_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeeded = result_VerifyThreadLocals_Job.ContentsEquals(result_VerifyThreadLocals_Task);

		Assert.True(succeeded, "Test with Thread Locals");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_021_VerifyThreadLocals_Job()
	{
		using System.Threading.ManualResetEvent mre = new(false);
		String outputPrefix = Executor.GetLogOutputPrefix(System.Reflection.MethodBase.GetCurrentMethod()!.Name);
		if (Output != null)
			Output.WriteLine(outputPrefix + "Thread Starting");

		// run the test in a separate thread to get rid of Task-s
		// call stack and infrastructure used in the XUnit framework
		Thread testThread = new(VerifyThreadLocals_Job_ThreadProc)
		{
			Name = "VerifyThreadLocals"
		};
		testThread.Start(mre);

		// wait until the thread starts
		Executor.SleepAndReturnVoid(Executor.TaskCompletionDelayMS)();

		if (Output != null)
			Output.WriteLine(outputPrefix + "Thread Waiting");
		mre.WaitOne();
		testThread.Join();

		if (Output != null)
			Output.WriteLine(outputPrefix + "Thread Stopped");
	}

	private async void VerifyThreadLocals_Job_ThreadProc(Object? arg)
	{
		ManualResetEvent mre = (ManualResetEvent)arg!;
		String outputPrefix = Executor.GetLogOutputPrefix(System.Reflection.MethodBase.GetCurrentMethod()!.Name);

		// Set thread locals
		ThreadLocalDataSlotValue = System.Threading.Thread.GetNamedDataSlot("TLS Value");
		if (ThreadLocalDataSlotValue == null)
			ThreadLocalDataSlotValue = System.Threading.Thread.AllocateNamedDataSlot("TLS Value");

		ThreadStaticString = "Well Initialized";
		System.Threading.Thread.SetData(ThreadLocalDataSlotValue, "Well Initialized");

		// call within the same thread context
		VerifyThreadLocals_DumpToOutput(outputPrefix + "Before Run: ");

		// run the job (in a parallel thread)
		await Job.Run(VerifyThreadLocals_DumpToOutput, "During Run No Configuration: ").ConfigureAwait(true);

		// call within the same thread context
		VerifyThreadLocals_DumpToOutput("After Run No Configuration: ");

		// run the job (in a parallel thread)
#pragma warning disable CS0618 // Type or member is obsolete
		await Job.Run(VerifyThreadLocals_DumpToOutput, "During Run Null Thread Context: ");
#pragma warning restore CS0618 // Type or member is obsolete

		// call within the same thread context
		VerifyThreadLocals_DumpToOutput("After Run Null Thread Context: ");

		mre.Set();
	}

	//[Fact]
	private void RunJSL_022_VerifyThreadLocals_Task()
	{
		using ManualResetEvent mre = new(false);
		String outputPrefix = Executor.GetLogOutputPrefix(System.Reflection.MethodBase.GetCurrentMethod()!.Name);
		if (Output != null)
			Output.WriteLine(outputPrefix + "Thread Starting");

		// run the test in a separate thread to get rid of Task-s
		// call stack and infrastructure used in the XUnit framework
		Thread testThread = new(VerifyThreadLocals_Task_ThreadProc)
		{
			Name = "VerifyThreadLocals"
		};
		testThread.Start(mre);

		// wait until the thread starts
		Executor.SleepAndReturnVoid(Executor.TaskCompletionDelayMS)();

		if (Output != null)
			Output.WriteLine(outputPrefix + "Thread Waiting");
		mre.WaitOne();
		testThread.Join();

		if (Output != null)
			Output.WriteLine(outputPrefix + "Thread Stopped");
	}

	private async void VerifyThreadLocals_Task_ThreadProc(Object? arg)
	{
		ManualResetEvent mre = (ManualResetEvent)arg!;
		String outputPrefix = Executor.GetLogOutputPrefix(System.Reflection.MethodBase.GetCurrentMethod()!.Name);

		// Set thread locals
		ThreadLocalDataSlotValue = System.Threading.Thread.GetNamedDataSlot("TLS Value");
		if (ThreadLocalDataSlotValue == null)
			ThreadLocalDataSlotValue = System.Threading.Thread.AllocateNamedDataSlot("TLS Value");

		ThreadStaticString = "Well Initialized";
		System.Threading.Thread.SetData(ThreadLocalDataSlotValue, "Well Initialized");

		// call within the same thread context
		VerifyThreadLocals_DumpToOutput(outputPrefix + "Before Run: ");

		// run the job (in a parallel thread)
		System.Threading.Tasks.Task task1 = new(VerifyThreadLocals_DumpToOutput, "During Run No Configuration: ");
		task1.Start();
		await task1.ConfigureAwait(true);

		// call within the same thread context
		VerifyThreadLocals_DumpToOutput("After Run No Configuration: ");

		// run the job (in a parallel thread)
		System.Threading.Tasks.Task task2 = new(VerifyThreadLocals_DumpToOutput, "During Run Null Thread Context: ");
		task2.Start();
		await task2;

		// call within the same thread context
		VerifyThreadLocals_DumpToOutput("After Run Null Thread Context: ");

		mre.Set();
	}


	private void VerifyThreadLocals_DumpToOutput(Object? objPrefixString)
	{
		String outputPrefix = (String)objPrefixString!;

		// test thread locals
		String? tlsValue = System.Threading.Thread.GetData(ThreadLocalDataSlotValue!) as String;
		String? staticValue = ThreadStaticString;
		if (String.IsNullOrEmpty(tlsValue))
			tlsValue = "[None]";
		if (String.IsNullOrEmpty(staticValue))
			staticValue = "[None]";

		Output.WriteLine(outputPrefix + "Thread Local Slot Value = " + tlsValue);
		Output.WriteLine(outputPrefix + "Thread Static Value = " + staticValue);
		Output.WriteLine("");
	}

	#endregion // 005-02X - Thread Locals
}
