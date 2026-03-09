using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using JPEG.Images;
using PixelFormat = JPEG.Images.PixelFormat;

namespace JPEG.Processor;

public class JpegProcessor : IJpegProcessor
{
	public static readonly JpegProcessor Init = new();
	public const int CompressionQuality = 70;
	private const int DCTSize = 8;

	public void Compress(string imagePath, string compressedImagePath)
	{
		using var fileStream = File.OpenRead(imagePath);
		using var bmp = (Bitmap)Image.FromStream(fileStream, false, false);
		var y = new double[bmp.Height * bmp.Width];
		var cb =  new double[bmp.Height * bmp.Width];
		var cr = new double[bmp.Height * bmp.Width];
		PreparePixelsInfo(y, cb, cr, bmp);
		var compressionResult = Compress(y, cb, cr, bmp.Width, bmp.Height, CompressionQuality);
		compressionResult.Save(compressedImagePath);
	}

	private unsafe void PreparePixelsInfo(double[] y, double[] cb, double[] cr, Bitmap bmp)
	{
		int fullHeight = bmp.Height;
		int fullWidth = bmp.Width;
		var height = bmp.Height - bmp.Height % 8;
		var width = bmp.Width - bmp.Width % 8;
		
		var rect = new Rectangle(0, 0, width, height);
		var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);
		var pixelFormat = bmp.PixelFormat;
		var pixelSize = pixelFormat is System.Drawing.Imaging.PixelFormat.Format24bppRgb ? 3 : 
				pixelFormat is System.Drawing.Imaging.PixelFormat.Format32bppArgb ? 4 : 
				pixelFormat is System.Drawing.Imaging.PixelFormat.Format32bppRgb ? 4 :
					throw new NotSupportedException("Unsupported pixel format");

		for (var j = 0; j < height; ++j)
		{
			var row = (byte*)bmpData.Scan0 + j * bmpData.Stride;
			var yOffset = j * fullWidth;
			for (var i = 0; i < width; i++)
			{
				var pixel = row + i * pixelSize;
				var b = pixel[0];
				var g = pixel[1];
				var r = pixel[2];
				var index = yOffset + i;
				y[index] = 16.0 + (65.738 * r + 129.057 * g + 24.064 * b) / 256.0;
				cb[index] = 128.0 + (-37.945 * r - 74.494 * g + 112.439 * b) / 256.0;
				cr[index] = 128.0 + (112.439 * r - 94.154 * g - 18.285 * b) / 256.0;
			}
		}
		
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
		var allQuantizedBytes = new List<byte>(width * height * 3 / 64 * 64);

		for (var y = 0; y < height; y += DCTSize)
		{
			for (var x = 0; x < width; x += DCTSize)
			{
				var subMatrix = GetSubMatrix(yChannel, y, DCTSize, x, DCTSize, width);
				ShiftMatrixValues(subMatrix, DCTSize, DCTSize, -128);
				var channelFreqs = DCT.DCT2D(subMatrix, DCTSize, DCTSize);
				var quantizedFreqs = Quantize(channelFreqs, quality);
				ZigZagScan(quantizedFreqs, allQuantizedBytes);
				
				subMatrix = GetSubMatrix(cb, y, DCTSize, x, DCTSize, width);
				ShiftMatrixValues(subMatrix, DCTSize, DCTSize, -128);
				channelFreqs = DCT.DCT2D(subMatrix, DCTSize, DCTSize);
				quantizedFreqs = Quantize(channelFreqs, quality);
				ZigZagScan(quantizedFreqs, allQuantizedBytes);
				
				subMatrix = GetSubMatrix(cr, y, DCTSize, x, DCTSize, width);
				ShiftMatrixValues(subMatrix, DCTSize, DCTSize, -128);
				channelFreqs = DCT.DCT2D(subMatrix, DCTSize, DCTSize);
				quantizedFreqs = Quantize(channelFreqs, quality);
				ZigZagScan(quantizedFreqs, allQuantizedBytes);
			}
		}

		long bitsCount;
		Dictionary<BitsWithLength, byte> decodeTable;
		var compressedBytes = HuffmanCodec.Encode(allQuantizedBytes, out decodeTable, out bitsCount);

