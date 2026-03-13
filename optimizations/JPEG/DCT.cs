using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using JPEG.Utilities;

namespace JPEG;

public class DCT
{
	private static double[] _precalcCosX;
	private static double[] _precalcCosY;
	private static double[] _temp;
	private static double[] _coeffs;
	private static readonly double AlphaValue = 0.70710678118;
	const double BetaValue = 0.25;
	// private static readonly Dictionary<(int height, int width), double> BetaStorage = new();
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void DCT2D(Span<double> input, int height, int width)
	{
	    if (_precalcCosX is null || _precalcCosY is null)
	        PrepareCos(width, height);

	    Span<double> temp = stackalloc double[64];
	    var vecSize = Vector<double>.Count;

	    for (var v = 0; v < height; ++v)
	    {
	        int rowOffset = v * width;
	        for (var u = 0; u < width; ++u)
	        {
	            var cosOffset = u * width;
	            var sumVec = Vector<double>.Zero;
	            var x = 0;
	            
	            for (; x <= width - vecSize; x += vecSize)
	            {
	                var valVec = new Vector<double>(input.Slice(rowOffset + x, vecSize));
	                var cosVec = new Vector<double>(_precalcCosX, cosOffset + x);
	                sumVec += valVec * cosVec;
	            }

	            double sum = Vector.Dot(sumVec, Vector<double>.One);

	            for (; x < width; x++)
	                sum += input[rowOffset + x] * _precalcCosX[cosOffset + x];

	            temp[rowOffset + u] = sum * (u == 0 ? 0.70710678118 : 1.0);
	        }
	    }

	    for (var u = 0; u < width; u++)
	    {
	        for (var v = 0; v < height; v++)
	        {
	            var cosOffset = v * height;
	            var sum = 0d;
	            for (var y = 0; y < height; y++)
	                sum += temp[y * width + u] * _precalcCosY[cosOffset + y];

	            input[v * width + u] = sum * (v == 0 ? 0.70710678118 : 1.0) * 0.25;
	        }
	    }
	}

	public static void IDCT2D(double[] coeffs, double[] output, int height, int width)
	{
		if (_precalcCosX is null || _precalcCosY is null)
			PrepareCos(width, height);
    
		if (_temp is null)
			_temp = new double[width * height];
		else
			Array.Clear(_temp, 0, _temp.Length);
		
		for (var u = 0; u < width; ++u)
		{
			var alphaU = u ==0 ? 0.70710678118 : 1;
			var uCoeffOffset = u * height;
			var uCosOffset = u * width;
        
			for (var v = 0; v < height; ++v)
			{
				var coeff = coeffs[uCoeffOffset + v] * alphaU * (v ==0 ? 0.70710678118 : 1) * BetaValue;
				for (var x = 0; x < width; ++x)
				{
					var cosX = _precalcCosX[uCosOffset + x];
					_temp[x * width + v] += coeff * cosX;
				}
			}
		}
    
		for (var x = 0; x < width; ++x)
		{
			var xOffset = x * width;
			for (var y = 0; y < height; ++y)
			{
				var sum = 0d;
				for (var v = 0; v < height; v++)
					sum += _temp[xOffset + v] * _precalcCosY[v * height + y];
            
				output[xOffset + y] = sum;
			}
		}
	}

	private static void PrepareCos(int width, int height)
	{
		_precalcCosX = new double[width * width];
		_precalcCosY = new double[height * height];

		for (int u = 0; u < width; u++)
		for (int x = 0; x < width; x++)
			_precalcCosX[u * width + x] = Math.Cos((2d * x + 1d) * u * Math.PI / (2 * width));

		for (int v = 0; v < height; v++)
		for (int y = 0; y < height; y++)
			_precalcCosY[v * height + y] = Math.Cos((2d * y + 1d) * v * Math.PI / (2 * height));
	}
	//
	// [MethodImpl(MethodImplOptions.AggressiveInlining)]
	// private static double Alpha(int u) => u == 0 ? 0.70710678118 : 1;

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