using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed class RawFile
{
	private sealed class Tag
	{
		public ushort Type;

		public uint Count;

		public long Value;

		public byte[] Inline;
	}

	private readonly string path;

	private readonly Dictionary<long, Dictionary<ushort, Tag>> directories = new Dictionary<long, Dictionary<ushort, Tag>>();

	public int Width;

	public int Height;

	public int SessionCount;

	public int SessionIndex = -1;

	public string Model = "";

	public string Make = "";

	public string ExposureKey = "";

	public long[] Offsets;

	public long[] Counts;

	public long Length;

	public RawFile(string file)
	{
		path = Path.GetFullPath(file);
		using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			using (BinaryReader binaryReader = new BinaryReader(fileStream))
			{
				Length = fileStream.Length;
				if (Length < 8 || binaryReader.ReadUInt16() != 18761 || binaryReader.ReadUInt16() != 42)
				{
					throw new InvalidDataException("只支持小端 TIFF 格式的未压缩 .3fr。");
				}
				long num = binaryReader.ReadUInt32();
				ReadDirectory(binaryReader, num, 0);
				Dictionary<ushort, Tag> dictionary = directories[num];
				Dictionary<ushort, Tag> dictionary2 = null;
				Dictionary<ushort, Tag> dictionary3 = null;
				foreach (Dictionary<ushort, Tag> value2 in directories.Values)
				{
					if (value2.ContainsKey(262) && Numbers(binaryReader, value2[262])[0] == 32803)
					{
						dictionary2 = value2;
					}
				}
				if (dictionary2 == null)
				{
					throw new InvalidDataException("找不到 Bayer RAW 图像目录。");
				}
				Make = Ascii(binaryReader, dictionary, 271);
				Model = Ascii(binaryReader, dictionary, 272);
				if (Make.IndexOf("Hasselblad", StringComparison.OrdinalIgnoreCase) < 0 || Model.IndexOf("X2D", StringComparison.OrdinalIgnoreCase) < 0)
				{
					throw new InvalidDataException("本程序仅接受 Hasselblad X2D 的 .3fr。");
				}
				Width = Scalar(binaryReader, dictionary2, 256);
				Height = Scalar(binaryReader, dictionary2, 257);
				if (Width < 8 || Height < 8 || Width > 20000 || Height > 20000)
				{
					throw new InvalidDataException("RAW 尺寸超出支持范围。");
				}
				if (Scalar(binaryReader, dictionary2, 258) != 16 || Scalar(binaryReader, dictionary2, 259) != 1 || Scalar(binaryReader, dictionary2, 277) != 1)
				{
					throw new InvalidDataException("仅支持未压缩、16 位、单通道 Bayer RAW；不支持压缩输入。");
				}
				if (!dictionary2.ContainsKey(273) || !dictionary2.ContainsKey(279))
				{
					throw new InvalidDataException("RAW 数据条带缺失。");
				}
				Offsets = Numbers(binaryReader, dictionary2[273]);
				Counts = Numbers(binaryReader, dictionary2[279]);
				if (Offsets.Length != Counts.Length || Offsets.Length == 0)
				{
					throw new InvalidDataException("RAW 条带数量不一致。");
				}
				long num2 = 0L;
				for (int i = 0; i < Offsets.Length; i++)
				{
					Check(Offsets[i], Counts[i]);
					num2 = checked(num2 + Counts[i]);
				}
				if (num2 != (long)Width * (long)Height * 2)
				{
					throw new InvalidDataException("RAW 条带长度与尺寸不一致。");
				}
				if (dictionary.ContainsKey(34665))
				{
					long key = Numbers(binaryReader, dictionary[34665])[0];
					if (directories.ContainsKey(key))
					{
						dictionary3 = directories[key];
					}
				}
				checked
				{
					if (dictionary3 != null)
					{
						StringBuilder stringBuilder = new StringBuilder();
						ushort[] array = new ushort[3] { 33434, 33437, 34855 };
						foreach (ushort num3 in array)
						{
							if (!dictionary3.ContainsKey(num3))
							{
								throw new InvalidDataException("曝光、光圈或 ISO 元数据缺失。");
							}
							stringBuilder.Append(num3).Append(':').Append(Convert.ToBase64String(Bytes(binaryReader, dictionary3[num3])))
								.Append(';');
						}
						ExposureKey = stringBuilder.ToString();
						if (!dictionary3.ContainsKey(37500))
						{
							return;
						}
						Tag tag = dictionary3[37500];
						long value = tag.Value;
						if (tag.Count <= 4 || tag.Type != 7)
						{
							return;
						}
						try
						{
							ReadDirectory(binaryReader, value, 0);
							Dictionary<ushort, Tag> dictionary4 = directories[value];
							if (dictionary4.ContainsKey(33))
							{
								long[] array2 = Numbers(binaryReader, dictionary4[33]);
								if (array2.Length >= 2)
								{
									SessionCount = (int)array2[0];
									SessionIndex = (int)array2[1];
								}
							}
							return;
						}
						catch (InvalidDataException)
						{
							return;
						}
					}
					throw new InvalidDataException("EXIF 图像信息缺失。");
				}
			}
		}
	}

	private void Check(long offset, long bytes)
	{
		if (offset < 0 || bytes < 0 || offset > Length || bytes > Length - offset)
		{
			throw new InvalidDataException("RAW 文件截断或偏移越界。");
		}
	}

	private void ReadDirectory(BinaryReader r, long offset, int depth)
	{
		if (directories.ContainsKey(offset))
		{
			return;
		}
		if (depth > 6 || directories.Count > 32)
		{
			throw new InvalidDataException("图像目录过深。");
		}
		Check(offset, 2L);
		r.BaseStream.Position = offset;
		int num = r.ReadUInt16();
		if (num > 1024)
		{
			throw new InvalidDataException("图像标签数量异常。");
		}
		Check(offset + 2, (long)num * 12L + 4);
		Dictionary<ushort, Tag> dictionary = new Dictionary<ushort, Tag>();
		directories.Add(offset, dictionary);
		for (int i = 0; i < num; i++)
		{
			r.BaseStream.Position = offset + 2 + i * 12;
			ushort key = r.ReadUInt16();
			ushort type = r.ReadUInt16();
			uint count = r.ReadUInt32();
			byte[] array = r.ReadBytes(4);
			if (dictionary.ContainsKey(key))
			{
				throw new InvalidDataException("图像目录存在重复标签。");
			}
			Tag tag = new Tag();
			tag.Type = type;
			tag.Count = count;
			tag.Inline = array;
			tag.Value = BitConverter.ToUInt32(array, 0);
			Tag value = tag;
			dictionary.Add(key, value);
		}
		ushort[] array2 = new ushort[2] { 330, 34665 };
		foreach (ushort key2 in array2)
		{
			if (dictionary.ContainsKey(key2))
			{
				long[] array3 = Numbers(r, dictionary[key2]);
				foreach (long offset2 in array3)
				{
					ReadDirectory(r, offset2, depth + 1);
				}
			}
		}
	}

	private int TypeSize(ushort t)
	{
		switch (t)
		{
		case 1:
		case 2:
		case 7:
			return 1;
		case 3:
			return 2;
		case 4:
		case 9:
			return 4;
		case 5:
		case 10:
			return 8;
		default:
			throw new InvalidDataException("不支持的 TIFF 标签类型。");
		}
	}

	private byte[] Bytes(BinaryReader r, Tag t)
	{
		long num = checked(t.Count * TypeSize(t.Type));
		if (num > 65536)
		{
			throw new InvalidDataException("元数据标签过大。");
		}
		if (num <= 4)
		{
			byte[] array = new byte[(int)num];
			Array.Copy(t.Inline, array, array.Length);
			return array;
		}
		Check(t.Value, num);
		r.BaseStream.Position = t.Value;
		byte[] array2 = r.ReadBytes((int)num);
		if (array2.Length != num)
		{
			throw new InvalidDataException("元数据截断。");
		}
		return array2;
	}

	private long[] Numbers(BinaryReader r, Tag t)
	{
		byte[] array = Bytes(r, t);
		long[] array2 = new long[t.Count];
		for (int i = 0; i < array2.Length; i++)
		{
			switch (t.Type)
			{
			case 1:
				array2[i] = array[i];
				break;
			case 3:
				array2[i] = BitConverter.ToUInt16(array, i * 2);
				break;
			case 4:
				array2[i] = BitConverter.ToUInt32(array, i * 4);
				break;
			case 9:
				array2[i] = BitConverter.ToInt32(array, i * 4);
				break;
			default:
				throw new InvalidDataException("无效的整数标签类型。");
			}
		}
		return array2;
	}

	private int Scalar(BinaryReader r, Dictionary<ushort, Tag> d, ushort id)
	{
		if (!d.ContainsKey(id))
		{
			throw new InvalidDataException("缺失 RAW 标签 " + id);
		}
		long[] array = Numbers(r, d[id]);
		if (array.Length != 1)
		{
			throw new InvalidDataException("非标量 RAW 标签 " + id);
		}
		return checked((int)array[0]);
	}

	private string Ascii(BinaryReader r, Dictionary<ushort, Tag> d, ushort id)
	{
		if (!d.ContainsKey(id))
		{
			return "";
		}
		string text = Encoding.ASCII.GetString(Bytes(r, d[id]));
		char[] trimChars = new char[1];
		return text.TrimEnd(trimChars);
	}

	public void ReadPixels(IntPtr target)
	{
		using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			byte[] array = new byte[1048576];
			long num = 0L;
			for (int i = 0; i < Offsets.Length; i++)
			{
				fileStream.Position = Offsets[i];
				long num2 = Counts[i];
				while (num2 > 0)
				{
					int num3 = fileStream.Read(array, 0, (int)Math.Min(num2, array.Length));
					if (num3 == 0)
					{
						throw new EndOfStreamException();
					}
					Marshal.Copy(array, 0, new IntPtr(target.ToInt64() + num), num3);
					num += num3;
					num2 -= num3;
				}
			}
		}
	}
}
