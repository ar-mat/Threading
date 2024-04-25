using System;
using System.Text;

using Xunit.Abstractions;

namespace Armat.Threading;

public class OutputInterceptor : ITestOutputHelper
{
	public OutputInterceptor(ITestOutputHelper inner)
	{
		Inner = inner;
		StringBuilder = new System.Text.StringBuilder();
	}

	public ITestOutputHelper Inner { get; }
	private System.Text.StringBuilder StringBuilder { get; }

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
