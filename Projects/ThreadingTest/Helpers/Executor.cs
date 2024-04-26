﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

using Xunit.Abstractions;

namespace Armat.Threading;

public enum WorkerType
{
	Void,
	Job,
	JobT,
	Task,
	TaskT
}

public struct WorkerRunOptions
{
	public WorkerRunOptions(WorkerType workerType)
	{
		WorkerType = workerType;
		ConfigureAwait = null;
	}
	public WorkerRunOptions(WorkerType workerType, Boolean? configureAwait)
	{
		WorkerType = workerType;
		ConfigureAwait = configureAwait;
	}

	public WorkerType WorkerType { get; set; }
	public Boolean? ConfigureAwait { get; set; }
}

public static class Executor
{
	public const Int32 TaskCompletionDelayMS = 0_500;
	public const Int32 DefaultSleepMS = 1_000;

	public static OutputInterceptor CreateOutputInterceptor(ITestOutputHelper output)
	{
		return new OutputInterceptor(output);
	}

	public static Action SleepAndReturnVoid()
	{
		return SleepAndReturnVoid(-1);
	}
	public static Action SleepAndReturnVoid(Int32 sleepMS)
	{
		return () =>
		{
			Int32 sleepTime = sleepMS >= 0 ? sleepMS : DefaultSleepMS;

			if (sleepTime >= 0)
				System.Threading.Thread.Sleep(sleepTime);
		};
	}

	public static Func<String> SleepAndReturnString(String returnValue)
	{
		return SleepAndReturnString(-1, returnValue);
	}
	public static Func<String> SleepAndReturnString(Int32 sleepMS, String returnValue)
	{
		return () =>
		{
			System.Threading.Thread.Sleep(sleepMS >= 0 ? sleepMS : DefaultSleepMS);
			return returnValue;
		};
	}

	public static String GetLogOutputPrefix(String methodName)
	{
		return $"{DateTime.Now:s} - {methodName} ({Environment.CurrentManagedThreadId})| ";
	}
	public static String GetLogOutputPrefix(String methodName, IEnumerable<WorkerRunOptions> listWorkerOptions)
	{
		if (listWorkerOptions != null)
		{
			foreach (WorkerRunOptions runOptions in listWorkerOptions)
			{
				methodName += "_" + runOptions.WorkerType.ToString();

				if (!runOptions.ConfigureAwait.HasValue)
					methodName += "_Default";
				else
					methodName += runOptions.ConfigureAwait.Value ? "_True" : "_False";
			}
		}

		return GetLogOutputPrefix(methodName);
	}
	public static String[] RemoveLogOutputPrefix(String[] arrLogLines)
	{
		if (arrLogLines.Length == 0)
			return arrLogLines;

		const String separatorPattern = ")| ";
		for (Int32 index = 0; index < arrLogLines.Length; index++)
		{
			String row = arrLogLines[index];
			Int32 separatorPos = row.IndexOf(separatorPattern, StringComparison.InvariantCulture);

			if (separatorPos != -1)
				arrLogLines[index] = row[(separatorPos + separatorPattern.Length)..];
		}

		return arrLogLines;
	}

	public static void TriggerAwaitRunner(String methodName, WorkerType resultType, IEnumerable<WorkerRunOptions> listWorkerOptions, ITestOutputHelper output)
	{
		using ManualResetEvent mre = new(false);

		String outputPrefix = GetLogOutputPrefix(System.Reflection.MethodBase.GetCurrentMethod()!.Name, listWorkerOptions);
		output.WriteLine(outputPrefix + "Thread Starting");

		// run the test in a separate thread to get rid of Task-s call stack
		// and infrastructure used in the XUnit framework
		Thread testThread = new(TriggerAwaitRunner_ThreadProc);
		testThread.Name = "TestThreadProc";
		testThread.Start(new Tuple<String, WorkerType, IEnumerable<WorkerRunOptions>, ITestOutputHelper, ManualResetEvent>(methodName, resultType, listWorkerOptions, output, mre));

		// wait until the thread starts
		SleepAndReturnVoid(TaskCompletionDelayMS)();

		output.WriteLine(outputPrefix + "Thread Waiting");

		mre.WaitOne();
		testThread.Join();

		output.WriteLine(outputPrefix + "Thread Stopped");
	}

