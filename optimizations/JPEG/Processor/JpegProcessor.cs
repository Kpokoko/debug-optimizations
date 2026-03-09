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
		var y = new double[bmp.Height, bmp.Width];
		var cb =  new double[bmp.Height, bmp.Width];
		var cr = new double[bmp.Height, bmp.Width];
		PreparePixelsInfo(y, cb, cr, bmp);
		//Console.WriteLine($"{bmp.Width}x{bmp.Height} - {fileStream.Length / (1024.0 * 1024):F2} MB");
		var compressionResult = Compress(y, cb, cr, bmp.Width, bmp.Height, CompressionQuality);
		compressionResult.Save(compressedImagePath);
	}

	private unsafe void PreparePixelsInfo(double[,] y, double[,] cb, double[,] cr, Bitmap bmp)
	{
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
			for (var i = 0; i < width; i++)
			{
				var pixel = row + i * pixelSize;
				var b = pixel[0];
				var g = pixel[1];
				var r = pixel[2];
				y[j, i] = 16.0 + (65.738 * r + 129.057 * g + 24.064 * b) / 256.0;
				cb[j, i] = 128.0 + (-37.945 * r - 74.494 * g + 112.439 * b) / 256.0;
				cr[j, i] = 128.0 + (112.439 * r - 94.154 * g - 18.285 * b) / 256.0;
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

	private static CompressedImage Compress(double[,] yChannel, double[,] cb, double[,] cr,
		int width, int height, int quality = 50)
	{
		var allQuantizedBytes = new List<byte>();

		for (var y = 0; y < height; y += DCTSize)
		{
			for (var x = 0; x < width; x += DCTSize)
			{
				var subMatrix = GetSubMatrix(yChannel, y, DCTSize, x, DCTSize);
				ShiftMatrixValues(subMatrix, -128);
				var channelFreqs = DCT.DCT2D(subMatrix);
				var quantizedFreqs = Quantize(channelFreqs, quality);
				var quantizedBytes = ZigZagScan(quantizedFreqs);
				allQuantizedBytes.AddRange(quantizedBytes);
				
				subMatrix = GetSubMatrix(cb, y, DCTSize, x, DCTSize);
				ShiftMatrixValues(subMatrix, -128);
				channelFreqs = DCT.DCT2D(subMatrix);
				quantizedFreqs = Quantize(channelFreqs, quality);
				quantizedBytes = ZigZagScan(quantizedFreqs);
				allQuantizedBytes.AddRange(quantizedBytes);
				
				subMatrix = GetSubMatrix(cr, y, DCTSize, x, DCTSize);
				ShiftMatrixValues(subMatrix, -128);
				channelFreqs = DCT.DCT2D(subMatrix);
				quantizedFreqs = Quantize(channelFreqs, quality);
				quantizedBytes = ZigZagScan(quantizedFreqs);
				allQuantizedBytes.AddRange(quantizedBytes);
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
		var result = new Bitmap(image.Width, image.Height);
		using (var allQuantizedBytes =
		       new MemoryStream(HuffmanCodec.Decode(image.CompressedBytes, image.DecodeTable, image.BitsCount)))
		{
			for (var y = 0; y < image.Height; y += DCTSize)
			{
				for (var x = 0; x < image.Width; x += DCTSize)
				{
					var _y = new double[DCTSize, DCTSize];
					var cb = new double[DCTSize, DCTSize];
					var cr = new double[DCTSize, DCTSize];
					foreach (var channel in new[] { _y, cb, cr })
					{
						var quantizedBytes = new byte[DCTSize * DCTSize];
						allQuantizedBytes.ReadAsync(quantizedBytes, 0, quantizedBytes.Length).Wait();
						var quantizedFreqs = ZigZagUnScan(quantizedBytes);
						var channelFreqs = DeQuantize(quantizedFreqs, image.Quality);
						DCT.IDCT2D(channelFreqs, channel);
						ShiftMatrixValues(channel, 128);
					}

					SetPixels(result, _y, cb, cr, y, x);
				}
			}
		}

		return result;
	}

	private static void ShiftMatrixValues(double[,] subMatrix, int shiftValue)
	{
		var height = subMatrix.GetLength(0);
		var width = subMatrix.GetLength(1);

		for (var y = 0; y < height; y++)
		for (var x = 0; x < width; x++)
			subMatrix[y, x] = subMatrix[y, x] + shiftValue;
	}

	private static void SetPixels(Bitmap bmp, double[,] yChannel, double[,] cbChannel, double[,] crChannel,
		int yOffset, int xOffset)
	{
		var height = yChannel.GetLength(0);
		var width = yChannel.GetLength(1);

		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				double _y = yChannel[y, x];
				double cb = cbChannel[y, x];
				double cr = crChannel[y, x];

				var rByte = (byte)Math.Clamp((298.082 * _y + 408.583 * cr) / 256.0 - 222.921, 0, 255);
				var gByte = (byte)Math.Clamp((298.082 * _y - 100.291 * cb - 208.120 * cr) / 256.0 + 135.576, 0, 255);
				var bByte = (byte)Math.Clamp((298.082 * _y + 516.412 * cb) / 256.0 - 276.836, 0, 255);

				bmp.SetPixel(xOffset + x, yOffset + y, Color.FromArgb(rByte, gByte, bByte));
			}
		}
	}

	private static double[,] GetSubMatrix(double[,] channel, int yOffset, int yLength, int xOffset, int xLength)
	{
		var result = new double[yLength, xLength];
		for (var j = 0; j < yLength; j++)
		for (var i = 0; i < xLength; i++)
			result[j, i] = channel[yOffset + j, xOffset + i];
		return result;
	}

	private static IEnumerable<byte> ZigZagScan(byte[,] channelFreqs)
	{
		return new[]
		{
			channelFreqs[0, 0], channelFreqs[0, 1], channelFreqs[1, 0], channelFreqs[2, 0], channelFreqs[1, 1],
			channelFreqs[0, 2], channelFreqs[0, 3], channelFreqs[1, 2],
			channelFreqs[2, 1], channelFreqs[3, 0], channelFreqs[4, 0], channelFreqs[3, 1], channelFreqs[2, 2],
			channelFreqs[1, 3], channelFreqs[0, 4], channelFreqs[0, 5],
			channelFreqs[1, 4], channelFreqs[2, 3], channelFreqs[3, 2], channelFreqs[4, 1], channelFreqs[5, 0],
			channelFreqs[6, 0], channelFreqs[5, 1], channelFreqs[4, 2],
			channelFreqs[3, 3], channelFreqs[2, 4], channelFreqs[1, 5], channelFreqs[0, 6], channelFreqs[0, 7],
			channelFreqs[1, 6], channelFreqs[2, 5], channelFreqs[3, 4],
			channelFreqs[4, 3], channelFreqs[5, 2], channelFreqs[6, 1], channelFreqs[7, 0], channelFreqs[7, 1],
			channelFreqs[6, 2], channelFreqs[5, 3], channelFreqs[4, 4],
			channelFreqs[3, 5], channelFreqs[2, 6], channelFreqs[1, 7], channelFreqs[2, 7], channelFreqs[3, 6],
			channelFreqs[4, 5], channelFreqs[5, 4], channelFreqs[6, 3],
			channelFreqs[7, 2], channelFreqs[7, 3], channelFreqs[6, 4], channelFreqs[5, 5], channelFreqs[4, 6],
			channelFreqs[3, 7], channelFreqs[4, 7], channelFreqs[5, 6],
			channelFreqs[6, 5], channelFreqs[7, 4], channelFreqs[7, 5], channelFreqs[6, 6], channelFreqs[5, 7],
			channelFreqs[6, 7], channelFreqs[7, 6], channelFreqs[7, 7]
		};
	}

	private static byte[,] ZigZagUnScan(IReadOnlyList<byte> quantizedBytes)
	{
		return new[,]
		{
			{
				quantizedBytes[0], quantizedBytes[1], quantizedBytes[5], quantizedBytes[6], quantizedBytes[14],
				quantizedBytes[15], quantizedBytes[27], quantizedBytes[28]
			},
			{
				quantizedBytes[2], quantizedBytes[4], quantizedBytes[7], quantizedBytes[13], quantizedBytes[16],
				quantizedBytes[26], quantizedBytes[29], quantizedBytes[42]
			},
			{
				quantizedBytes[3], quantizedBytes[8], quantizedBytes[12], quantizedBytes[17], quantizedBytes[25],
				quantizedBytes[30], quantizedBytes[41], quantizedBytes[43]
			},
			{
				quantizedBytes[9], quantizedBytes[11], quantizedBytes[18], quantizedBytes[24], quantizedBytes[31],
				quantizedBytes[40], quantizedBytes[44], quantizedBytes[53]
			},
			{
				quantizedBytes[10], quantizedBytes[19], quantizedBytes[23], quantizedBytes[32], quantizedBytes[39],
				quantizedBytes[45], quantizedBytes[52], quantizedBytes[54]
			},
			{
				quantizedBytes[20], quantizedBytes[22], quantizedBytes[33], quantizedBytes[38], quantizedBytes[46],
				quantizedBytes[51], quantizedBytes[55], quantizedBytes[60]
			},
			{
				quantizedBytes[21], quantizedBytes[34], quantizedBytes[37], quantizedBytes[47], quantizedBytes[50],
				quantizedBytes[56], quantizedBytes[59], quantizedBytes[61]
			},
			{
				quantizedBytes[35], quantizedBytes[36], quantizedBytes[48], quantizedBytes[49], quantizedBytes[57],
				quantizedBytes[58], quantizedBytes[62], quantizedBytes[63]
			}
		};
	}

	private static byte[,] Quantize(double[,] channelFreqs, int quality)
	{
		var result = new byte[channelFreqs.GetLength(0), channelFreqs.GetLength(1)];

		var quantizationMatrix = GetQuantizationMatrix(quality);
		for (int y = 0; y < channelFreqs.GetLength(0); y++)
		{
			for (int x = 0; x < channelFreqs.GetLength(1); x++)
			{
				result[y, x] = (byte)(channelFreqs[y, x] / quantizationMatrix[y, x]);
			}
		}

		return result;
	}

	private static double[,] DeQuantize(byte[,] quantizedBytes, int quality)
	{
		var result = new double[quantizedBytes.GetLength(0), quantizedBytes.GetLength(1)];
		var quantizationMatrix = GetQuantizationMatrix(quality);

		for (int y = 0; y < quantizedBytes.GetLength(0); y++)
		{
			for (int x = 0; x < quantizedBytes.GetLength(1); x++)
			{
				result[y, x] =
					((sbyte)quantizedBytes[y, x]) *
					quantizationMatrix[y, x]; //NOTE cast to sbyte not to loose negative numbers
			}
		}

		return result;
	}

	private static int[,] GetQuantizationMatrix(int quality)
	{
		if (quality < 1 || quality > 99)
			throw new ArgumentException("quality must be in [1,99] interval");

		var multiplier = quality < 50 ? 5000 / quality : 200 - 2 * quality;

		var result = new[,]
		{
			{ 16, 11, 10, 16, 24, 40, 51, 61 },
			{ 12, 12, 14, 19, 26, 58, 60, 55 },
			{ 14, 13, 16, 24, 40, 57, 69, 56 },
			{ 14, 17, 22, 29, 51, 87, 80, 62 },
			{ 18, 22, 37, 56, 68, 109, 103, 77 },
			{ 24, 35, 55, 64, 81, 104, 113, 92 },
			{ 49, 64, 78, 87, 103, 121, 120, 101 },
			{ 72, 92, 95, 98, 112, 100, 103, 99 }
		};

		for (int y = 0; y < result.GetLength(0); y++)
		{
			for (int x = 0; x < result.GetLength(1); x++)
			{
				result[y, x] = (multiplier * result[y, x] + 50) / 100;
			}
		}

		return result;
	}
}