		return new CompressedImage
		{
			Quality = quality, CompressedBytes = compressedBytes, BitsCount = bitsCount, DecodeTable = decodeTable,
			Height = height, Width = width
		};
	}

	private static Bitmap Uncompress(CompressedImage image)
	{
		var height = image.Height;
		var width = image.Width;
		var result = new Bitmap(width, height);
		var quantizedBytes = HuffmanCodec.Decode(image.CompressedBytes, image.DecodeTable, image.BitsCount);
		int quantIndex = 0;
		
		for (var y = 0; y < height; y += DCTSize)
		{
			for (var x = 0; x < width; x += DCTSize)
			{
				var yChannel = new double[DCTSize * DCTSize];
				var cbChannel = new double[DCTSize * DCTSize];
				var crChannel = new double[DCTSize * DCTSize];
				
				for (int c = 0; c < 3; c++)
				{
					var quantizedBlock = new byte[DCTSize * DCTSize];
					Array.Copy(quantizedBytes, quantIndex, quantizedBlock, 0, DCTSize * DCTSize);
					quantIndex += DCTSize * DCTSize;
					
					var quantizedFreqs = ZigZagUnScan(quantizedBlock);
					var channelFreqs = DeQuantize(quantizedFreqs, image.Quality);
					var channel = c == 0 ? yChannel : (c == 1 ? cbChannel : crChannel);
					DCT.IDCT2D(channelFreqs, channel, DCTSize, DCTSize);
					ShiftMatrixValues(channel, DCTSize, DCTSize, 128);
				}

				SetPixels(result, yChannel, cbChannel, crChannel, y, x, width);
			}
		}

		return result;
	}

	private static void ShiftMatrixValues(double[] subMatrix, int height, int width, int shiftValue)
	{
		for (int i = 0; i < height * width; i++)
			subMatrix[i] += shiftValue;
	}

	private static unsafe void SetPixels(Bitmap bmp, double[] yChannel, double[] cbChannel, double[] crChannel,
		int yOffset, int xOffset, int stride)
	{
		var rect = new Rectangle(xOffset, yOffset, DCTSize, DCTSize);
		var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
		var pixelSize = bmp.PixelFormat == System.Drawing.Imaging.PixelFormat.Format24bppRgb ? 3 : 4;
		
		for (int y = 0; y < DCTSize; y++)
		{
			var row = (byte*)bmpData.Scan0 + y * bmpData.Stride;
			var index = y * DCTSize;
			
			for (int x = 0; x < DCTSize; x++)
			{
				double _y = yChannel[index + x];
				double cb = cbChannel[index + x];
				double cr = crChannel[index + x];

				var rByte = (byte)Math.Clamp((298.082 * _y + 408.583 * cr) / 256.0 - 222.921, 0, 255);
				var gByte = (byte)Math.Clamp((298.082 * _y - 100.291 * cb - 208.120 * cr) / 256.0 + 135.576, 0, 255);
				var bByte = (byte)Math.Clamp((298.082 * _y + 516.412 * cb) / 256.0 - 276.836, 0, 255);

				var pixel = row + x * pixelSize;
				pixel[2] = rByte;
				pixel[1] = gByte;
				pixel[0] = bByte;
			}
		}
		
		bmp.UnlockBits(bmpData);
	}

	private static double[] GetSubMatrix(double[] channel, int yOffset, int yLength, int xOffset, int xLength, int stride)
	{
		var result = new double[yLength * xLength];
		for (int j = 0; j < yLength; j++)
		{
			var srcOffset = (yOffset + j) * stride + xOffset;
			var dstOffset = j * xLength;
			Array.Copy(channel, srcOffset, result, dstOffset, xLength);
		}
		return result;
	}

	private static void ZigZagScan(byte[] channelFreqs, List<byte> output)
	{
		output.Add(channelFreqs[0]);
		output.Add(channelFreqs[1]);
		output.Add(channelFreqs[8]); 
		output.Add(channelFreqs[16]);
		output.Add(channelFreqs[9]);
		output.Add(channelFreqs[2]);
		output.Add(channelFreqs[3]);
		output.Add(channelFreqs[10]);
		output.Add(channelFreqs[17]);
		output.Add(channelFreqs[24]);
		output.Add(channelFreqs[32]);
		output.Add(channelFreqs[25]);
		output.Add(channelFreqs[18]);
		output.Add(channelFreqs[11]);
		output.Add(channelFreqs[4]);
		output.Add(channelFreqs[5]);
		output.Add(channelFreqs[12]);
		output.Add(channelFreqs[19]);
		output.Add(channelFreqs[26]);
		output.Add(channelFreqs[33]);
		output.Add(channelFreqs[40]);
		output.Add(channelFreqs[48]);
		output.Add(channelFreqs[41]);
		output.Add(channelFreqs[34]);
		output.Add(channelFreqs[27]);
		output.Add(channelFreqs[20]);
		output.Add(channelFreqs[13]);
		output.Add(channelFreqs[6]);
		output.Add(channelFreqs[7]);
		output.Add(channelFreqs[14]);
		output.Add(channelFreqs[21]);
		output.Add(channelFreqs[28]);
		output.Add(channelFreqs[35]);
		output.Add(channelFreqs[42]);
		output.Add(channelFreqs[49]);
		output.Add(channelFreqs[56]);
		output.Add(channelFreqs[57]);
		output.Add(channelFreqs[50]);
		output.Add(channelFreqs[43]);
		output.Add(channelFreqs[36]);
		output.Add(channelFreqs[29]);
		output.Add(channelFreqs[22]);
		output.Add(channelFreqs[15]);
		output.Add(channelFreqs[23]);
		output.Add(channelFreqs[30]);
		output.Add(channelFreqs[37]);
		output.Add(channelFreqs[44]);
		output.Add(channelFreqs[51]);
		output.Add(channelFreqs[58]);
		output.Add(channelFreqs[59]);
		output.Add(channelFreqs[52]);
		output.Add(channelFreqs[45]);
		output.Add(channelFreqs[38]);
		output.Add(channelFreqs[31]);
		output.Add(channelFreqs[39]);
		output.Add(channelFreqs[46]);
		output.Add(channelFreqs[53]);
		output.Add(channelFreqs[60]);
		output.Add(channelFreqs[61]);
		output.Add(channelFreqs[54]);
		output.Add(channelFreqs[47]);
		output.Add(channelFreqs[55]);
		output.Add(channelFreqs[62]);
		output.Add(channelFreqs[63]);
	}

	private static byte[] ZigZagUnScan(byte[] quantizedBytes)
	{
		var result = new byte[64];
		result[0] = quantizedBytes[0];
		result[1] = quantizedBytes[1];
		result[8] = quantizedBytes[2];
		result[16] = quantizedBytes[3];
		result[9] = quantizedBytes[4];
		result[2] = quantizedBytes[5];
		result[3] = quantizedBytes[6];
		result[10] = quantizedBytes[7];
		result[17] = quantizedBytes[8];
		result[24] = quantizedBytes[9];
		result[32] = quantizedBytes[10];
		result[25] = quantizedBytes[11];
		result[18] = quantizedBytes[12];
		result[11] = quantizedBytes[13];
		result[4] = quantizedBytes[14];
		result[5] = quantizedBytes[15];
		result[12] = quantizedBytes[16];
		result[19] = quantizedBytes[17];
		result[26] = quantizedBytes[18];
		result[33] = quantizedBytes[19];
		result[40] = quantizedBytes[20];
		result[48] = quantizedBytes[21];
		result[41] = quantizedBytes[22];
		result[34] = quantizedBytes[23];
		result[27] = quantizedBytes[24];
		result[20] = quantizedBytes[25];
		result[13] = quantizedBytes[26];
		result[6] = quantizedBytes[27];
		result[7] = quantizedBytes[28];
		result[14] = quantizedBytes[29];
		result[21] = quantizedBytes[30];
		result[28] = quantizedBytes[31];
		result[35] = quantizedBytes[32];
		result[42] = quantizedBytes[33];
		result[49] = quantizedBytes[34];
		result[56] = quantizedBytes[35];
		result[57] = quantizedBytes[36];
		result[50] = quantizedBytes[37];
		result[43] = quantizedBytes[38];
		result[36] = quantizedBytes[39];
		result[29] = quantizedBytes[40];
		result[22] = quantizedBytes[41];
		result[15] = quantizedBytes[42];
		result[23] = quantizedBytes[43];
		result[30] = quantizedBytes[44];
		result[37] = quantizedBytes[45];
		result[44] = quantizedBytes[46];
		result[51] = quantizedBytes[47];
		result[58] = quantizedBytes[48];
		result[59] = quantizedBytes[49];
		result[52] = quantizedBytes[50];
		result[45] = quantizedBytes[51];
		result[38] = quantizedBytes[52];
		result[31] = quantizedBytes[53];
		result[39] = quantizedBytes[54];
		result[46] = quantizedBytes[55];
		result[53] = quantizedBytes[56];
		result[60] = quantizedBytes[57];
		result[61] = quantizedBytes[58];
		result[54] = quantizedBytes[59];
		result[47] = quantizedBytes[60];
		result[55] = quantizedBytes[61];
		result[62] = quantizedBytes[62];
		result[63] = quantizedBytes[63];
		return result;
	}

	private static byte[] Quantize(double[] channelFreqs, int quality)
	{
		var result = new byte[64];
		var quantizationMatrix = GetQuantizationMatrix(quality);
		
		for (int i = 0; i < 64; i++)
			result[i] = (byte)(channelFreqs[i] / quantizationMatrix[i]);

		return result;
	}

	private static double[] DeQuantize(byte[] quantizedBytes, int quality)
	{
		var result = new double[64];
		var quantizationMatrix = GetQuantizationMatrix(quality);

		for (int i = 0; i < 64; i++)
			result[i] = (sbyte)quantizedBytes[i] * quantizationMatrix[i];

		return result;
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
}