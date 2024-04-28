using Armat.Utils.Extensions;

using System;
using System.Text;

using Xunit;
using Xunit.Abstractions;

namespace Armat.Threading;

[TestCaseOrderer("Armat.Test.PriorityOrderer", "ArmatUtilsTest")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "<Pending>")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "<Pending>")]
public class JobSchedulerUnitTest_ErrorHandling
{
	#region Initialization

	public JobSchedulerUnitTest_ErrorHandling(ITestOutputHelper output)
	{
		Output = Executor.CreateOutputInterceptor(output);
	}

	private OutputInterceptor Output { get; set; }

	#endregion // Initialization

	#region 009-010 - Compare Jobs vs Tasks exceptions - blocking wait

	// Compares Job and Task exception (error) reporting on Wait() call
	[Fact]
	public void RunJSL_010_Compare_TPL_vs_JSL_Exceptions_Wait()
	{
		//RunJSL_011_VerifyJobsExceptions_Wait();
		//RunJSL_011_VerifyTasksExceptions_Wait();

		System.Diagnostics.Stopwatch _stopWatchJobsPerformance = System.Diagnostics.Stopwatch.StartNew();
		Type?[] resultJob = RunJSL_011_VerifyJobsExceptions_Wait();
		Int64 jobsDuration = _stopWatchJobsPerformance.ElapsedMilliseconds;
		Output.WriteLine($"Job result = {FormatResults(resultJob)}, duration = {jobsDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchTasksPerformance = System.Diagnostics.Stopwatch.StartNew();
		Type?[] resultTask = RunJSL_012_VerifyTasksExceptions_Wait();
		Int64 tasksDuration = _stopWatchTasksPerformance.ElapsedMilliseconds;
		Output.WriteLine($"Task result = {FormatResults(resultTask)}, duration = {tasksDuration} ms");

		Int32 expectedDurationErrorPerc = 150;
		Boolean succeededPerformance = Math.Abs(jobsDuration - tasksDuration) < (tasksDuration * expectedDurationErrorPerc) / 100;

		Assert.True(resultJob.ContentsEquals(resultTask), "Acceptance test");
		Assert.True(succeededPerformance, "Test with performance");
	}

	//[Fact]
	private Type?[] RunJSL_011_VerifyJobsExceptions_Wait()
	{
		Type?[] result = new Type?[4];

		for (Int32 exceptionStage = 0; exceptionStage < 4; exceptionStage++)
		{
			try
			{
				FailingJob(exceptionStage).Wait();

				result[exceptionStage] = null;
				Output.WriteLine($"Job [{exceptionStage}] succeeded");
			}
			catch (Exception e)
			{
				result[exceptionStage] = e.GetType();
				Output.WriteLine($"Job [{exceptionStage}] failed with error {e.GetType().Name}");
			}
		}

		return result;
	}

	//[Fact]
	private Type?[] RunJSL_012_VerifyTasksExceptions_Wait()
	{
		Type?[] result = new Type?[4];

		for (Int32 exceptionStage = 0; exceptionStage < 4; exceptionStage++)
		{
			try
			{
				FailingTask(exceptionStage).Wait();

				result[exceptionStage] = null;
				Output.WriteLine($"Task [{exceptionStage}] succeeded");
			}
			catch (Exception e)
			{
				result[exceptionStage] = e.GetType();
				Output.WriteLine($"Task [{exceptionStage}] failed with error {e.GetType().Name}");
			}
		}

		return result;
	}

	#endregion // 009-010 - Compare Jobs vs Tasks exceptions - blocking wait

	#region 009-010 - Compare Jobs vs Tasks exceptions - await

	// Compares Job and Task exception (error) reporting on await call
	[Fact]
	public void RunJSL_020_Compare_TPL_vs_JSL_Exceptions_Await()
	{
		//RunJSL_011_VerifyJobsExceptions_Await();
		//RunJSL_011_VerifyTasksExceptions_Await();

		System.Diagnostics.Stopwatch _stopWatchJobsPerformance = System.Diagnostics.Stopwatch.StartNew();
		Type?[] resultJob = RunJSL_021_VerifyJobsExceptions_Await().Result;
		Int64 jobsDuration = _stopWatchJobsPerformance.ElapsedMilliseconds;
		Output.WriteLine($"Job result = {FormatResults(resultJob)}, duration = {jobsDuration} ms");

		System.Diagnostics.Stopwatch _stopWatchTasksPerformance = System.Diagnostics.Stopwatch.StartNew();
		Type?[] resultTask = RunJSL_022_VerifyTasksExceptions_Await().Result;
		Int64 tasksDuration = _stopWatchTasksPerformance.ElapsedMilliseconds;
		Output.WriteLine($"Task result = {FormatResults(resultTask)}, duration = {tasksDuration} ms");

		Int32 expectedDurationErrorPerc = 150;
		Boolean succeededPerformance = Math.Abs(jobsDuration - tasksDuration) < (tasksDuration * expectedDurationErrorPerc) / 100;

		Assert.True(resultJob.ContentsEquals(resultTask), "Acceptance test");
		Assert.True(succeededPerformance, "Test with performance");
	}

	//[Fact]
	private async System.Threading.Tasks.Task<Type?[]> RunJSL_021_VerifyJobsExceptions_Await()
	{
		Type?[] result = new Type?[4];

		for (Int32 exceptionStage = 0; exceptionStage < 4; exceptionStage++)
		{
			try
			{
#pragma warning disable CS0618 // Type or member is obsolete
				await FailingJob(exceptionStage);
#pragma warning restore CS0618 // Type or member is obsolete

				result[exceptionStage] = null;
				Output.WriteLine($"Job [{exceptionStage}] succeeded");
			}
			catch (Exception e)
			{
				result[exceptionStage] = e.GetType();
				Output.WriteLine($"Job [{exceptionStage}] failed with error {e.GetType().Name}");
			}
		}

		return result;
	}

	//[Fact]
	private async System.Threading.Tasks.Task<Type?[]> RunJSL_022_VerifyTasksExceptions_Await()
	{
		Type?[] result = new Type?[4];

		for (Int32 exceptionStage = 0; exceptionStage < 4; exceptionStage++)
		{
			try
			{
#pragma warning disable CS0618 // Type or member is obsolete
				await FailingTask(exceptionStage);
#pragma warning restore CS0618 // Type or member is obsolete

				result[exceptionStage] = null;
				Output.WriteLine($"Task [{exceptionStage}] succeeded");
			}
			catch (Exception e)
			{
				result[exceptionStage] = e.GetType();
				Output.WriteLine($"Task [{exceptionStage}] failed with error {e.GetType().Name}");
			}
		}

		return result;
	}

	#endregion // 009-010 - Compare Jobs vs Tasks exceptions - await

	#region Helpers

	private static async Job FailingJob(Int32 exceptionStage)
	{
		// stage 0
		if (exceptionStage == 0)
			throw new ApplicationException();

		// stage 1
#pragma warning disable CS0618 // Type or member is obsolete
		await Job.Run(Executor.SleepAndReturnVoid(1_000));
#pragma warning restore CS0618 // Type or member is obsolete
		if (exceptionStage == 1)
			throw new ApplicationException();

		// stage 2
#pragma warning disable CS0618 // Type or member is obsolete
		await Job.Run(Executor.SleepAndReturnVoid(1_000));
#pragma warning restore CS0618 // Type or member is obsolete
		if (exceptionStage == 2)
			throw new ApplicationException();

		// stage 3+
		throw new ApplicationException();
	}

	private static async System.Threading.Tasks.Task FailingTask(Int32 exceptionStage)
	{
		// stage 0
		if (exceptionStage == 0)
			throw new ApplicationException();

		// stage 1
#pragma warning disable CS0618 // Type or member is obsolete
		await System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(1_000));
#pragma warning restore CS0618 // Type or member is obsolete
		if (exceptionStage == 1)
			throw new ApplicationException();

		// stage 2
#pragma warning disable CS0618 // Type or member is obsolete
		await System.Threading.Tasks.Task.Run(Executor.SleepAndReturnVoid(1_000));
#pragma warning restore CS0618 // Type or member is obsolete
		if (exceptionStage == 2)
			throw new ApplicationException();

		// stage 3+
		throw new ApplicationException();
	}

	private static String FormatResults(System.Collections.IEnumerable enumerable)
	{
		StringBuilder result = new();

		foreach (Object? obj in enumerable)
		{
			if (result.Length > 0)
				result.Append(',');

			result.Append(obj == null ? "null" : obj.ToString());
		}

		return result.ToString();
	}

	#endregion // Helpers
}
