using System;
using System.Threading;

using Xunit;
using Xunit.Abstractions;

namespace Armat.Threading;

[TestCaseOrderer("Armat.Test.PriorityOrderer", "ArmatUtilsTest")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "<Pending>")]
public class JobSchedulerUnitTest_MultipleSchedulers
{
	#region Initialization

	public JobSchedulerUnitTest_MultipleSchedulers(ITestOutputHelper output)
	{
		Output = Executor.CreateOutputInterceptor(output);
	}

	private OutputInterceptor Output { get; set; }

	#endregion // Initialization

	#region 006-010 - Verify Jobs run in the same scheduler

	[Fact]
	public void RunJSL_010_VerifyJobsRunInScheduler()
	{
		Int32 expectedDurationMS = 6_000, expectedDurationErrorPerc = 40;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();

		JobScheduler scheduler = new("Scheduler A");
		ManualResetEvent mre = new(false);

		Job job = new(JobProcA, mre);

		Output.WriteLine("Starting JobProcA job");
		job.Run(scheduler);

		mre.WaitOne();
		Output.WriteLine("Completed JobProcA job");

		// wait until the task
		Executor.SleepAndReturnVoid(Executor.TaskCompletionDelayMS)();

		JobSchedulerStatistics stats = scheduler.Statistics;

		//JobSchedulerStatistics mbStats = scheduler.MethodBuilderStatistics;

		job.Dispose();
		mre.Dispose();
		scheduler.Dispose();

		Output.WriteLine("Job Scheduler Statistics:" + stats.ToString());
		Assert.True(stats.QueuedJobs == 0 && stats.SucceededJobs == 6, "Test with Job Executions count");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	private async void JobProcA(Object arg)
	{
		ManualResetEvent mre = (ManualResetEvent)arg;

		await JobProcA(true);

		mre.Set();
	}

	private async Job JobProcA(Boolean callAnother)
	{
		if (!callAnother)
		{
			Output.WriteLine("JobProcA - Before Wait 1");
			await Job.Run(Executor.SleepAndReturnVoid());
			Output.WriteLine("JobProcA - After Wait 1");

			Output.WriteLine("JobProcA - Before Wait 2");
			await Job.Run(Executor.SleepAndReturnVoid());
			Output.WriteLine("JobProcA - After Wait 2");
		}
		else
		{
			Output.WriteLine("JobProcA - Before Wait 1");
			await Job.Run(Executor.SleepAndReturnVoid());
			Output.WriteLine("JobProcA - After Wait 1");

			Output.WriteLine("JobProcA - Before Call Another");
			await JobProcA(false);
			Output.WriteLine("JobProcA - After Call Another");

			Output.WriteLine("JobProcA - Before Wait 2");
			await Job.Run(Executor.SleepAndReturnVoid());
			Output.WriteLine("JobProcA - After Wait 2");
		}
	}

	#endregion // 006-010 - Verify Jobs run in the same scheduler

	#region 006-020 - Verify Jobs switch between schedulers

	[Fact]
	public void RunJSL_020_VerifyJobsSwitchSchedulers()
	{
		Int32 expectedDurationMS = 14_000, expectedDurationErrorPerc = 40;
		System.Diagnostics.Stopwatch _stopWatch = System.Diagnostics.Stopwatch.StartNew();


		JobScheduler schedulerA = new("Scheduler A");
		JobScheduler schedulerB = new("Scheduler B");
		ManualResetEvent mre = new(false);

		Job job = new(JobProcB, new Tuple<JobScheduler, ManualResetEvent>(schedulerA, mre));

		Output.WriteLine("Starting JobProcB job");
		job.Run(schedulerB);

		mre.WaitOne();
		Output.WriteLine("Completed JobProcB job");

		// wait until the task
		Executor.SleepAndReturnVoid(Executor.TaskCompletionDelayMS)();

		JobSchedulerStatistics statsA = schedulerA.Statistics;
		JobSchedulerStatistics statsB = schedulerB.Statistics;

		//JobSchedulerStatistics mbStats = scheduler.MethodBuilderStatistics;

		job.Dispose();
		mre.Dispose();
		schedulerB.Dispose();
		schedulerA.Dispose();

		Output.WriteLine("Job Scheduler A Statistics:" + statsA.ToString());
		Output.WriteLine("Job Scheduler B Statistics:" + statsB.ToString());
		Assert.True(statsA.QueuedJobs == 0 && statsA.SucceededJobs == 4, "Test with Job Executions count");
		Assert.True(statsB.QueuedJobs == 0 && statsB.SucceededJobs == 10, "Test with Job Executions count");
		Assert.True(Math.Abs(_stopWatch.ElapsedMilliseconds - expectedDurationMS) < (expectedDurationMS * expectedDurationErrorPerc) / 100, "Test duration");
	}

	private async void JobProcB(Object arg)
	{
		Tuple<JobScheduler, ManualResetEvent> tuple = (Tuple<JobScheduler, ManualResetEvent>)arg;
		JobScheduler scheduler = tuple.Item1;
		ManualResetEvent mre = tuple.Item2;

		await JobProcB(scheduler, true);

		mre.Set();
	}
	private async Job JobProcB(JobScheduler scheduler, Boolean callAnother)
	{
		if (scheduler == null)
		{
			// will run in scheduler A
			Output.WriteLine("JobProcB - Before Wait 1 - to run in scheduler A");
			await Job.Run(Executor.SleepAndReturnVoid());
			Output.WriteLine("JobProcB - After Wait 1 - to run in scheduler A");

			// get the top level job - it should be run in Job Scheduler B
			Output.WriteLine("JobProcA - Before Call JobProcA - to run in scheduler B");
			ManualResetEvent mre = new(false);
			await Job.Run(JobProcA, mre, CancellationToken.None, JobCreationOptions.None, Job.Current.Root.Scheduler);
			mre.WaitOne();
			mre.Dispose();
			Output.WriteLine("JobProcA - After Call JobProcA - to run in scheduler A");

			Output.WriteLine("JobProcB - Before Wait 2 - to run in scheduler A");
			await Job.Run(Executor.SleepAndReturnVoid());
			Output.WriteLine("JobProcB - After Wait 2 - to run in scheduler A");
		}
		else if (!callAnother)
		{
			// switch to the scheduler A
			Output.WriteLine("JobProcB - Before Wait 1 - to run in scheduler A");
			await Job.Run(Executor.SleepAndReturnVoid(), CancellationToken.None, JobCreationOptions.None, scheduler);
			Output.WriteLine("JobProcB - After Wait 1 - to run in scheduler A");

			Output.WriteLine("JobProcA - Before Call JobProcB - to run in scheduler A");
			await JobProcB(null, false);
			Output.WriteLine("JobProcA - After Call JobProcB - to run in scheduler A");

			Output.WriteLine("JobProcB - Before Wait 2 - to run in scheduler A");
			await Job.Run(Executor.SleepAndReturnVoid(), CancellationToken.None, JobCreationOptions.None, scheduler);
			Output.WriteLine("JobProcB - After Wait 2 - to run in scheduler A");
		}
		else
		{
			// will run in scheduler B
			Output.WriteLine("JobProcB - Before Wait 1");
			await Job.Run(Executor.SleepAndReturnVoid());
			Output.WriteLine("JobProcB - After Wait 1");

			Output.WriteLine("JobProcA - Before Call JobProcB");
			await JobProcB(scheduler, false);
			Output.WriteLine("JobProcA - After Call JobProcB");

			Output.WriteLine("JobProcB - Before Wait 2");
			await Job.Run(Executor.SleepAndReturnVoid());
			Output.WriteLine("JobProcB - After Wait 2");
		}
	}

	#endregion // 006-010 - Verify Jobs switch between schedulers
}
