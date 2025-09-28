using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xunit;
using Xunit.Abstractions;

namespace Armat.Threading;
public class JobSchedulerUnitTest_RuntimeScope
{
	#region Initialization

	public JobSchedulerUnitTest_RuntimeScope(ITestOutputHelper output)
	{
		Output = Executor.CreateOutputInterceptor(output);
	}

	private OutputInterceptor Output { get; set; }

	#endregion // Initialization

	#region UserData test

	private record class UserData(String UserName, String Password)
	{
		public static UserData Default = new ("UserName", "Password");
	}

	[Fact]
	public void UserScopedAsyncOperation()
	{
		Output.WriteLine("Begin test");

		Job job = RunUserScopedOperation();
		job.Wait();

		Output.WriteLine("End test");
	}

	private async Job RunUserScopedOperation()
	{
		using var scope = JobRuntimeScope.Enter<UserData>(() => new UserData(UserData.Default.UserName, UserData.Default.Password));

		System.Threading.Thread.Sleep(100);
		Output.WriteLine("Scope Created");
		Assert.True(JobRuntimeScope.GetValue<UserData>()?.Password == UserData.Default.Password);

		await Job.Yield();
		System.Threading.Thread.Sleep(100);
		Output.WriteLine("After Yield");
		Assert.True(JobRuntimeScope.GetValue<UserData>()?.Password == UserData.Default.Password);

		await Job.Run(OperationA).ConfigureAwait(false);
		System.Threading.Thread.Sleep(100);
		Output.WriteLine("After OperationA");
		Assert.True(JobRuntimeScope.GetValue<UserData>()?.Password == UserData.Default.Password);

		await AsyncOperationB().ConfigureAwait(false);
		System.Threading.Thread.Sleep(100);
		Output.WriteLine("After AsyncOperationB");
		Assert.True(JobRuntimeScope.GetValue<UserData>()?.Password == UserData.Default.Password);
	}

	private void OperationA()
	{
		Output.WriteLine("Running OperationA");
		Assert.True(JobRuntimeScope.GetValue<UserData>()?.Password == UserData.Default.Password);
	}

	private async Job AsyncOperationB()
	{
		Output.WriteLine("Running AsyncOperationB");
		await Job.Yield();
		Assert.True(JobRuntimeScope.GetValue<UserData>()?.Password == UserData.Default.Password);

		await Job.Run(OperationA).ConfigureAwait(false);
		System.Threading.Thread.Sleep(100);
		Assert.True(JobRuntimeScope.GetValue<UserData>()?.Password == UserData.Default.Password);
	}

	#endregion // UserData test

	#region CorrelationID test

	[Fact]
	public void CorrelationIDPerAsyncOperation()
	{
		Output.WriteLine("Begin test");

		Job job1 = RunCorrelationIDTest(1);
		Job job2 = RunCorrelationIDTest(2);
		Job job3 = RunCorrelationIDTest(3);
		Job.WaitAll(job1, job2, job3);

		Output.WriteLine("End test");

		Assert.True(CorrelationIdScope.Current() == null);
	}

	private async Job RunCorrelationIDTest(Int32 testNum)
	{
		{
			using var scope = CorrelationIdScope.Create();
			CorrelationIdScope? corrIDHolder = CorrelationIdScope.Current();
			Assert.True(corrIDHolder != null);

			await Job.Yield();

			Assert.True(CorrelationIdScope.Current()!.CorrelationID == corrIDHolder.CorrelationID);
			Output.WriteLine("RunCorrelationIDTest: Correlation ID for test {0} is {1}",
				testNum,
				CorrelationIdScope.Current()!.CorrelationID);

			await NestedAsyncMethodCall(testNum, corrIDHolder.CorrelationID, 1).ConfigureAwait(false);
			Assert.True(CorrelationIdScope.Current()!.CorrelationID == corrIDHolder.CorrelationID);
		}

		Assert.True(CorrelationIdScope.Current() == null);
	}

