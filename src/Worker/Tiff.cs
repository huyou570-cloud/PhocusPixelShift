using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal class Tiff : IDisposable
{
	public FileStream F;

	public bool Big;

	public uint Root;

	public Tiff(string p, bool write = false)
	{
		F = new FileStream(p, FileMode.Open, (!write) ? FileAccess.Read : FileAccess.ReadWrite, FileShare.Read);
		byte[] array = Read(0L, 8);
		Big = array[0] == 77;
		bool num;
		if (!Big)
		{
			if (array[0] != 73)
			{
				goto IL_006f;
			}
			num = array[1] == 73;
		}
		else
		{
			num = array[1] == 77;
		}
		if (num && U16(array, 2) == 42)
		{
			Root = U32(array, 4);
			return;
		}
		goto IL_006f;
		IL_006f:
		throw new Exception("不支持的 TIFF/RAW 文件结构");
	}

	public byte[] Read(long p, int n)
	{
		if (p < 0 || p + n > F.Length)
		{
			throw new Exception("文件数据超出边界");
		}
		F.Position = p;
		byte[] array = new byte[n];
		int i;
		int num;
		for (i = 0; i < n; i += num)
		{
			if ((num = F.Read(array, i, n - i)) <= 0)
			{
				break;
			}
		}
		if (i != n)
		{
			throw new EndOfStreamException();
		}
		return array;
	}

	public ushort U16(byte[] b, int p)
	{
		return (ushort)(Big ? (b[p] * 256 + b[p + 1]) : (b[p] + b[p + 1] * 256));
	}

	public uint U32(byte[] b, int p)
	{
		return (uint)(Big ? ((b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]) : (b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24)));
	}

	public byte[] W16(ushort v)
	{
		return Big ? new byte[2]
		{
			(byte)(v >> 8),
			(byte)v
		} : new byte[2]
		{
			(byte)v,
			(byte)(v >> 8)
		};
	}

	public byte[] W32(uint v)
	{
		return Big ? new byte[4]
		{
			(byte)(v >> 24),
			(byte)(v >> 16),
			(byte)(v >> 8),
			(byte)v
		} : new byte[4]
		{
			(byte)v,
			(byte)(v >> 8),
			(byte)(v >> 16),
			(byte)(v >> 24)
		};
	}

	public void Put(long p, byte[] b)
	{
		F.Position = p;
		F.Write(b, 0, b.Length);
	}

	public uint Append(byte[] b)
	{
		if (F.Length + b.Length > uint.MaxValue)
		{
			throw new Exception("FFF 超出 4GB 容量限制");
		}
		uint num = (uint)F.Length;
		Put(num, b);
		return num;
	}

	public Dictionary<int, Entry> Dir(uint p)
	{
		Dictionary<int, Entry> dictionary = new Dictionary<int, Entry>();
		int num = U16(Read(p, 2), 0);
		if (num > 4096)
		{
			throw new Exception("异常的 TIFF 目录");
		}
		byte[] source = Read(p + 2, num * 12);
		for (int i = 0; i < num; i++)
		{
			byte[] array = source.Skip(i * 12).Take(12).ToArray();
			Entry entry = new Entry();
			entry.Pos = p + 2 + i * 12;
			entry.Bytes = array;
			entry.Tag = U16(array, 0);
			entry.Type = U16(array, 2);
			entry.Count = U32(array, 4);
			entry.Value = U32(array, 8);
			Entry entry2 = entry;
			dictionary[entry2.Tag] = entry2;
		}
		return dictionary;
	}

	public static int Size(int t)
	{
		switch (t)
		{
		case 1:
		case 2:
		case 6:
		case 7:
			return 1;
		case 3:
		case 8:
			return 2;
		case 4:
		case 9:
		case 11:
			return 4;
		case 5:
		case 10:
		case 12:
			return 8;
		default:
			throw new Exception("不支持的 TIFF 数据类型");
		}
	}

	public byte[] Data(Entry e)
	{
		long num = Size(e.Type) * e.Count;
		if (num > 33554432)
		{
			throw new Exception("元数据过大");
		}
		return (num <= 4) ? e.Bytes.Skip(8).Take((int)num).ToArray() : Read(e.Value, (int)num);
	}

	public uint[] Numbers(Entry e)
	{
		byte[] b = Data(e);
		uint[] array = new uint[e.Count];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = ((e.Type == 3) ? U16(b, i * 2) : U32(b, i * 4));
		}
		return array;
	}

	public byte[] Make(int tag, int type, uint count, uint val)
	{
		return W16((ushort)tag).Concat(W16((ushort)type)).Concat(W32(count)).Concat(W32(val))
			.ToArray();
	}

	public void Set(Entry e, int type, uint count, uint val)
	{
		Put(e.Pos, Make(e.Tag, type, count, val));
	}

	public uint RawDir(Dictionary<int, Entry> top)
	{
		return Numbers(top[330])[0];
	}

	public void Dispose()
	{
		F.Dispose();
	}
}
