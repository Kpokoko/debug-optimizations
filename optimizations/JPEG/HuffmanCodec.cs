using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JPEG.Utilities;

namespace JPEG;

class HuffmanNode
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
	public static byte[] Encode(List<byte> data, out Dictionary<BitsWithLength, byte> decodeTable,
		out long bitsCount)
	{
		var frequences = CalcFrequences(data);

		var root = BuildHuffmanTree(frequences);

		var encodeTable = new BitsWithLength[byte.MaxValue + 1];
		FillEncodeTable(root, encodeTable);

		var bitsBuffer = new BitsBuffer(data.Count);
		for (var i = 0; i < data.Count; ++i)
		{
			var code = encodeTable[data[i]];
			bitsBuffer.Add(code.Bits, code.BitsCount);
		}

		decodeTable = CreateDecodeTable(encodeTable);

		return bitsBuffer.ToArray(out bitsCount);
	}

	public static byte[] Decode(byte[] encodedData, Dictionary<BitsWithLength, byte> decodeTable, long bitsCount)
	{
		var result = new List<byte>();

		byte decodedByte;
		var sample = new BitsWithLength { Bits = 0, BitsCount = 0 };
		for (var byteNum = 0; byteNum < encodedData.Length; byteNum++)
		{
			var b = encodedData[byteNum];
			for (var bitNum = 0; bitNum < 8 && byteNum * 8 + bitNum < bitsCount; bitNum++)
			{
				sample.Bits = (sample.Bits << 1) + ((b & (1 << (8 - bitNum - 1))) != 0 ? 1 : 0);
				sample.BitsCount++;

				if (decodeTable.TryGetValue(sample, out decodedByte))
				{
					result.Add(decodedByte);

					sample.BitsCount = 0;
					sample.Bits = 0;
				}
			}
		}

		return result.ToArray();
	}

	private static Dictionary<BitsWithLength, byte> CreateDecodeTable(BitsWithLength[] encodeTable)
	{
		var result = new Dictionary<BitsWithLength, byte>();
		for (int b = 0; b < encodeTable.Length; b++)
		{
			var bitsWithLength = encodeTable[b];

			result[bitsWithLength] = (byte)b;
		}

		return result;
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
		var nodes = Enumerable.Range(0, byte.MaxValue + 1)
			.Select(num => new HuffmanNode { Frequency = frequences[num], LeafLabel = (byte)num })
			.Where(node => node.Frequency > 0);
		var queue = new PriorityQueue<HuffmanNode, int>();
		foreach (var node in nodes)
			queue.Enqueue(node, node.Frequency);
		return queue;
	}

	private static int[] CalcFrequences(List<byte> data)
	{
		var result = new int[byte.MaxValue + 1];
		for (var i = 0; i < data.Count; ++i)
			++result[data[i]];
		return result;
	}
}