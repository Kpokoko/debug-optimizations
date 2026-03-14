using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace JPEG.Processor;

public class JpegProcessor : IJpegProcessor
{
	public static readonly JpegProcessor Init = new();
	public const int CompressionQuality = 70;
	private const int DCTSize = 8;
	private static readonly ConcurrentDictionary<int, int[]> QuantizeCache = new();

	public void Compress(string imagePath, string compressedImagePath)
	{
	    using var fileStream = File.OpenRead(imagePath);
	    using var bmp = (Bitmap)Image.FromStream(fileStream, false, false);
	    
	    var height = bmp.Height - bmp.Height % 8;
	    var width  = bmp.Width  - bmp.Width  % 8;
	    
	    var y  = new double[height * width];
	    var cb = new double[height / 2 * width / 2];
	    var cr = new double[height / 2 * width / 2];
	    PreparePixelsInfo(y, cb, cr, bmp, width, height);
	    var compressionResult = Compress(y, cb, cr, width, height, CompressionQuality);
	    compressionResult.Save(compressedImagePath);
	}

	private unsafe void PreparePixelsInfo(double[] y, double[] cb, double[] cr, Bitmap bmp, int width, int height)
	{
		var rect = new Rectangle(0, 0, width, height);
		var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);
		var pixelSize = bmp.PixelFormat switch
		{
			PixelFormat.Format24bppRgb  => 3,
			PixelFormat.Format32bppArgb => 4,
			PixelFormat.Format32bppRgb  => 4,
			_ => throw new NotSupportedException("Unsupported pixel format")
		};

		Parallel.For(0, height / 2, jBlock =>
		{
			var j    = jBlock * 2;
			var row1 = (byte*)bmpData.Scan0 + j * bmpData.Stride;
			var row2 = row1 + bmpData.Stride;
			var yRowOffset = j * width;  // теперь stride = width везде одинаковый
			var cbWidth = width / 2;

			for (var i = 0; i < width; i += 2)
			{
				var cbSum = 0d;
				var crSum = 0d;
				for (var dy = 0; dy < 2; ++dy)
				{
					var row = dy == 0 ? row1 : row2;
					for (var dx = 0; dx < 2; ++dx)
					{
						var pixel = row + (i + dx) * pixelSize;
						var b = pixel[0];
						var g = pixel[1];
						var r = pixel[2];
						var index = yRowOffset + dy * width + i + dx;
						y[index] = 16.0 + (65.738 * r + 129.057 * g + 24.064 * b) / 256.0;
						cbSum += 128.0 + (-37.945 * r - 74.494 * g + 112.439 * b) / 256.0;
						crSum += 128.0 + (112.439 * r - 94.154 * g - 18.285 * b) / 256.0;
					}
				}

				var cbCrIndex = j / 2 * cbWidth + i / 2;
				cb[cbCrIndex] = cbSum / 4;
				cr[cbCrIndex] = crSum / 4;
			}
		});

