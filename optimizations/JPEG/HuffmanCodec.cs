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

public class BitsWithLength
{
	public int Bits { get; set; }
	public int BitsCount { get; set; }

	public class Comparer : IEqualityComparer<BitsWithLength>
	{
		public bool Equals(BitsWithLength x, BitsWithLength y)
		{
			if (x == y) return true;
			if (x == null || y == null)
				return false;
			return x.BitsCount == y.BitsCount && x.Bits == y.Bits;
		}

		public int GetHashCode(BitsWithLength obj)
		{
			if (obj == null)
				return 0;
			return ((397 * obj.Bits) << 5) ^ (17 * obj.BitsCount);
		}
	}
}

class BitsBuffer
{
	private byte[] buffer;
	private BitsWithLength unfinishedBits;
	private int i;

	public BitsBuffer(int bits)
	{
		buffer = new byte[bits];
		i = 0;
		unfinishedBits = new BitsWithLength();
	}

	public void Add(BitsWithLength bitsWithLength)
	{
		var totalBits = bitsWithLength.BitsCount + unfinishedBits.BitsCount;
		var combined = (unfinishedBits.Bits <<  bitsWithLength.BitsCount) | bitsWithLength.Bits;
		
		while (totalBits >= 8)
		{
			totalBits -= 8;
			buffer[i++] = (byte)(combined >> totalBits);
			combined &= (1 << totalBits) - 1;
		}

		unfinishedBits.BitsCount = totalBits;
		unfinishedBits.Bits = combined;
	}

	public byte[] ToArray(out long bitsCount)
	{
		bitsCount = i * 8L + unfinishedBits.BitsCount;
		var byteCount = (bitsCount + 7) / 8;
		var result = new byte[byteCount];
		Array.Copy(buffer, 0, result, 0, i);
		if (unfinishedBits.BitsCount > 0)
			result[i] = (byte)(unfinishedBits.Bits << (8 - unfinishedBits.BitsCount));
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

		var bitsBuffer = new BitsBuffer(data.Count());
		foreach (var b in data)
			bitsBuffer.Add(encodeTable[b]);

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
		var result = new Dictionary<BitsWithLength, byte>(new BitsWithLength.Comparer());
		for (int b = 0; b < encodeTable.Length; b++)
		{
			var bitsWithLength = encodeTable[b];
			if (bitsWithLength == null)
				continue;

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