	private static void TriggerAwaitRunner_ThreadProc(Object? arg)
	{
		if (arg == null)
			throw new ArgumentNullException(nameof(arg));

		var arguments = (Tuple<String, WorkerType, IEnumerable<WorkerRunOptions>, ITestOutputHelper, ManualResetEvent>)arg;
		String methodName = arguments.Item1;
		WorkerType resultType = arguments.Item2;
		IEnumerable<WorkerRunOptions> listWorkerOptions = arguments.Item3;
		ITestOutputHelper output = arguments.Item4;
		ManualResetEvent mre = arguments.Item5;

		TriggerAwaitRunner_ThreadProc(methodName, resultType, listWorkerOptions, output, mre);
	}

	public static async void TriggerAwaitRunner_ThreadProc(String methodName, WorkerType resultType, IEnumerable<WorkerRunOptions> listWorkerOptions, ITestOutputHelper output, ManualResetEvent? mre)
	{
		SynchronizationContext context = new();
		SynchronizationContext.SetSynchronizationContext(context);

		String jobResult = "None";
		String outputPrefix;

		switch (resultType)
		{
			case WorkerType.Void:

				VoidAwaiterRunner(methodName, listWorkerOptions, output, mre);

				jobResult = "Void";

				outputPrefix = GetLogOutputPrefix(methodName);
				output.WriteLine(outputPrefix + "Worker Result: ---->>>> " + jobResult);

				// there's no other way to wait for void returning async function
				mre?.WaitOne();

				break;
			case WorkerType.Job:

#pragma warning disable CS0618 // Type or member is obsolete
				await JobAwaiterRunner(methodName, listWorkerOptions, output, null);
#pragma warning restore CS0618 // Type or member is obsolete

				jobResult = "Runnable";

				outputPrefix = GetLogOutputPrefix(methodName);
				output.WriteLine(outputPrefix + "Worker Result: ---->>>> " + jobResult);

				mre?.Set();

				break;
			case WorkerType.JobT:

#pragma warning disable CS0618 // Type or member is obsolete
				jobResult = await JobTAwaiterRunner(methodName, listWorkerOptions, output, null);
#pragma warning restore CS0618 // Type or member is obsolete

				jobResult = String.Format(System.Globalization.CultureInfo.InvariantCulture, "Runnable<{0}>", jobResult);

				outputPrefix = GetLogOutputPrefix(methodName);
				output.WriteLine(outputPrefix + "Worker Result: ---->>>> " + jobResult);

				mre?.Set();

				break;
			case WorkerType.Task:

#pragma warning disable CS0618 // Type or member is obsolete
				await TaskAwaiterRunner(methodName, listWorkerOptions, output, null);
#pragma warning restore CS0618 // Type or member is obsolete

				jobResult = "Runnable";

				outputPrefix = GetLogOutputPrefix(methodName);
				output.WriteLine(outputPrefix + "Worker Result: ---->>>> " + jobResult);

				mre?.Set();

				break;
			case WorkerType.TaskT:

#pragma warning disable CS0618 // Type or member is obsolete
				jobResult = await TaskTAwaiterRunner(methodName, listWorkerOptions, output, null);
#pragma warning restore CS0618 // Type or member is obsolete

				jobResult = String.Format(System.Globalization.CultureInfo.InvariantCulture, "Runnable<{0}>", jobResult);

				outputPrefix = GetLogOutputPrefix(methodName);
				output.WriteLine(outputPrefix + "Worker Result: ---->>>> " + jobResult);

				mre?.Set();

				break;
			default:

				outputPrefix = GetLogOutputPrefix(methodName);
				output.WriteLine(outputPrefix + "Worker Result: ---->>>> " + jobResult);

				break;
		}
	}