		bmp.UnlockBits(bmpData);
	}

	public void Uncompress(string compressedImagePath, string uncompressedImagePath)
	{
		var compressedImage = CompressedImage.Load(compressedImagePath);
		var resultBmp = Uncompress(compressedImage);
		resultBmp.Save(uncompressedImagePath, ImageFormat.Bmp);
	}

	private static CompressedImage Compress(double[] yChannel, double[] cb, double[] cr,
		int width, int height, int quality = 50)
	{
		var blocksX = (width + DCTSize * 2 - 1) / (DCTSize * 2);
		var blocksY = (height + DCTSize * 2 - 1) / (DCTSize * 2);
		var totalBlocks = blocksX * blocksY * 4;
		var totalCbCrBlocks = blocksX * blocksY;
		var allQuantizedBytes = new byte[(totalBlocks + totalCbCrBlocks * 2) * 64];

		Parallel.For(0, blocksY, yBlock =>
		{
			Span<double> blockBuffer = stackalloc double[64];
			for (var xBlock = 0; xBlock < blocksX; ++xBlock)
			{
				var y = yBlock * DCTSize * 2;
				var x = xBlock * DCTSize * 2;
				var offset = (yBlock * blocksX + xBlock) * 6 * 64;
				for (var dy = 0; dy < DCTSize * 2; dy += DCTSize)
				for (var dx = 0; dx < DCTSize * 2; dx += DCTSize)
				{
					GetSubMatrix(yChannel, y + dy, DCTSize, x + dx, DCTSize, width, height, blockBuffer);
					ShiftMatrixValues(blockBuffer, DCTSize, DCTSize, -128);
					DCT.DCT2D(blockBuffer, DCTSize, DCTSize);
					QuantizeAndZigZagInPlace(blockBuffer, quality, allQuantizedBytes, offset);
					offset += 64;
				}

				var cbWidth = width / 2;
				var cbHeight = height / 2;

				GetSubMatrix(cb, y / 2, DCTSize, x / 2, DCTSize, cbWidth, cbHeight, blockBuffer);
				ShiftMatrixValues(blockBuffer, DCTSize, DCTSize, -128);
				DCT.DCT2D(blockBuffer, DCTSize, DCTSize);
				QuantizeAndZigZagInPlace(blockBuffer, quality, allQuantizedBytes, offset);
				offset += 64;

				GetSubMatrix(cr, y / 2, DCTSize, x / 2, DCTSize, cbWidth, cbHeight, blockBuffer);
				ShiftMatrixValues(blockBuffer, DCTSize, DCTSize, -128);
				DCT.DCT2D(blockBuffer, DCTSize, DCTSize);
				QuantizeAndZigZagInPlace(blockBuffer, quality, allQuantizedBytes, offset);
			}
		});

		long bitsCount;
		HuffmanNode decodeTable;
		var compressedBytes = HuffmanCodec.Encode(allQuantizedBytes, out decodeTable, out bitsCount);

		return new CompressedImage
		{
			Quality = quality, CompressedBytes = compressedBytes, BitsCount = bitsCount, TreeRoot = decodeTable,
			Height = height, Width = width
		};
	}

	private static unsafe Bitmap Uncompress(CompressedImage image)
	{
		var height = image.Height;
		var width = image.Width;
		var result = new Bitmap(width, height);
		var rect = new Rectangle(0, 0, width, height);
		var bmpData = result.LockBits(rect, ImageLockMode.WriteOnly, result.PixelFormat);
		var pixelSize = result.PixelFormat is PixelFormat.Format24bppRgb ? 3 : 4;
		var scan = (byte*)bmpData.Scan0;
		var stride = bmpData.Stride;
		var quantizedBytes = HuffmanCodec.Decode(image.CompressedBytes, image.TreeRoot, image.BitsCount);
		var blockSize = DCTSize * 2;

		var blocksX = (width + DCTSize * 2 - 1) / (DCTSize * 2);
		var blocksY = (height + DCTSize * 2 - 1) / (DCTSize * 2);

		Parallel.For(0, blocksY, blockY =>
		{
			var yChannel = new double[blockSize * blockSize];
			var cbChannel = new double[DCTSize * DCTSize];
			var crChannel = new double[DCTSize * DCTSize];
			var preIDCTBuffer = new double[DCTSize * DCTSize];
			var postIDCTBuffer = new double[DCTSize * DCTSize];

			for (var blockX = 0; blockX < blocksX; ++blockX)
			{
				var quantIndex = (blockY * blocksX + blockX) * 6 * 64;
				var y = blockY * blockSize;
				var x = blockX * blockSize;
				for (var dy = 0; dy < DCTSize * 2; dy += DCTSize)
				for (var dx = 0; dx < DCTSize * 2; dx += DCTSize)
				{
					var quantizedBlock = new ReadOnlySpan<byte>(quantizedBytes, quantIndex, DCTSize * DCTSize);
					quantIndex += DCTSize * DCTSize;

					DequantizeAndZigZagInPlace(quantizedBlock, image.Quality, preIDCTBuffer);
					DCT.IDCT2D(preIDCTBuffer, postIDCTBuffer, DCTSize, DCTSize);
					ShiftMatrixValues(postIDCTBuffer, DCTSize, DCTSize, 128);
					for (var j = 0; j < DCTSize; j++)
						Array.Copy(postIDCTBuffer, j * DCTSize, yChannel, (dy + j) * blockSize + dx, DCTSize);
				}

				var cbQuant = new ReadOnlySpan<byte>(quantizedBytes, quantIndex, DCTSize * DCTSize);
				quantIndex += DCTSize * DCTSize;
				DequantizeAndZigZagInPlace(cbQuant, image.Quality, preIDCTBuffer);
				DCT.IDCT2D(preIDCTBuffer, postIDCTBuffer, DCTSize, DCTSize);
				ShiftMatrixValues(postIDCTBuffer, DCTSize, DCTSize, 128);
				Array.Copy(postIDCTBuffer, 0, cbChannel, 0, 64);

				var crQuant = new ReadOnlySpan<byte>(quantizedBytes, quantIndex, DCTSize * DCTSize);
				quantIndex += DCTSize * DCTSize;
				DequantizeAndZigZagInPlace(crQuant, image.Quality, preIDCTBuffer);
				DCT.IDCT2D(preIDCTBuffer, postIDCTBuffer, DCTSize, DCTSize);
				ShiftMatrixValues(postIDCTBuffer, DCTSize, DCTSize, 128);
				Array.Copy(postIDCTBuffer, 0, crChannel, 0, 64);

				var drawWidth = Math.Min(blockSize, width - x);
				var drawHeight = Math.Min(blockSize, height - y);
				if (drawWidth > 0 && drawHeight > 0)
					SetPixelsDirect(scan, stride, pixelSize, yChannel, cbChannel,
						crChannel, y, x, drawWidth, drawHeight);
			}
		});
		result.UnlockBits(bmpData);

		return result;
	}

	private static void ShiftMatrixValues(Span<double> buffer, int height, int width, int shiftValue)
	{
		for (int i = 0; i < height * width; i++)
			buffer[i] += shiftValue;
	}

	private static unsafe void SetPixelsDirect(byte* scan0, int stride, int pixelSize, 
		double[] yChannel, double[] cbChannel, double[] crChannel,
		int yOffset, int xOffset, int blockW, int blockH)
	{
		var blockSize = DCTSize * 2;
		
		for (var y = 0; y < blockH; ++y)
		{
			var row = scan0 + (yOffset + y) * stride + xOffset * pixelSize;
			var index = y * blockSize;
			for (int x = 0; x < blockW; ++x)
			{
				var _y = yChannel[index + x] - 16;
				var cbIndex = (y / 2) * DCTSize + (x / 2);
				var cb = cbChannel[cbIndex] - 128;
				var cr = crChannel[cbIndex] - 128;

				var y1 = 1.164 * _y;
				var r = y1 + 1.596 * cr;
				var g = y1 - 0.392 * cb - 0.813 * cr;
				var b = y1 + 2.017 * cb;

				var rByte = (byte)(r < 0 ? 0 : r > 255 ? 255 : r);
				var gByte = (byte)(g < 0 ? 0 : g > 255 ? 255 : g);
				var bByte = (byte)(b < 0 ? 0 : b > 255 ? 255 : b);

				var pixel = row + x * pixelSize;
				pixel[2] = rByte;
				pixel[1] = gByte;
				pixel[0] = bByte;
			}
		}
	}

	private static void GetSubMatrix(double[] channel, int yOffset, int yLength, int xOffset,
		int xLength, int stride, int channelHeight, Span<double> buffer)
	{
		for (var j = 0; j < yLength; ++j)
		{
			var row = Math.Min(yOffset + j, channelHeight - 1);
			var rowOffset = row * stride;
			var bufferRowOffset = j * 8;
			for (var i = 0; i < xLength; ++i)
			{
				var col = Math.Min(xOffset + i, stride - 1);
				buffer[bufferRowOffset + i] = channel[rowOffset + col];
			}
		}
	}

	private static int[] GetQuantizationMatrix(int quality)
	{
		if (quality < 1 || quality > 99)
			throw new ArgumentException("quality must be in [1,99] interval");

		var multiplier = quality < 50 ? 5000 / quality : 200 - 2 * quality;

		var result = new int[]
		{
			16, 11, 10, 16, 24, 40, 51, 61,
			12, 12, 14, 19, 26, 58, 60, 55,
			14, 13, 16, 24, 40, 57, 69, 56,
			14, 17, 22, 29, 51, 87, 80, 62,
			18, 22, 37, 56, 68, 109, 103, 77,
			24, 35, 55, 64, 81, 104, 113, 92,
			49, 64, 78, 87, 103, 121, 120, 101,
			72, 92, 95, 98, 112, 100, 103, 99
		};

		for (int i = 0; i < 64; i++)
			result[i] = (multiplier * result[i] + 50) / 100;

		return result;
	}
	
	private static readonly int[] ZigZagMap =
	[
		0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
		12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
		35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
		58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
	];

	private static unsafe void QuantizeAndZigZagInPlace(Span<double> dctBlock, int quality, byte[] output, int offset)
	{
		if (!QuantizeCache.TryGetValue(quality, out var qMatrix))
		{
			qMatrix = GetQuantizationMatrix(quality);
			QuantizeCache[quality] = qMatrix;
		}
		fixed (double* pBlock = dctBlock)
		fixed (int* pQ = qMatrix)
		fixed (int* pZig = ZigZagMap)
		{
			for (int i = 0; i < 64; i++)
			{
				var zIndex = pZig[i];
				output[offset + i] = (byte)(pBlock[zIndex] / pQ[zIndex]);
			}
		}
	}
	
	private static void DequantizeAndZigZagInPlace(ReadOnlySpan<byte> quantized, int quality, double[] output)
	{
		if (!QuantizeCache.TryGetValue(quality, out var qMatrix))
		{
			qMatrix = GetQuantizationMatrix(quality);
			QuantizeCache[quality] = qMatrix;
		}

		for (var i = 0; i < 64; ++i)
		{
			var targetIndex = ZigZagMap[i];
			output[targetIndex] = (double)(sbyte)quantized[i] * qMatrix[targetIndex];
		}
	}
}