using System;
using System.Collections.Generic;
using System.IO;

namespace JPEG;

public class CompressedImage
{
	public int Width { get; set; }
	public int Height { get; set; }

	public int Quality { get; set; }
		
	public HuffmanNode TreeRoot { get; set; }

	public long BitsCount { get; set; }
	public byte[] CompressedBytes { get; set; }

	public void Save(string path)
	{
		using(var sw = new FileStream(path, FileMode.Create))
		{
			byte[] buffer;

			buffer = BitConverter.GetBytes(Width);
			sw.Write(buffer, 0, buffer.Length);

			buffer = BitConverter.GetBytes(Height);
			sw.Write(buffer, 0, buffer.Length);

			buffer = BitConverter.GetBytes(Quality);
			sw.Write(buffer, 0, buffer.Length);

			WriteTree(sw, TreeRoot);

			buffer = BitConverter.GetBytes(BitsCount);
			sw.Write(buffer, 0, buffer.Length);

			buffer = BitConverter.GetBytes(CompressedBytes.Length);
			sw.Write(buffer, 0, buffer.Length);

			sw.Write(CompressedBytes, 0, CompressedBytes.Length);
		}
	}

	public static CompressedImage Load(string path)
	{
		var result = new CompressedImage();
		using(var sr = new FileStream(path, FileMode.Open))
		{
			byte[] buffer = new byte[8];

			sr.Read(buffer, 0, 4);
			result.Width = BitConverter.ToInt32(buffer, 0);

			sr.Read(buffer, 0, 4);
			result.Height = BitConverter.ToInt32(buffer, 0);

			sr.Read(buffer, 0, 4);
			result.Quality = BitConverter.ToInt32(buffer, 0);

			result.TreeRoot = ReadTree(sr);

			sr.Read(buffer, 0, 8);
			result.BitsCount = BitConverter.ToInt64(buffer, 0);

			sr.Read(buffer, 0, 4);
			var compressedBytesCount = BitConverter.ToInt32(buffer, 0);

			result.CompressedBytes = new byte[compressedBytesCount];
			var totalRead = 0;
			while(totalRead < compressedBytesCount)
				totalRead += sr.Read(result.CompressedBytes, totalRead, compressedBytesCount - totalRead);
		}
		return result;
	}
	
	private static void WriteTree(Stream s, HuffmanNode node)
	{
		if (node.LeafLabel != null)
		{
			s.WriteByte(1);
			s.WriteByte(node.LeafLabel.Value);
		}
		else
		{
			s.WriteByte(0);
			WriteTree(s, node.Left);
			WriteTree(s, node.Right);
		}
	}

	private static HuffmanNode ReadTree(Stream s)
	{
		var flag = s.ReadByte();
		if (flag == 1)
			return new HuffmanNode { LeafLabel = (byte)s.ReadByte() };

		return new HuffmanNode
		{
			Left  = ReadTree(s),
			Right = ReadTree(s)
		};
	}
}