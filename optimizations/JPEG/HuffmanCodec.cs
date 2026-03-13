using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JPEG.Utilities;

namespace JPEG;

public class HuffmanNode
{
	public byte? LeafLabel { get; set; }
	public int Frequency { get; set; }
	public HuffmanNode Left { get; set; }
	public HuffmanNode Right { get; set; }
}

public struct BitsWithLength : IEquatable<BitsWithLength>
{
	public int Bits;
	public int BitsCount;

	public bool Equals(BitsWithLength other)
	{
		return Bits == other.Bits && BitsCount == other.BitsCount;
	}

	public override bool Equals(object obj)
	{
		return obj is BitsWithLength other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Bits, BitsCount);
	}
}

class BitsBuffer
{
	private readonly byte[] _buffer;
	private int _position;
	private int _pendingBits;
	private int _pendingCount;

	public BitsBuffer(int bytes)
	{
		_buffer = new byte[bytes];
		_position = 0;
		_pendingBits = 0;
		_pendingCount = 0;
	}

	public void Add(int bits, int count)
	{
		int totalCount = _pendingCount + count;
		int totalBits = (_pendingBits << count) | bits;
    
		while (totalCount >= 8)
		{
			totalCount -= 8;
			_buffer[_position++] = (byte)(totalBits >> totalCount);
			totalBits &= (1 << totalCount) - 1;
		}

		_pendingCount = totalCount;
		_pendingBits = totalBits;
	}

	public byte[] ToArray(out long bitsCount)
	{
		bitsCount = ((long)_position << 3) + _pendingCount;
		var result = new byte[(bitsCount + 7) >> 3];
		Array.Copy(_buffer, 0, result, 0, _position);
        
		if (_pendingCount > 0)
			result[_position] = (byte)(_pendingBits << (8 - _pendingCount));
            
		return result;
	}
}

class HuffmanCodec
{
	public static byte[] Encode(byte[] data, out HuffmanNode treeRoot, out long bitsCount)
	{
		var frequences = CalcFrequences(data);
		treeRoot = BuildHuffmanTree(frequences);

		var encodeTable = new BitsWithLength[byte.MaxValue + 1];
		FillEncodeTable(treeRoot, encodeTable);

		var threadCount = Environment.ProcessorCount;
		var chunkSize = (data.Length + threadCount - 1) / threadCount;
		var chunks = new (byte[] bytes, long bits)[threadCount];

		Parallel.For(0, threadCount, i =>
		{
			var start = i * chunkSize;
			var end = Math.Min(start + chunkSize, data.Length);
			if (start >= end) { chunks[i] = (Array.Empty<byte>(), 0); return; }

			var buffer = new BitsBuffer(end - start);
			for (var j = start; j < end; j++)
			{
				var code = encodeTable[data[j]];
				buffer.Add(code.Bits, code.BitsCount);
			}
			chunks[i] = (buffer.ToArray(out var chunkBits), chunkBits);
		});

		return MergeChunks(chunks, out bitsCount);
	}

	private static byte[] MergeChunks((byte[] bytes, long bits)[] chunks, out long totalBits)
	{
		totalBits = 0;
		foreach (var c in chunks) totalBits += c.bits;

		var result = new byte[(totalBits + 7) / 8];
		var bitPos = 0L;

		foreach (var (bytes, bits) in chunks)
		{
			var byteOffset = (int)(bitPos >> 3);
			var bitOffset  = (int)(bitPos & 7);

			if (bitOffset == 0)
				Array.Copy(bytes, 0, result, byteOffset, bytes.Length);
			else
			{
				for (var i = 0; i < bytes.Length; i++)
				{
					result[byteOffset + i]     |= (byte)(bytes[i] >> bitOffset);
					if (byteOffset + i + 1 < result.Length)
						result[byteOffset + i + 1] |= (byte)(bytes[i] << (8 - bitOffset));
				}
			}

			bitPos += bits;
		}

		return result;
	}
	
	public static byte[] Decode(byte[] encodedData, HuffmanNode treeRoot, long bitsCount)
	{
		var result = new List<byte>();
		var node = treeRoot;

		for (var byteNum = 0; byteNum < encodedData.Length; ++byteNum)
		{
			var b = encodedData[byteNum];
			for (var bitNum = 0; bitNum < 8 && byteNum * 8 + bitNum < bitsCount; ++bitNum)
			{
				node = (b & (1 << (7 - bitNum))) != 0 ? node.Left : node.Right;

				if (node.LeafLabel != null)
				{
					result.Add(node.LeafLabel.Value);
					node = treeRoot;
				}
			}
		}

		return result.ToArray();
	}

	private static void FillEncodeTable(HuffmanNode node, BitsWithLength[] encodeSubstitutionTable,
		int bitvector = 0, int depth = 0)
	{
		if (node.LeafLabel != null)
			encodeSubstitutionTable[node.LeafLabel.Value] =
				new BitsWithLength { Bits = bitvector, BitsCount = depth };
		else
		{
			if (node.Left != null)
			{
				FillEncodeTable(node.Left, encodeSubstitutionTable, (bitvector << 1) + 1, depth + 1);
				FillEncodeTable(node.Right, encodeSubstitutionTable, (bitvector << 1) + 0, depth + 1);
			}
		}
	}

	private static HuffmanNode BuildHuffmanTree(int[] frequences)
	{
		var nodes = GetNodes(frequences);

		while (nodes.Count > 1)
		{
			var firstMin = nodes.Dequeue();
			var secondMin = nodes.Dequeue();
			var combined = new HuffmanNode
				{ Frequency = firstMin.Frequency + secondMin.Frequency, Left = secondMin, Right = firstMin };
			nodes.Enqueue(combined, combined.Frequency);
		}

		return nodes.Dequeue();
	}

	private static PriorityQueue<HuffmanNode, int> GetNodes(int[] frequences)
	{
		var queue = new PriorityQueue<HuffmanNode, int>();
		for (var i = 0; i < frequences.Length; ++i)
		{
			if (frequences[i] > 0)
			{
				var node = new HuffmanNode { Frequency = frequences[i], LeafLabel = (byte)i };
				queue.Enqueue(node, node.Frequency);
			}
		}
		return queue;
	}

	private static int[] CalcFrequences(byte[] data)
	{
		var result = new int[byte.MaxValue + 1];
		for (var i = 0; i < data.Length; ++i)
			++result[data[i]];
		return result;
	}
}