	private async Job NestedAsyncMethodCall(Int32 testNum, Int64 expectedCorrID, Int32 depth)
	{
		await Job.Yield();

		Assert.True(CorrelationIdScope.Current()!.CorrelationID == expectedCorrID);
		Output.WriteLine("NestedAsyncMethodCall<{0}>: Correlation ID for test {1} is {2}",
			depth,
			testNum,
			CorrelationIdScope.Current()!.CorrelationID);

		await Job.Yield();
		Assert.True(CorrelationIdScope.Current()!.CorrelationID == expectedCorrID);

		if (depth < 3)
			await NestedAsyncMethodCall(testNum, expectedCorrID, depth + 1).ConfigureAwait(false);
	}

	#endregion // CorrelationID test

	#region JobRuntimeScopeWithAsyncTask test

	[Fact]
	public void JobRuntimeScopeWithAsyncTask()
	{
		Output.WriteLine("Begin test");

		Job job = RunJobRuntimeScopeWithAsyncTask();
		job.Wait();

		Output.WriteLine("End test");
	}

	private async Job RunJobRuntimeScopeWithAsyncTask()
	{
		// Create a scope with a string value
		using var scope = JobRuntimeScope.Enter<String>(() => "Test");

		// Validate the scope value
		Assert.True(JobRuntimeScope.GetValue<String>() == "Test");

		await Job.Yield();
		Assert.True(JobRuntimeScope.GetValue<String>() == "Test");

		// Run some async Job
		await Job.Run(AsyncExecutableMethod).ConfigureAwait(false);
		Assert.True(JobRuntimeScope.GetValue<String>() == "Test");

		// Run some async Task
		await Task.Run(AsyncExecutableMethod).ConfigureAwait(false);
		Assert.True(JobRuntimeScope.GetValue<String>() == "Test");
	}

	private void AsyncExecutableMethod()
	{
		Output.WriteLine("Running AsyncExecutableMethod");
	}

	#endregion // JobRuntimeScopeWithAsyncTask test

	#region NestedJobRuntimeScopes test

	[Fact]
	public void NestedJobRuntimeScopes()
	{
		Output.WriteLine("Begin test");

		Job job = RunNestedScopesAsyncTask(1, 3);
		job.Wait();

		Output.WriteLine("End test");
	}

	private async Job RunNestedScopesAsyncTask(Int32 keyIndex, Int32 maxDepth)
	{
		if (keyIndex > 1)
		{
			// ensure that the caller scope is still valid
			for (Int32 callerIndex = 1; callerIndex < keyIndex; callerIndex++)
			{
				// Validate the scope value
				Assert.True(JobRuntimeScope.GetValue($"Key {callerIndex}") is String strValue && strValue == $"Value {callerIndex}");
			}
		}

		// Create a scope with a string value
		using var scope = JobRuntimeScope.Enter($"Key {keyIndex}", () => $"Value {keyIndex}");

		{
			// Validate the scope value
			Assert.True(JobRuntimeScope.GetValue($"Key {keyIndex}") is String strValue && strValue == $"Value {keyIndex}");
		}

		await Job.Yield();
		{
			// Validate the scope value
			Assert.True(JobRuntimeScope.GetValue($"Key {keyIndex}") is String strValue && strValue == $"Value {keyIndex}");
		}

		// Run some async Job
		await Job.Run(AsyncExecutableNestedMethod, (Object?)keyIndex).ConfigureAwait(false);
		{
			// Validate the scope value
			Assert.True(JobRuntimeScope.GetValue($"Key {keyIndex}") is String strValue && strValue == $"Value {keyIndex}");
		}

		// recursively run this method
		if (keyIndex < maxDepth)
		{
			await RunNestedScopesAsyncTask(keyIndex + 1, maxDepth).ConfigureAwait(false);
			{
				// Validate the scope value
				Assert.True(JobRuntimeScope.GetValue($"Key {keyIndex}") is String strValue && strValue == $"Value {keyIndex}");
			}
		}

		if (keyIndex < maxDepth)
		{
			// ensure that the deeper scopes are not visible
			for (Int32 deeperIndex = keyIndex + 1; deeperIndex <= maxDepth; deeperIndex++)
			{
				// Validate the scope value
				Assert.True(JobRuntimeScope.GetValue($"Key {deeperIndex}") == null);
			}
		}
	}

	private void AsyncExecutableNestedMethod(Object? state)
	{
		Output.WriteLine($"Running AsyncExecutableNestedMethod<{state}>");
	}

	#endregion // NestedJobRuntimeScopes test
}
