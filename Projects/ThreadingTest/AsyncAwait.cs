using Armat.Utils.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;
using Xunit.Abstractions;

namespace Armat.Threading;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "<Pending>")]
public class JobSchedulerUnitTest_AsyncAwait
{
	#region Initialization

	public JobSchedulerUnitTest_AsyncAwait(ITestOutputHelper output)
	{
		Output = Executor.CreateOutputInterceptor(output);
	}

	private OutputInterceptor Output { get; set; }

	private void RunJSL_AsyncAwait(String callerName, WorkerType returnType, IEnumerable<WorkerType> listWorkerTypes)
	{
		RunJSL_AsyncAwait(callerName, returnType, listWorkerTypes.Select(workerType => new WorkerRunOptions(workerType)));
	}

	private void RunJSL_AsyncAwait(String callerName, WorkerType returnType, IEnumerable<WorkerRunOptions> listWorkerOptions)
	{
		Executor.TriggerAwaitRunner(callerName, returnType, listWorkerOptions, Output);
	}

	// Test case naming convention is RunJSL_<CategoryId>_Await<AwaiterClassTypes>_<AwaiterReturnType>

	#endregion // Initialization

	#region 004-010 - Compare TPL vs JSL on Single Worker Execution (non-generic workers case)

	// Runs a single asynchronous operation with all possible non-generic variations of 
	// return value types (void, Job, Task) and asynchronous executor types (Job, Task)
	// successful run implies that the results are matching in all described cases
	[Fact]
	public void RunJSL_010_Compare_TPL_vs_JSL_NonGeneric_SingleWorkerExecution()
	{
		Boolean succeededVoids, succeededRunnables;
		String[] result;
		String[] result_AwaitJob_Void, result_AwaitTask_Void;
		String[] result_AwaitJob_Job, result_AwaitTask_Job;
		String[] result_AwaitJob_Task, result_AwaitTask_Task;
		Int32 expectedDurationMS = 6_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		// AwaitJob_Void
		Output.Reset();
		RunJSL_011_AwaitJob_Void();
		result = Output.GetLines();
		result_AwaitJob_Void = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTask_Void
		Output.Reset();
		RunJSL_012_AwaitTask_Void();
		result = Output.GetLines();
		result_AwaitTask_Void = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJob_Job
		Output.Reset();
		RunJSL_013_AwaitJob_Job();
		result = Output.GetLines();
		result_AwaitJob_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTask_Job
		Output.Reset();
		RunJSL_014_AwaitTask_Job();
		result = Output.GetLines();
		result_AwaitTask_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJob_Task
		Output.Reset();
		RunJSL_015_AwaitJob_Task();
		result = Output.GetLines();
		result_AwaitJob_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTask_Task
		Output.Reset();
		RunJSL_016_AwaitTask_Task();
		result = Output.GetLines();
		result_AwaitTask_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeededVoids =
			result_AwaitJob_Void.ContentsEquals(result_AwaitJob_Void) &&
			result_AwaitJob_Void.ContentsEquals(result_AwaitTask_Void);
		succeededRunnables =
			result_AwaitJob_Job.ContentsEquals(result_AwaitJob_Job) &&
			result_AwaitJob_Job.ContentsEquals(result_AwaitJob_Task) &&
			result_AwaitJob_Job.ContentsEquals(result_AwaitTask_Job) &&
			result_AwaitJob_Job.ContentsEquals(result_AwaitTask_Task);

		Assert.True(succeededVoids, "Test with Void Result");
		Assert.True(succeededRunnables, "Test with Runnable Result");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_011_AwaitJob_Void()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Void, new WorkerType[] { WorkerType.Job });
	}

	//[Fact]
	private void RunJSL_012_AwaitTask_Void()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Void, new WorkerType[] { WorkerType.Task });
	}

	//[Fact]
	private void RunJSL_013_AwaitJob_Job()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Task, new WorkerType[] { WorkerType.Job });
	}

	//[Fact]
	private void RunJSL_014_AwaitTask_Job()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Job, new WorkerType[] { WorkerType.Task });
	}

	//[Fact]
	private void RunJSL_015_AwaitJob_Task()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Task, new WorkerType[] { WorkerType.Job });
	}

	//[Fact]
	private void RunJSL_016_AwaitTask_Task()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Task, new WorkerType[] { WorkerType.Task });
	}

	#endregion // 004-010 - Compare TPL vs JSL on Single Worker Execution

	#region 004-020 - Compare TPL vs JSL on Single Worker Execution (generic workers case)

	// Runs a single asynchronous operation with all possible generic variations of 
	// return value types (void, JobT, TaskT) and asynchronous executor types (JobT, TaskT)
	// successful run implies that the results are matching in all described cases
	[Fact]
	public void RunJSL_020_Compare_TPL_vs_JSL_Generic_SingleWorkerExecution()
	{
		Boolean succeededVoids, succeededRunnables;
		String[] result;
		String[] result_AwaitJobT_Void, result_AwaitTaskT_Void;
		String[] result_AwaitJobT_JobT, result_AwaitTaskT_JobT;
		String[] result_AwaitJobT_TaskT, result_AwaitTaskT_TaskT;
		Int32 expectedDurationMS = 6_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		// AwaitJobT_Void
		Output.Reset();
		RunJSL_021_AwaitJobT_Void();
		result = Output.GetLines();
		result_AwaitJobT_Void = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskT_Void
		Output.Reset();
		RunJSL_022_AwaitTaskT_Void();
		result = Output.GetLines();
		result_AwaitTaskT_Void = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJobT_JobT
		Output.Reset();
		RunJSL_023_AwaitJobT_JobT();
		result = Output.GetLines();
		result_AwaitJobT_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskT_JobT
		Output.Reset();
		RunJSL_024_AwaitTaskT_JobT();
		result = Output.GetLines();
		result_AwaitTaskT_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJobT_TaskT
		Output.Reset();
		RunJSL_025_AwaitJobT_TaskT();
		result = Output.GetLines();
		result_AwaitJobT_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskT_TaskT
		Output.Reset();
		RunJSL_026_AwaitTaskT_TaskT();
		result = Output.GetLines();
		result_AwaitTaskT_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeededVoids =
			result_AwaitJobT_Void.ContentsEquals(result_AwaitJobT_Void) &&
			result_AwaitJobT_Void.ContentsEquals(result_AwaitTaskT_Void);
		succeededRunnables =
			result_AwaitJobT_JobT.ContentsEquals(result_AwaitJobT_JobT) &&
			result_AwaitJobT_JobT.ContentsEquals(result_AwaitJobT_TaskT) &&
			result_AwaitJobT_JobT.ContentsEquals(result_AwaitTaskT_JobT) &&
			result_AwaitJobT_JobT.ContentsEquals(result_AwaitTaskT_TaskT);

		Assert.True(succeededVoids, "Test with Void Result");
		Assert.True(succeededRunnables, "Test with Runnable Result");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_021_AwaitJobT_Void()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Void, new WorkerType[] { WorkerType.JobT });
	}

	//[Fact]
	private void RunJSL_022_AwaitTaskT_Void()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Void, new WorkerType[] { WorkerType.TaskT });
	}

	//[Fact]
	private void RunJSL_023_AwaitJobT_JobT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.JobT, new WorkerType[] { WorkerType.JobT });
	}

	//[Fact]
	private void RunJSL_024_AwaitTaskT_JobT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.JobT, new WorkerType[] { WorkerType.TaskT });
	}

	//[Fact]
	private void RunJSL_025_AwaitJobT_TaskT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.TaskT, new WorkerType[] { WorkerType.JobT });
	}

	//[Fact]
	private void RunJSL_026_AwaitTaskT_TaskT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.TaskT, new WorkerType[] { WorkerType.TaskT });
	}

	#endregion // 004-020 - Compare TPL vs JSL on Single Worker Execution

	#region 004-030 - Compare TPL vs JSL on Double Worker Execution (non-generic workers case)

	// Runs a two sequential asynchronous operations with all possible non-generic variations of 
	// return value types (void, Job, Task) and asynchronous executor types (Job, Task)
	// successful run implies that the results are matching in all described cases
	[Fact]
	public void RunJSL_030_Compare_TPL_vs_JSL_NonGeneric_DoubleWorkerExecution()
	{
		Boolean succeeded;
		String[] result;
		String[] result_AwaitJobJob_Job, result_AwaitJobTask_Job, result_AwaitTaskJob_Job, result_AwaitTaskTask_Job;
		String[] result_AwaitJobJob_Task, result_AwaitJobTask_Task, result_AwaitTaskJob_Task, result_AwaitTaskTask_Task;
		Int32 expectedDurationMS = 16_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		// AwaitJobJob_Job
		Output.Reset();
		RunJSL_031_AwaitJobJob_Job();
		result = Output.GetLines();
		result_AwaitJobJob_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJobTask_Task
		Output.Reset();
		RunJSL_032_AwaitJobTask_Job();
		result = Output.GetLines();
		result_AwaitJobTask_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskJob_Job
		Output.Reset();
		RunJSL_033_AwaitTaskJob_Job();
		result = Output.GetLines();
		result_AwaitTaskJob_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskTask_Task
		Output.Reset();
		RunJSL_034_AwaitTaskTask_Job();
		result = Output.GetLines();
		result_AwaitTaskTask_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJobJob_Task
		Output.Reset();
		RunJSL_035_AwaitJobJob_Task();
		result = Output.GetLines();
		result_AwaitJobJob_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJobTask_Task
		Output.Reset();
		RunJSL_036_AwaitJobTask_Task();
		result = Output.GetLines();
		result_AwaitJobTask_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskJob_Task
		Output.Reset();
		RunJSL_037_AwaitTaskJob_Task();
		result = Output.GetLines();
		result_AwaitTaskJob_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskTask_Task
		Output.Reset();
		RunJSL_038_AwaitTaskTask_Task();
		result = Output.GetLines();
		result_AwaitTaskTask_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeeded =
			result_AwaitJobJob_Job.ContentsEquals(result_AwaitJobTask_Job) &&
			result_AwaitJobJob_Job.ContentsEquals(result_AwaitTaskJob_Job) &&
			result_AwaitJobJob_Job.ContentsEquals(result_AwaitTaskTask_Job) &&
			result_AwaitJobJob_Job.ContentsEquals(result_AwaitJobJob_Task) &&
			result_AwaitJobJob_Job.ContentsEquals(result_AwaitJobTask_Task) &&
			result_AwaitJobJob_Job.ContentsEquals(result_AwaitTaskJob_Task) &&
			result_AwaitJobJob_Job.ContentsEquals(result_AwaitTaskTask_Task);

		Assert.True(succeeded, "Test with Runnable Result");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_031_AwaitJobJob_Job()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Job, new WorkerType[] { WorkerType.Job, WorkerType.Job });
	}

	//[Fact]
	private void RunJSL_032_AwaitJobTask_Job()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Job, new WorkerType[] { WorkerType.Job, WorkerType.Task });
	}

	//[Fact]
	private void RunJSL_033_AwaitTaskJob_Job()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Job, new WorkerType[] { WorkerType.Task, WorkerType.Job });
	}

	//[Fact]
	private void RunJSL_034_AwaitTaskTask_Job()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Job, new WorkerType[] { WorkerType.Task, WorkerType.Task });
	}

	//[Fact]
	private void RunJSL_035_AwaitJobJob_Task()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Task, new WorkerType[] { WorkerType.Job, WorkerType.Job });
	}

	//[Fact]
	private void RunJSL_036_AwaitJobTask_Task()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Task, new WorkerType[] { WorkerType.Job, WorkerType.Task });
	}

	//[Fact]
	private void RunJSL_037_AwaitTaskJob_Task()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Task, new WorkerType[] { WorkerType.Task, WorkerType.Job });
	}

	//[Fact]
	private void RunJSL_038_AwaitTaskTask_Task()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Task, new WorkerType[] { WorkerType.Task, WorkerType.Task });
	}

	#endregion // 004-030 - Compare TPL vs JSL on Double Worker Execution (non-generic workers case)

	#region 004-040 - Compare TPL vs JSL on Double Worker Execution (generic workers case)

	// Runs a two sequential asynchronous operations with all possible generic variations of 
	// return value types (void, JobT, TaskT) and asynchronous executor types (JobT, TaskT)
	// successful run implies that the results are matching in all described cases
	[Fact]
	public void RunJSL_040_Compare_TPL_vs_JSL_Generic_DoubleWorkerExecution()
	{
		Boolean succeeded;
		String[] result;
		String[] result_AwaitJobTJobT_JobT, result_AwaitJobTTaskT_JobT, result_AwaitTaskTJobT_JobT, result_AwaitTaskTTaskT_JobT;
		String[] result_AwaitJobTJobT_TaskT, result_AwaitJobTTaskT_TaskT, result_AwaitTaskTJobT_TaskT, result_AwaitTaskTTaskT_TaskT;
		Int32 expectedDurationMS = 16_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		// AwaitJobT_JobT
		Output.Reset();
		RunJSL_041_AwaitJobTJobT_JobT();
		result = Output.GetLines();
		result_AwaitJobTJobT_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJobT_TaskT
		Output.Reset();
		RunJSL_042_AwaitJobTTaskT_JobT();
		result = Output.GetLines();
		result_AwaitJobTTaskT_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskT_JobT
		Output.Reset();
		RunJSL_043_AwaitTaskTJobT_JobT();
		result = Output.GetLines();
		result_AwaitTaskTJobT_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskT_TaskT
		Output.Reset();
		RunJSL_044_AwaitTaskTTaskT_JobT();
		result = Output.GetLines();
		result_AwaitTaskTTaskT_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJobT_TaskT
		Output.Reset();
		RunJSL_045_AwaitJobTJobT_TaskT();
		result = Output.GetLines();
		result_AwaitJobTJobT_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJobT_TaskT
		Output.Reset();
		RunJSL_046_AwaitJobTTaskT_TaskT();
		result = Output.GetLines();
		result_AwaitJobTTaskT_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskT_TaskT
		Output.Reset();
		RunJSL_047_AwaitTaskTJobT_TaskT();
		result = Output.GetLines();
		result_AwaitTaskTJobT_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskT_TaskT
		Output.Reset();
		RunJSL_048_AwaitTaskTTaskT_TaskT();
		result = Output.GetLines();
		result_AwaitTaskTTaskT_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeeded =
			result_AwaitJobTJobT_JobT.ContentsEquals(result_AwaitJobTTaskT_JobT) &&
			result_AwaitJobTJobT_JobT.ContentsEquals(result_AwaitTaskTJobT_JobT) &&
			result_AwaitJobTJobT_JobT.ContentsEquals(result_AwaitTaskTTaskT_JobT) &&
			result_AwaitJobTJobT_JobT.ContentsEquals(result_AwaitJobTJobT_TaskT) &&
			result_AwaitJobTJobT_JobT.ContentsEquals(result_AwaitJobTTaskT_TaskT) &&
			result_AwaitJobTJobT_JobT.ContentsEquals(result_AwaitTaskTJobT_TaskT) &&
			result_AwaitJobTJobT_JobT.ContentsEquals(result_AwaitTaskTTaskT_TaskT);

		Assert.True(succeeded, "Test with Runnable Result");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_041_AwaitJobTJobT_JobT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.JobT, new WorkerType[] { WorkerType.JobT, WorkerType.JobT });
	}

	//[Fact]
	private void RunJSL_042_AwaitJobTTaskT_JobT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.JobT, new WorkerType[] { WorkerType.JobT, WorkerType.TaskT });
	}

	//[Fact]
	private void RunJSL_043_AwaitTaskTJobT_JobT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.JobT, new WorkerType[] { WorkerType.TaskT, WorkerType.JobT });
	}

	//[Fact]
	private void RunJSL_044_AwaitTaskTTaskT_JobT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.JobT, new WorkerType[] { WorkerType.TaskT, WorkerType.TaskT });
	}

	//[Fact]
	private void RunJSL_045_AwaitJobTJobT_TaskT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.TaskT, new WorkerType[] { WorkerType.JobT, WorkerType.JobT });
	}

	//[Fact]
	private void RunJSL_046_AwaitJobTTaskT_TaskT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.TaskT, new WorkerType[] { WorkerType.JobT, WorkerType.TaskT });
	}

	//[Fact]
	private void RunJSL_047_AwaitTaskTJobT_TaskT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.TaskT, new WorkerType[] { WorkerType.TaskT, WorkerType.JobT });
	}

	//[Fact]
	private void RunJSL_048_AwaitTaskTTaskT_TaskT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.TaskT, new WorkerType[] { WorkerType.TaskT, WorkerType.TaskT });
	}

	#endregion // 004-040 - Compare TPL vs JSL on Double Worker Execution (generic workers case)

	#region 004-050 - Compare TPL vs JSL on Multi Worker Execution (void workers case)

	// Runs four sequential asynchronous operations returning void results
	// It calls [Job, JobT, Task, TaskT] and [Task, TaskT, Job, JobT] sequentially using await keyword
	// successful run implies that the results are matching in all described cases
	[Fact]
	public void RunJSL_050_Compare_TPL_vs_JSL_Void_MultiWorkerExecution()
	{
		Boolean succeeded;
		String[] result;
		String[] result_AwaitJob_Void, result_AwaitTask_Void;
		Int32 expectedDurationMS = 8_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		// AwaitJobJobTTaskTaskT_Void
		Output.Reset();
		RunJSL_051_AwaitJobJobTTaskTaskT_Void();
		result = Output.GetLines();
		result_AwaitJob_Void = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskTaskTJobJobT_Void
		Output.Reset();
		RunJSL_052_AwaitTaskTaskTJobJobT_Void();
		result = Output.GetLines();
		result_AwaitTask_Void = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeeded =
			result_AwaitJob_Void.ContentsEquals(result_AwaitJob_Void) &&
			result_AwaitJob_Void.ContentsEquals(result_AwaitTask_Void);

		Assert.True(succeeded, "Test with Void Result");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_051_AwaitJobJobTTaskTaskT_Void()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Void, new WorkerType[] { WorkerType.Job, WorkerType.JobT, WorkerType.Task, WorkerType.TaskT });
	}

	//[Fact]
	private void RunJSL_052_AwaitTaskTaskTJobJobT_Void()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Void, new WorkerType[] { WorkerType.Task, WorkerType.TaskT, WorkerType.Job, WorkerType.JobT });
	}

	#endregion // 004-050 - Compare TPL vs JSL on Multi Worker Execution (void workers case)

	#region 004-060 - Compare TPL vs JSL on Multi Worker Execution (non-generic workers case)

	// Runs four sequential asynchronous operations returning non-generic (Job and Task) results
	// It calls [Job, JobT, Task, TaskT] and [Task, TaskT, Job, JobT] sequentially using await keyword
	// successful run implies that the results are matching in all described cases
	[Fact]
	public void RunJSL_060_Compare_TPL_vs_JSL_NonGeneric_MultiWorkerExecution()
	{
		Boolean succeeded;
		String[] result;
		String[] result_AwaitJobJobTTaskTaskT_Job, result_AwaitJobJobTTaskTaskT_Task, result_AwaitTaskTaskTJobJobT_Job, result_AwaitTaskTaskTJobJobT_Task;
		Int32 expectedDurationMS = 16_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		// AwaitJobJobTTaskTaskT_Job
		Output.Reset();
		RunJSL_061_AwaitJobJobTTaskTaskT_Job();
		result = Output.GetLines();
		result_AwaitJobJobTTaskTaskT_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJobJobTTaskTaskT_Task
		Output.Reset();
		RunJSL_062_AwaitJobJobTTaskTaskT_Task();
		result = Output.GetLines();
		result_AwaitJobJobTTaskTaskT_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskTaskTJobJobT_Job
		Output.Reset();
		RunJSL_063_AwaitTaskTaskTJobJobT_Job();
		result = Output.GetLines();
		result_AwaitTaskTaskTJobJobT_Job = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskTaskTJobJobT_Task
		Output.Reset();
		RunJSL_064_AwaitTaskTaskTJobJobT_Task();
		result = Output.GetLines();
		result_AwaitTaskTaskTJobJobT_Task = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeeded =
			result_AwaitJobJobTTaskTaskT_Job.ContentsEquals(result_AwaitJobJobTTaskTaskT_Job) &&
			result_AwaitJobJobTTaskTaskT_Job.ContentsEquals(result_AwaitJobJobTTaskTaskT_Task) &&
			result_AwaitJobJobTTaskTaskT_Job.ContentsEquals(result_AwaitTaskTaskTJobJobT_Job) &&
			result_AwaitJobJobTTaskTaskT_Job.ContentsEquals(result_AwaitTaskTaskTJobJobT_Task);

		Assert.True(succeeded, "Test with Runnable Result");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_061_AwaitJobJobTTaskTaskT_Job()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Job, new WorkerType[] { WorkerType.Job, WorkerType.JobT, WorkerType.Task, WorkerType.TaskT });
	}

	//[Fact]
	private void RunJSL_062_AwaitJobJobTTaskTaskT_Task()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Task, new WorkerType[] { WorkerType.Job, WorkerType.JobT, WorkerType.Task, WorkerType.TaskT });
	}

	//[Fact]
	private void RunJSL_063_AwaitTaskTaskTJobJobT_Job()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Job, new WorkerType[] { WorkerType.Task, WorkerType.TaskT, WorkerType.Job, WorkerType.JobT });
	}

	//[Fact]
	private void RunJSL_064_AwaitTaskTaskTJobJobT_Task()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.Task, new WorkerType[] { WorkerType.Task, WorkerType.TaskT, WorkerType.Job, WorkerType.JobT });
	}

	#endregion // 004-060 - Compare TPL vs JSL on Multi Worker Execution (generic workers case)

	#region 004-070 - Compare TPL vs JSL on Multi Worker Execution (generic workers case)

	// Runs four sequential asynchronous operations returning generic (JobT and TaskT) results
	// It calls [Job, JobT, Task, TaskT] and [Task, TaskT, Job, JobT] sequentially using await keyword
	// successful run implies that the results are matching in all described cases
	[Fact]
	public void RunJSL_070_Compare_TPL_vs_JSL_Generic_MultiWorkerExecution()
	{
		Boolean succeeded;
		String[] result;
		String[] result_AwaitJobJobTTaskTaskT_JobT, result_AwaitJobJobTTaskTaskT_TaskT, result_AwaitTaskTaskTJobJobT_JobT, result_AwaitTaskTaskTJobJobT_TaskT;
		Int32 expectedDurationMS = 16_000, expectedDurationErrorPerc = 10;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		// AwaitJobJobTTaskTaskT_JobT
		Output.Reset();
		RunJSL_071_AwaitJobJobTTaskTaskT_JobT();
		result = Output.GetLines();
		result_AwaitJobJobTTaskTaskT_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitJobJobTTaskTaskT_TaskT
		Output.Reset();
		RunJSL_072_AwaitJobJobTTaskTaskT_TaskT();
		result = Output.GetLines();
		result_AwaitJobJobTTaskTaskT_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskTaskTJobJobT_JobT
		Output.Reset();
		RunJSL_073_AwaitTaskTaskTJobJobT_JobT();
		result = Output.GetLines();
		result_AwaitTaskTaskTJobJobT_JobT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		// AwaitTaskTaskTJobJobT_TaskT
		Output.Reset();
		RunJSL_074_AwaitTaskTaskTJobJobT_TaskT();
		result = Output.GetLines();
		result_AwaitTaskTaskTJobJobT_TaskT = Executor.RemoveLogOutputPrefix(result);
		Output.WriteLine(String.Empty);

		succeeded =
			result_AwaitJobJobTTaskTaskT_JobT.ContentsEquals(result_AwaitJobJobTTaskTaskT_JobT) &&
			result_AwaitJobJobTTaskTaskT_JobT.ContentsEquals(result_AwaitJobJobTTaskTaskT_TaskT) &&
			result_AwaitJobJobTTaskTaskT_JobT.ContentsEquals(result_AwaitTaskTaskTJobJobT_JobT) &&
			result_AwaitJobJobTTaskTaskT_JobT.ContentsEquals(result_AwaitTaskTaskTJobJobT_TaskT);

		Assert.True(succeeded, "Test with Runnable Result");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	//[Fact]
	private void RunJSL_071_AwaitJobJobTTaskTaskT_JobT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.JobT, new WorkerType[] { WorkerType.Job, WorkerType.JobT, WorkerType.Task, WorkerType.TaskT });
	}

	//[Fact]
	private void RunJSL_072_AwaitJobJobTTaskTaskT_TaskT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.TaskT, new WorkerType[] { WorkerType.Job, WorkerType.JobT, WorkerType.Task, WorkerType.TaskT });
	}

	//[Fact]
	private void RunJSL_073_AwaitTaskTaskTJobJobT_JobT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.JobT, new WorkerType[] { WorkerType.Task, WorkerType.TaskT, WorkerType.Job, WorkerType.JobT });
	}

	//[Fact]
	private void RunJSL_074_AwaitTaskTaskTJobJobT_TaskT()
	{
		String methodName = System.Reflection.MethodBase.GetCurrentMethod()!.Name;

		RunJSL_AsyncAwait(methodName, WorkerType.TaskT, new WorkerType[] { WorkerType.Task, WorkerType.TaskT, WorkerType.Job, WorkerType.JobT });
	}

	#endregion // 004-070 - Compare TPL vs JSL on Multi Worker Execution (non-generic workers case)
}
