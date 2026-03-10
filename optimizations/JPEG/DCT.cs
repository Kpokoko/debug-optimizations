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
	const double BetaValue = 0.25;
	// private static readonly Dictionary<(int height, int width), double> BetaStorage = new();
	
	public static double[] DCT2D(double[] input, int height, int width)
	{
		var temp = new double[width * height];
		var coeffs = new double[width * height];
		if (_precalcCosX is null || _precalcCosY is null)
			PrepareCos(width, height);

		for (var x = 0; x < width; ++x)
		{
			var xOffset = x * height;
			for (var v = 0; v < height; ++v)
			{
				var sum = 0d;
				for (var y = 0; y < height; ++y)
					sum += input[xOffset + y] * _precalcCosY[y * height + v];
				
				temp[xOffset + v] = sum;
			}
		}

		for (var u = 0; u < width; ++u)
		{
			var alphaU = Alpha(u);
			for (var v = 0; v < height; ++v)
			{
				var sum = 0d;
				for (var x = 0; x < width; ++x)
					sum += temp[x * height + v] * _precalcCosX[x * width + u];
				
				coeffs[u * height + v] = sum * BetaValue * alphaU * Alpha(v);
				//coeffs[u, v] = sum * Beta(height, width) * Alpha(u) * Alpha(v);
			}
		}

		return coeffs;
	}

	public static void IDCT2D(double[] coeffs, double[] output, int height, int width)
	{
		if (_precalcCosX is null || _precalcCosY is null)
			PrepareCos(width, height);
		
		var temp = new double[width * height];

		for (var u = 0; u < width; ++u)
		{
			var alphaU = Alpha(u);
			var uOffset = u * height;
        
			for (var v = 0; v < height; ++v)
			{
				var coeff = coeffs[uOffset + v] * alphaU * Alpha(v) * BetaValue;
				for (var x = 0; x < height; x++)
				{
					var cosX = _precalcCosX[x * width + u];
					temp[x * width + v] += coeff * cosX;
				}
			}
		}
		
		for (var x = 0; x < height; x++)
		{
			var xOffset = x * width;
			for (var y = 0; y < width; y++)
			{
				var sum = 0d;
				for (var v = 0; v < height; v++)
					sum += temp[xOffset + v] * _precalcCosY[y * height + v];
				
				output[xOffset + y] = sum;
			}
		}
	}

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

	// [MethodImpl(MethodImplOptions.AggressiveInlining)]
	// private static double Beta(int height, int width)
	// {
	// 	// if (!BetaStorage.TryGetValue((height, width), out var beta))
	// 	// {
	// 	// 	beta = 1d / width + 1d / height;
	// 	// 	BetaStorage[(height, width)] = beta;
	// 	// }
	// 	// return beta;
	// 	return 0.25;
	// }
}