using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace JPEG.Utilities;

public static class MathEx
{
	public static double Sum(int from, int to, Func<int, double> function)
		=> Enumerable.Range(from, to - from).Sum(function);

	public static double SumByTwoVariables(int from1, int to1, int from2, int to2, Func<int, int, double> function)
		=> Sum(from1, to1, x => Sum(from2, to2, y => function(x, y)));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void LoopByTwoVariables(int from1, int to1, int from2, int to2, Action<int, int> function)
	{
		for (var x = from1; x < to1; ++x)
			for (var y =  from2; y < to2; ++y)
				function(x, y);
	}
}