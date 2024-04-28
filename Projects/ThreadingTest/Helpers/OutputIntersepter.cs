using System;
using System.Text;

using Xunit.Abstractions;

namespace Armat.Threading;

// Acts as a proxy layer on top of ITestOutputHelper interface
// and allows reading the output data and resetting it for teh consumers
// is used by test cases to trace asynchronous execution logs and verify the execution behavior
public class OutputInterceptor : ITestOutputHelper
{
	public OutputInterceptor(ITestOutputHelper inner)
	{
		Inner = inner;
		StringBuilder = new StringBuilder();
	}

	public ITestOutputHelper Inner { get; }
	private StringBuilder StringBuilder { get; }

	public void WriteLine(String message)
	{
		StringBuilder
			.Append(message)
			.Append(System.Environment.NewLine);

		Inner.WriteLine(message);
	}

	public void WriteLine(String format, params Object[] args)
	{
		StringBuilder
			.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, format, args)
			.Append(System.Environment.NewLine);

		Inner.WriteLine(format, args);
	}

	public String[] GetLines()
	{
		return StringBuilder.ToString().Split(System.Environment.NewLine);
	}

	public void Reset()
	{
		StringBuilder.Clear();
	}
}
