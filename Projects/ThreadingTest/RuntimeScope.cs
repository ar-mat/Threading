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

	#region CorrelationIDTest

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

	#endregion // CorrelationIDTest
}
