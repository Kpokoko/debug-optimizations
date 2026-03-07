using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JPEG.Utilities;

namespace JPEG;

public class DCT
{
	private static double[] _precalcCosX;
	private static double[] _precalcCosY;
	private static readonly double AlphaValue = 1 / Math.Sqrt(2);
	private static readonly Dictionary<(int height, int width), double> BetaStorage = new();
	
	public static double[,] DCT2D(double[,] input)
	{
		var height = input.GetLength(0);
		var width = input.GetLength(1);
		var temp = new double[width, height];
		var coeffs = new double[width, height];
		PrepareCos(width, height);

		MathEx.LoopByTwoVariables(
			0, width,
			0, height,
			(x, v) =>
			{
				var sum = 0d;
				for (var y = 0; y < height; ++y)
					sum += input[x, y] * _precalcCosY[y * height + v];

				temp[x, v] = sum;
			});

		MathEx.LoopByTwoVariables(
			0, width,
			0, height,
			(u, v) =>
			{
				var sum = 0d;
				for (var x = 0; x < width; ++x)
					sum += temp[x, v] * _precalcCosX[x * width + u];
				
				coeffs[u, v] = sum * Beta(height, width) * Alpha(u) * Alpha(v);
			});

		return coeffs;
	}

	public static void IDCT2D(double[,] coeffs, double[,] output)
	{
		var height = coeffs.GetLength(0);
		var width = coeffs.GetLength(1);
		PrepareCos(width, height);
		
		for (var x = 0; x < coeffs.GetLength(1); ++x)
		{
			for (var y = 0; y < coeffs.GetLength(0); ++y)
			{
				var sum = 0d;
				for (var u = 0; u < width; ++u)
				{
					var factorX = _precalcCosX[x * width + u];
					for (var v = 0; v < height; ++v)
						sum += coeffs[u, v] * factorX * _precalcCosY[y * height + v] * Alpha(v);
					sum *= Alpha(u);
				}

				output[x, y] = sum * Beta(height, width);
			}
		}
	}
	
	// public static double BasisFunction(double a, int u, int v, int x, int y, int height, int width)
	// {
	// 	return a * _precalcCosX[x * width + u] * _precalcCosY[y * height + v];
	// }

	private static void PrepareCos(int width, int height)
	{
		_precalcCosX = new double[width * width];
		_precalcCosY = new double[height * height];

		MathEx.LoopByTwoVariables(
			0, width,
			0, width,
			(u, x) => PrecalcCosX(u, x, width));

		MathEx.LoopByTwoVariables(
			0, height,
			0, height,
			(v, y) => PrecalcCosY(v, y, height));
	}

	private static void PrecalcCosX(int u, int x, int width)
	{
		_precalcCosX[x * width + u] = Math.Cos((2d * x + 1d) * u * Math.PI / (2 * width));
	}
	
	private static void PrecalcCosY(int v, int y, int height)
	{
		_precalcCosY[y * height + v] = Math.Cos((2d * y + 1d) * v * Math.PI / (2 * height));
	}
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double Alpha(int u) => u == 0 ? AlphaValue : 1;

	private static double Beta(int height, int width)
	{
		if (!BetaStorage.TryGetValue((height, width), out var beta))
		{
			beta = 1d / width + 1d / height;
			BetaStorage[(height, width)] = beta;
		}
		return beta;
	}
}