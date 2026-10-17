using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal class Worker
{
	private static JavaScriptSerializer Json = new JavaScriptSerializer();

	private static Job job;

	private static int Shots
	{
		get
		{
			return job.Inputs.Length;
		}
	}

	private static string Self
	{
		get
		{
			return Assembly.GetExecutingAssembly().Location;
		}
	}

	private static string Temp
	{
		get
		{
			return Path.Combine(job.Folder, ".work");
		}
	}

	private static void Say(string s)
	{
		Console.WriteLine(s);
		Console.Out.Flush();
	}

	private static string Hash(string p)
	{
		using (FileStream inputStream = File.OpenRead(p))
		{
			using (SHA256 sHA = SHA256.Create())
			{
				return BitConverter.ToString(sHA.ComputeHash(inputStream)).Replace("-", "").ToLowerInvariant();
			}
		}
	}

	private static string Q(string s)
	{
		return "\"" + s.Replace("\"", "\\\"") + "\"";
	}

	private static void Child(string exe, string args, string log, bool raw)
	{
		if (File.Exists(Path.Combine(Temp, "cancel.request")))
		{
			throw new OperationCanceledException("已停止，原片未改动");
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo(exe, args);
		processStartInfo.UseShellExecute = false;
		processStartInfo.CreateNoWindow = true;
		processStartInfo.RedirectStandardOutput = true;
		processStartInfo.RedirectStandardError = true;
		processStartInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
		ProcessStartInfo processStartInfo2 = processStartInfo;
		processStartInfo2.EnvironmentVariables["PHOCUS_HOME"] = job.Home;
		StreamWriter w = new StreamWriter(log, false, Encoding.UTF8);
		try
		{
			using (Process process = new Process())
			{
				process.StartInfo = processStartInfo2;
				object gate = new object();
				DataReceivedEventHandler value = delegate(object o, DataReceivedEventArgs e)
				{
					if (e.Data != null)
					{
						lock (gate)
						{
							w.WriteLine(e.Data);
							w.Flush();
						}
						if ((raw && (e.Data.StartsWith("检查原片") || e.Data.StartsWith("读取并送入"))) || (!raw && (e.Data.StartsWith("显色") || e.Data.StartsWith("Phocus 已完成") || e.Data.StartsWith("失败"))))
						{
							Say(e.Data);
						}
					}
				};
				process.OutputDataReceived += value;
				process.ErrorDataReceived += value;
				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				while (!process.WaitForExit(500))
				{
					if (File.Exists(Path.Combine(Temp, "cancel.request")))
					{
						process.Kill();
						process.WaitForExit();
						throw new OperationCanceledException("已停止，原片未改动");
					}
				}
				process.WaitForExit();
				if (process.ExitCode != 0)
				{
					throw new Exception("处理步骤失败，详见 " + Path.GetFileName(log));
				}
			}
		}
		finally
		{
			if (w != null)
			{
				((IDisposable)w).Dispose();
			}
		}
	}

	private static void Template()
	{
		Native native = new Native(job, "template");
		native.AppFolder = Temp;
		Native native2 = native;
		try
		{
			native2.E("ImportCopy", job.Inputs[0], Path.Combine(Temp, "template"));
		}
		finally
		{
			native2.Log("template");
		}
	}

	private static void Assemble()
	{
		string sourceFileName = Directory.GetFiles(Path.Combine(Temp, "template", "converted"), "*.fff").Single();
		string p = Path.Combine(Temp, "group.fff");
		string text = Path.Combine(Temp, "merged.fff");
		File.Copy(sourceFileName, text, false);
		Tiff f = new Tiff(text, true);
		try
		{
			using (Tiff tiff = new Tiff(p))
			{
				if (!f.Big || !tiff.Big)
				{
					throw new Exception("引擎输出字节序与已验证布局不同");
				}
				Dictionary<int, Entry> dictionary = f.Dir(f.Root);
				Dictionary<int, Entry> dictionary2 = f.Dir(f.RawDir(dictionary));
				Dictionary<int, Entry> dictionary3 = tiff.Dir(tiff.Root);
				Dictionary<int, Entry> dictionary4 = tiff.Dir(tiff.RawDir(dictionary3));
				uint num = tiff.Numbers(dictionary4[273]).Single();
				uint num2 = tiff.Numbers(dictionary4[279]).Single();
				if (tiff.Numbers(dictionary4[277])[0] != Shots || num2 != (long)tiff.Numbers(dictionary4[256])[0] * (long)tiff.Numbers(dictionary4[257])[0] * 2 * Shots)
				{
					throw new Exception("多帧数据量验证失败");
				}
				uint num3 = (uint)f.F.Length;
				f.F.Position = num3;
				tiff.F.Position = num;
				byte[] array = new byte[8388608];
				long num4 = num2;
				while (num4 > 0)
				{
					int num5 = tiff.F.Read(array, 0, (int)Math.Min(array.Length, num4));
					if (num5 <= 0)
					{
						throw new EndOfStreamException();
					}
					f.F.Write(array, 0, num5);
					num4 -= num5;
				}
				uint val = f.Append(Enumerable.Range(0, Shots).SelectMany((int x) => f.W16(16)).ToArray());
				f.Set(dictionary2[258], 3, (uint)Shots, val);
				f.Set(dictionary2[259], 3, 1u, 65536u);
				f.Set(dictionary2[277], 3, 1u, (uint)(Shots << 16));
				f.Set(dictionary2[284], 3, 1u, 65536u);
				f.Set(dictionary2[273], 4, 1u, num3);
				f.Set(dictionary2[279], 4, 1u, num2);
				int[] array2 = new int[2] { 50719, 50720 };
				foreach (int key in array2)
				{
					f.Put(dictionary2[key].Pos, dictionary4[key].Bytes);
				}
				Dictionary<int, Entry> dictionary5 = f.Dir(f.Numbers(dictionary[34665])[0]);
				Dictionary<int, Entry> source = f.Dir(dictionary5[37500].Value);
				Dictionary<int, Entry> dictionary6 = tiff.Dir(tiff.Numbers(dictionary3[34665])[0]);
				Dictionary<int, Entry> dictionary7 = tiff.Dir(dictionary6[37500].Value);
				Dictionary<int, byte[]> dictionary8 = source.ToDictionary((KeyValuePair<int, Entry> x) => x.Key, (KeyValuePair<int, Entry> x) => x.Value.Bytes);
				array2 = new int[4] { 33, 59, 66, 69 };
				foreach (int key in array2)
				{
					Entry entry = dictionary7[key];
					if (key == 33)
					{
						dictionary8[key] = f.Make(key, 4, 1u, (uint)Shots);
					}
					else if (entry.Count * Tiff.Size(entry.Type) > 4)
					{
						uint val2 = f.Append(tiff.Data(entry));
						dictionary8[key] = f.Make(key, entry.Type, entry.Count, val2);
					}
					else
					{
						dictionary8[key] = entry.Bytes;
					}
				}
				byte[] array3 = f.W16((ushort)dictionary8.Count).Concat(dictionary8.OrderBy((KeyValuePair<int, byte[]> x) => x.Key).SelectMany((KeyValuePair<int, byte[]> x) => x.Value)).Concat(new byte[4])
					.ToArray();
				uint val3 = f.Append(array3);
				f.Set(dictionary5[37500], 7, (uint)array3.Length, val3);
				f.F.Flush(true);
			}
		}
		finally
		{
			if (f != null)
			{
				((IDisposable)f).Dispose();
			}
		}
	}

	private static void Export()
	{
		Native native = new Native(job, "export");
		native.AppFolder = Temp;
		Native native2 = native;
		try
		{
			object main = native2.Main;
			object obj = Native.Invoke(main, "GetPreferences");
			Native.Invoke(obj, "SetUseOpenCL", false);
			object obj2 = Native.Invoke(main, "GetExportController");
			object obj3 = Native.Invoke(obj2, "GetCurrentOutputPreset");
			Native.Invoke(obj3, "SetOutputFormat", Enum.Parse(native2.Api.GetType("Phocus.Native.eOutputFormat"), "kOutputFormatTIFF16"));
			Native.Invoke(obj3, "SetSizeRestriction", Enum.Parse(native2.Api.GetType("Phocus.Native.eSizeRestriction"), "kSizeRestrictionNone"));
			string text = Path.Combine(Temp, "export");
			Directory.CreateDirectory(text);
			int num = (int)Native.Invoke(native2.Api.GetType("Phocus.Native.PhocusApi"), "ExportFiles", text, new string[1] { Path.Combine(Temp, "merged.fff") }, 1, true);
			if (num < 0)
			{
				throw new Exception("Phocus 无法开始导出：0x" + num.ToString("x8"));
			}
			object obj4 = Native.Invoke(main, "GetTaskManager");
			bool flag = false;
			bool flag2 = false;
			for (int i = 0; i < 2400; i++)
			{
				Thread.Sleep(500);
				bool flag3 = false;
				int num2 = (int)Native.Invoke(obj4, "NumberOfTasks");
				for (int j = 0; j < num2; j++)
				{
					object obj5 = Native.Invoke(obj4, "GetTask", j);
					if (obj5 == null)
					{
						continue;
					}
					Native.Invoke(obj5, "UpdateStatus");
					if (!(Native.Invoke(obj5, "GetTaskType").ToString() != "kTaskSaveFrom3f"))
					{
						flag = true;
						string text2 = Native.Invoke(obj5, "GetState").ToString();
						int num3 = (int)Native.Invoke(obj5, "GetError");
						if (text2 == "kTaskErrorOccured" || num3 < 0)
						{
							throw new Exception("Phocus 导出失败：0x" + num3.ToString("x8"));
						}
						if (text2 == "kTaskFinishedOK")
						{
							flag2 = true;
						}
						else
						{
							flag3 = true;
						}
						if (i % 10 == 0)
						{
							Say("显色导出进度 " + Math.Round(Convert.ToDouble(Native.Invoke(obj5, "GetCurrentProgress")) * 100.0) + "%");
						}
					}
				}
				if (flag && flag2 && !flag3)
				{
					Say("Phocus 已完成实际 TIFF 导出。");
					break;
				}
				if (i == 2399)
				{
					throw new Exception("Phocus 导出超时，未标记成功");
				}
			}
			if (!flag || !flag2)
			{
				throw new Exception("未收到 Phocus 导出完成状态");
			}
		}
		finally
		{
			native2.Log("export");
		}
	}

	private static int[] Verify(string p)
	{
		Tiff t = new Tiff(p);
		try
		{
			Dictionary<int, Entry> dictionary = t.Dir(t.Root);
			int num = (int)t.Numbers(dictionary[256])[0];
			int num2 = (int)t.Numbers(dictionary[257])[0];
			if (t.Numbers(dictionary[277])[0] != 3 || t.Numbers(dictionary[258]).Any((uint x) => x != 16))
			{
				throw new Exception("输出不是 16 位 RGB TIFF");
			}
			uint[] array = t.Numbers(dictionary[273]);
			uint[] lens = t.Numbers(dictionary[279]);
			if (array.Length != lens.Length || array.Where((uint x, int i) => (long)x + (long)lens[i] > t.F.Length).Any())
			{
				throw new Exception("TIFF 像素数据不完整");
			}
			return new int[2] { num, num2 };
		}
		finally
		{
			if (t != null)
			{
				((IDisposable)t).Dispose();
			}
		}
	}

	private static void Run(string json)
	{
		Directory.CreateDirectory(Temp);
		Say("检查原片与引擎……");
		if (job.Inputs == null || (job.Inputs.Length != 4 && job.Inputs.Length != 6) || job.Inputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != job.Inputs.Length)
		{
			throw new Exception("请选择同一组的四张或六张不同 3FR 原片");
		}
		string[] hashes = job.Inputs.Select(Hash).ToArray();
		Say("1/4 生成原生 FFF 模板……");
		Child(Self, "--template " + Q(json), Path.Combine(Temp, "template-console.log"), true);
		Say("2/4 调用 Phocus 多帧接口……");
		string args = Shots + " " + Q(Path.Combine(Temp, "group.fff")) + " " + string.Join(" ", job.Inputs.Select(Q));
		Child(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native-fff-bridge.exe"), args, Path.Combine(Temp, "group-console.log"), true);
		Say("3/4 整理多帧 RAW 与校准数据……");
		Assemble();
		Say("4/4 使用 Phocus 导出 16 位 TIFF……");
		Child(Self, "--export " + Q(json), Path.Combine(Temp, "export-console.log"), false);
		string text = Directory.GetFiles(Path.Combine(Temp, "export"), "*.tif").Single();
		int[] array = Verify(text);
		using (Tiff tiff = new Tiff(Path.Combine(Temp, "merged.fff")))
		{
			Dictionary<int, Entry> dictionary = tiff.Dir(tiff.RawDir(tiff.Dir(tiff.Root)));
			uint[] array2 = tiff.Numbers(dictionary[50720]);
			if (array[0] != array2[0] || array[1] != array2[1])
			{
				throw new Exception("TIFF 尺寸与多帧输出尺寸不一致");
			}
		}
		if (job.Inputs.Select(Hash).Where((string s, int i) => s != hashes[i]).Any())
		{
			throw new Exception("原片校验不一致");
		}
		string text2 = Path.Combine(job.Folder, "合并结果-16位.tif");
		string text3 = Path.Combine(job.Folder, "合并结果-" + Shots + "帧.fff");
		File.Move(text, text2);
		File.Move(Path.Combine(Temp, "merged.fff"), text3);
		var obj = new
		{
			Status = "complete",
			Shots = Shots,
			Inputs = job.Inputs,
			SHA256 = hashes,
			OriginalsVerifiedUnchanged = true,
			Width = array[0],
			Height = array[1],
			BitsPerChannel = 16,
			Channels = 3,
			Output = text2,
			OutputSHA256 = Hash(text2),
			FFF = text3,
			Completed = DateTime.Now.ToString("o"),
			Validation = "Phocus export task finished OK; TIFF RGB16 dimensions and pixel bounds verified; original SHA256 unchanged. Optical resolution gain not quantified."
		};
		File.WriteAllText(Path.Combine(job.Folder, "验证记录.json"), Json.Serialize(obj), Encoding.UTF8);
		Say("完成：" + array[0] + " × " + array[1] + "，16 位 RGB TIFF。原片校验一致。");
	}

	private static int Main(string[] args)
	{
		try
		{
			Console.OutputEncoding = new UTF8Encoding(false);
			if (args.Length != 2)
			{
				throw new Exception("请使用图形界面启动");
			}
			job = Json.Deserialize<Job>(File.ReadAllText(args[1]));
			job.Home = Path.GetFullPath(job.Home);
			job.Folder = Path.GetFullPath(job.Folder);
			if (args[0] == "--template")
			{
				Template();
			}
			else if (args[0] == "--export")
			{
				Export();
			}
			else
			{
				if (!(args[0] == "--run"))
				{
					throw new Exception("未知处理模式");
				}
				Run(Path.GetFullPath(args[1]));
			}
			return 0;
		}
		catch (Exception innerException)
		{
			while (innerException is TargetInvocationException && innerException.InnerException != null)
			{
				innerException = innerException.InnerException;
			}
			Say("失败：" + innerException.Message);
			return 1;
		}
	}
}