	public static async void VoidAwaiterRunner(String methodName, IEnumerable<WorkerRunOptions> listWorkerOptions, ITestOutputHelper output, ManualResetEvent? mre)
	{
		String outputPrefix = GetLogOutputPrefix(methodName);
		String jobResult = "Initial Value";

		ExecutionContext? execContextPrev = null;
		SynchronizationContext? syncContextPrev = null;
		Int32 iterationIndex = 0;

		try
		{
			output.WriteLine(outputPrefix + "---- Test started ----");

			foreach (WorkerRunOptions runOptions in listWorkerOptions)
			{
				String stage = "Worker Await " + (++iterationIndex).ToString(System.Globalization.CultureInfo.InvariantCulture);
				String stagePrefix = outputPrefix + "Stage = " + stage + " ";

				output.WriteLine(outputPrefix + "Begin " + stage);

				if (runOptions.ConfigureAwait.HasValue)
				{
					execContextPrev = ExecutionContext.Capture();
					syncContextPrev = SynchronizationContext.Current;

					output.WriteLine(outputPrefix + (execContextPrev == null ? "ExecutionContext was NULL" : "ExecutionContext was NOT NULL"));
					output.WriteLine(outputPrefix + (syncContextPrev == null ? "SynchronizationContext was NULL" : "SynchronizationContext was NOT NULL"));
				}

				switch (runOptions.WorkerType)
				{
					case WorkerType.Void:
						{
							jobResult = "Error: Cannot await to void result";
						}
						break;
					case WorkerType.Job:
						{
							Job worker = Job.Run(SleepAndReturnVoid());
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
							jobResult = stage;
						}
						break;
					case WorkerType.JobT:
						{
							Job<String> worker = Job<String>.Run(SleepAndReturnString(stage));
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								jobResult = await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								jobResult = await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
						}
						break;
					case WorkerType.Task:
						{
							System.Threading.Tasks.Task worker = System.Threading.Tasks.Task.Run(SleepAndReturnVoid());
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
							jobResult = stage;
						}
						break;
					case WorkerType.TaskT:
						{
							System.Threading.Tasks.Task<String> worker = System.Threading.Tasks.Task<String>.Run(SleepAndReturnString(stage));
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								jobResult = await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								jobResult = await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
						}
						break;
				}

				if (runOptions.ConfigureAwait.HasValue)
				{
					ExecutionContext? execContextNew = ExecutionContext.Capture();
					SynchronizationContext? syncContextNew = SynchronizationContext.Current;

					output.WriteLine(stagePrefix + (execContextNew == null ? "ExecutionContext is NULL" : "ExecutionContext is NOT NULL"));
					output.WriteLine(stagePrefix + (syncContextNew == null ? "SynchronizationContext is NULL" : "SynchronizationContext is NOT NULL"));

					output.WriteLine(stagePrefix + (execContextPrev == execContextNew ? "ExecutionContexts DID NOT change" : "ExecutionContexts DID change"));
					output.WriteLine(stagePrefix + (syncContextPrev == syncContextNew ? "SynchronizationContext DID NOT change" : "SynchronizationContext DID change"));
				}

				output.WriteLine(outputPrefix + "End " + stage + " with result = " + jobResult);
			}

			output.WriteLine(outputPrefix + "---- Test completed ----");
		}
		catch (Exception exc)
		{
			//jobResult = "Failure Value";

			output.WriteLine(outputPrefix + "---- Test failed with error: " + exc.Message + " ----");
		}

		mre?.Set();
	}
	public static async Job JobAwaiterRunner(String methodName, IEnumerable<WorkerRunOptions> listWorkerOptions, ITestOutputHelper output, ManualResetEvent? mre)
	{
		String outputPrefix = GetLogOutputPrefix(methodName);
		String jobResult = "Initial Value";

		ExecutionContext? execContextPrev = null;
		SynchronizationContext? syncContextPrev = null;
		Int32 iterationIndex = 0;

		try
		{
			output.WriteLine(outputPrefix + "---- Test started ----");

			foreach (WorkerRunOptions runOptions in listWorkerOptions)
			{
				String stage = "Worker Await " + (++iterationIndex).ToString(System.Globalization.CultureInfo.InvariantCulture);
				String stagePrefix = outputPrefix + "Stage = " + stage + " ";

				output.WriteLine(outputPrefix + "Begin " + stage);

				if (runOptions.ConfigureAwait.HasValue)
				{
					execContextPrev = ExecutionContext.Capture();
					syncContextPrev = SynchronizationContext.Current;

					output.WriteLine(outputPrefix + (execContextPrev == null ? "ExecutionContext was NULL" : "ExecutionContext was NOT NULL"));
					output.WriteLine(outputPrefix + (syncContextPrev == null ? "SynchronizationContext was NULL" : "SynchronizationContext was NOT NULL"));
				}

				switch (runOptions.WorkerType)
				{
					case WorkerType.Void:
						{
							jobResult = "Error: Cannot await to void result";
						}
						break;
					case WorkerType.Job:
						{
							Job worker = Job.Run(SleepAndReturnVoid());
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
							jobResult = stage;
						}
						break;
					case WorkerType.JobT:
						{
							Job<String> worker = Job<String>.Run(SleepAndReturnString(stage));
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								jobResult = await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								jobResult = await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
						}
						break;
					case WorkerType.Task:
						{
							System.Threading.Tasks.Task worker = System.Threading.Tasks.Task.Run(SleepAndReturnVoid());
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
							jobResult = stage;
						}
						break;
					case WorkerType.TaskT:
						{
							System.Threading.Tasks.Task<String> worker = System.Threading.Tasks.Task<String>.Run(SleepAndReturnString(stage));
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								jobResult = await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								jobResult = await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
						}
						break;
				}

				if (runOptions.ConfigureAwait.HasValue)
				{
					ExecutionContext? execContextNew = ExecutionContext.Capture();
					SynchronizationContext? syncContextNew = SynchronizationContext.Current;

					output.WriteLine(stagePrefix + (execContextNew == null ? "ExecutionContext is NULL" : "ExecutionContext is NOT NULL"));
					output.WriteLine(stagePrefix + (syncContextNew == null ? "SynchronizationContext is NULL" : "SynchronizationContext is NOT NULL"));

					output.WriteLine(stagePrefix + (execContextPrev == execContextNew ? "ExecutionContexts DID NOT change" : "ExecutionContexts DID change"));
					output.WriteLine(stagePrefix + (syncContextPrev == syncContextNew ? "SynchronizationContext DID NOT change" : "SynchronizationContext DID change"));
				}

				output.WriteLine(outputPrefix + "End " + stage + " with result = " + jobResult);
			}

			output.WriteLine(outputPrefix + "---- Test completed ----");
		}
		catch (Exception exc)
		{
			//jobResult = "Failure Value";

			output.WriteLine(outputPrefix + "---- Test failed with error: " + exc.Message + " ----");
		}

		mre?.Set();
	}
	public static async Job<String> JobTAwaiterRunner(String methodName, IEnumerable<WorkerRunOptions> listWorkerOptions, ITestOutputHelper output, ManualResetEvent? mre)
	{
		String outputPrefix = GetLogOutputPrefix(methodName);
		String jobResult = "Initial Value";

		ExecutionContext? execContextPrev = null;
		SynchronizationContext? syncContextPrev = null;
		Int32 iterationIndex = 0;

		try
		{
			output.WriteLine(outputPrefix + "---- Test started ----");

			foreach (WorkerRunOptions runOptions in listWorkerOptions)
			{
				String stage = "Worker Await " + (++iterationIndex).ToString(System.Globalization.CultureInfo.InvariantCulture);
				String stagePrefix = outputPrefix + "Stage = " + stage + " ";

				output.WriteLine(outputPrefix + "Begin " + stage);

				if (runOptions.ConfigureAwait.HasValue)
				{
					execContextPrev = ExecutionContext.Capture();
					syncContextPrev = SynchronizationContext.Current;

					output.WriteLine(outputPrefix + (execContextPrev == null ? "ExecutionContext was NULL" : "ExecutionContext was NOT NULL"));
					output.WriteLine(outputPrefix + (syncContextPrev == null ? "SynchronizationContext was NULL" : "SynchronizationContext was NOT NULL"));
				}

				switch (runOptions.WorkerType)
				{
					case WorkerType.Void:
						{
							jobResult = "Error: Cannot await to void result";
						}
						break;
					case WorkerType.Job:
						{
							Job worker = Job.Run(SleepAndReturnVoid());
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
							jobResult = stage;
						}
						break;
					case WorkerType.JobT:
						{
							Job<String> worker = Job<String>.Run(SleepAndReturnString(stage));
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								jobResult = await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								jobResult = await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
						}
						break;
					case WorkerType.Task:
						{
							System.Threading.Tasks.Task worker = System.Threading.Tasks.Task.Run(SleepAndReturnVoid());
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
							jobResult = stage;
						}
						break;
					case WorkerType.TaskT:
						{
							System.Threading.Tasks.Task<String> worker = System.Threading.Tasks.Task<String>.Run(SleepAndReturnString(stage));
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								jobResult = await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								jobResult = await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
						}
						break;
				}

				if (runOptions.ConfigureAwait.HasValue)
				{
					ExecutionContext? execContextNew = ExecutionContext.Capture();
					SynchronizationContext? syncContextNew = SynchronizationContext.Current;

					output.WriteLine(stagePrefix + (execContextNew == null ? "ExecutionContext is NULL" : "ExecutionContext is NOT NULL"));
					output.WriteLine(stagePrefix + (syncContextNew == null ? "SynchronizationContext is NULL" : "SynchronizationContext is NOT NULL"));

					output.WriteLine(stagePrefix + (execContextPrev == execContextNew ? "ExecutionContexts DID NOT change" : "ExecutionContexts DID change"));
					output.WriteLine(stagePrefix + (syncContextPrev == syncContextNew ? "SynchronizationContext DID NOT change" : "SynchronizationContext DID change"));
				}

				output.WriteLine(outputPrefix + "End " + stage + " with result = " + jobResult);
			}

			output.WriteLine(outputPrefix + "---- Test completed ----");
		}
		catch (Exception exc)
		{
			jobResult = "Failure Value";

			output.WriteLine(outputPrefix + "---- Test failed with error: " + exc.Message + " ----");
		}

		mre?.Set();

		return jobResult;
	}
	public static async System.Threading.Tasks.Task TaskAwaiterRunner(String methodName, IEnumerable<WorkerRunOptions> listWorkerOptions, ITestOutputHelper output, ManualResetEvent? mre)
	{
		String outputPrefix = GetLogOutputPrefix(methodName);
		String jobResult = "Initial Value";

		ExecutionContext? execContextPrev = null;
		SynchronizationContext? syncContextPrev = null;
		Int32 iterationIndex = 0;

		try
		{
			output.WriteLine(outputPrefix + "---- Test started ----");

			foreach (WorkerRunOptions runOptions in listWorkerOptions)
			{
				String stage = "Worker Await " + (++iterationIndex).ToString(System.Globalization.CultureInfo.InvariantCulture);
				String stagePrefix = outputPrefix + "Stage = " + stage + " ";

				output.WriteLine(outputPrefix + "Begin " + stage);

				if (runOptions.ConfigureAwait.HasValue)
				{
					execContextPrev = ExecutionContext.Capture();
					syncContextPrev = SynchronizationContext.Current;

					output.WriteLine(outputPrefix + (execContextPrev == null ? "ExecutionContext was NULL" : "ExecutionContext was NOT NULL"));
					output.WriteLine(outputPrefix + (syncContextPrev == null ? "SynchronizationContext was NULL" : "SynchronizationContext was NOT NULL"));
				}

				switch (runOptions.WorkerType)
				{
					case WorkerType.Void:
						{
							jobResult = "Error: Cannot await to void result";
						}
						break;
					case WorkerType.Job:
						{
							Job worker = Job.Run(SleepAndReturnVoid());
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								await worker;
#pragma warning disable CS0618 // Type or member is obsolete
							}
							else
							{
								await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
							jobResult = stage;
						}
						break;
					case WorkerType.JobT:
						{
							Job<String> worker = Job<String>.Run(SleepAndReturnString(stage));
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								jobResult = await worker;
#pragma warning disable CS0618 // Type or member is obsolete
							}
							else
							{
								jobResult = await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
						}
						break;
					case WorkerType.Task:
						{
							System.Threading.Tasks.Task worker = System.Threading.Tasks.Task.Run(SleepAndReturnVoid());
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								await worker;
#pragma warning disable CS0618 // Type or member is obsolete
							}
							else
							{
								await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
							jobResult = stage;
						}
						break;
					case WorkerType.TaskT:
						{
							System.Threading.Tasks.Task<String> worker = System.Threading.Tasks.Task<String>.Run(SleepAndReturnString(stage));
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								jobResult = await worker;
#pragma warning disable CS0618 // Type or member is obsolete
							}
							else
							{
								jobResult = await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
						}
						break;
				}

				if (runOptions.ConfigureAwait.HasValue)
				{
					ExecutionContext? execContextNew = ExecutionContext.Capture();
					SynchronizationContext? syncContextNew = SynchronizationContext.Current;

					output.WriteLine(stagePrefix + (execContextNew == null ? "ExecutionContext is NULL" : "ExecutionContext is NOT NULL"));
					output.WriteLine(stagePrefix + (syncContextNew == null ? "SynchronizationContext is NULL" : "SynchronizationContext is NOT NULL"));

					output.WriteLine(stagePrefix + (execContextPrev == execContextNew ? "ExecutionContexts DID NOT change" : "ExecutionContexts DID change"));
					output.WriteLine(stagePrefix + (syncContextPrev == syncContextNew ? "SynchronizationContext DID NOT change" : "SynchronizationContext DID change"));
				}

				output.WriteLine(outputPrefix + "End " + stage + " with result = " + jobResult);
			}

			output.WriteLine(outputPrefix + "---- Test completed ----");
		}
		catch (Exception exc)
		{
			//jobResult = "Failure Value";

			output.WriteLine(outputPrefix + "---- Test failed with error: " + exc.Message + " ----");
		}

		mre?.Set();
	}
	public static async System.Threading.Tasks.Task<String> TaskTAwaiterRunner(String methodName, IEnumerable<WorkerRunOptions> listWorkerOptions, ITestOutputHelper output, ManualResetEvent? mre)
	{
		String outputPrefix = GetLogOutputPrefix(methodName);
		String jobResult = "Initial Value";

		ExecutionContext? execContextPrev = null;
		SynchronizationContext? syncContextPrev = null;
		Int32 iterationIndex = 0;

		try
		{
			output.WriteLine(outputPrefix + "---- Test started ----");

			foreach (WorkerRunOptions runOptions in listWorkerOptions)
			{
				String stage = "Worker Await " + (++iterationIndex).ToString(System.Globalization.CultureInfo.InvariantCulture);
				String stagePrefix = outputPrefix + "Stage = " + stage + " ";

				output.WriteLine(outputPrefix + "Begin " + stage);

				if (runOptions.ConfigureAwait.HasValue)
				{
					execContextPrev = ExecutionContext.Capture();
					syncContextPrev = SynchronizationContext.Current;

					output.WriteLine(outputPrefix + (execContextPrev == null ? "ExecutionContext was NULL" : "ExecutionContext was NOT NULL"));
					output.WriteLine(outputPrefix + (syncContextPrev == null ? "SynchronizationContext was NULL" : "SynchronizationContext was NOT NULL"));
				}

				switch (runOptions.WorkerType)
				{
					case WorkerType.Void:
						{
							jobResult = "Error: Cannot await to void result";
						}
						break;
					case WorkerType.Job:
						{
							Job worker = Job.Run(SleepAndReturnVoid());
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
							jobResult = stage;
						}
						break;
					case WorkerType.JobT:
						{
							Job<String> worker = Job<String>.Run(SleepAndReturnString(stage));
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								jobResult = await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								jobResult = await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
						}
						break;
					case WorkerType.Task:
						{
							System.Threading.Tasks.Task worker = System.Threading.Tasks.Task.Run(SleepAndReturnVoid());
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
							jobResult = stage;
						}
						break;
					case WorkerType.TaskT:
						{
							System.Threading.Tasks.Task<String> worker = System.Threading.Tasks.Task<String>.Run(SleepAndReturnString(stage));
							if (!runOptions.ConfigureAwait.HasValue)
							{
#pragma warning disable CS0618 // Type or member is obsolete
								jobResult = await worker;
#pragma warning restore CS0618 // Type or member is obsolete
							}
							else
							{
								jobResult = await worker.ConfigureAwait(runOptions.ConfigureAwait.Value);
							}
						}
						break;
				}

				if (runOptions.ConfigureAwait.HasValue)
				{
					ExecutionContext? execContextNew = ExecutionContext.Capture();
					SynchronizationContext? syncContextNew = SynchronizationContext.Current;

					output.WriteLine(stagePrefix + (execContextNew == null ? "ExecutionContext is NULL" : "ExecutionContext is NOT NULL"));
					output.WriteLine(stagePrefix + (syncContextNew == null ? "SynchronizationContext is NULL" : "SynchronizationContext is NOT NULL"));

					output.WriteLine(stagePrefix + (execContextPrev == execContextNew ? "ExecutionContexts DID NOT change" : "ExecutionContexts DID change"));
					output.WriteLine(stagePrefix + (syncContextPrev == syncContextNew ? "SynchronizationContext DID NOT change" : "SynchronizationContext DID change"));
				}

				output.WriteLine(outputPrefix + "End " + stage + " with result = " + jobResult);
			}

			output.WriteLine(outputPrefix + "---- Test completed ----");
		}
		catch (Exception exc)
		{
			jobResult = "Failure Value";

			output.WriteLine(outputPrefix + "---- Test failed with error: " + exc.Message + " ----");
		}

		mre?.Set();

		return jobResult;
	}
}
