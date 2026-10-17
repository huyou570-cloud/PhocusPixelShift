using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

public static class BridgeWorker
{
	public static void WriteJson(string path, object data, Type t)
	{
		using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
		{
			new DataContractJsonSerializer(t).WriteObject((Stream)stream, data);
		}
	}

	public static BridgeJob ReadJob(string path)
	{
		using (FileStream stream = File.OpenRead(path))
		{
			return (BridgeJob)new DataContractJsonSerializer(typeof(BridgeJob)).ReadObject((Stream)stream);
		}
	}

	private static void Say(string text)
	{
		Console.WriteLine(text);
		Console.Out.Flush();
	}

	private static string ErrorText(Exception e)
	{
		StringBuilder stringBuilder = new StringBuilder();
		while (e != null)
		{
			stringBuilder.AppendLine(e.Message);
			e = e.InnerException;
		}
		return stringBuilder.ToString().Trim();
	}

	public static int Main(string[] args)
	{
		Console.OutputEncoding = new UTF8Encoding(false);
		try
		{
			if (args.Length > 0 && args[0] == "--self-test")
			{
				string home = ((args.Length > 1) ? args[1] : Installation.FindHome());
				string text = Installation.TemporaryDirectory("checks");
				Say("自检记录：" + text);
				NativeEngine nativeEngine = new NativeEngine(home, text, Say);
				nativeEngine.SelfTest();
				nativeEngine.SaveLog(Path.Combine(text, "phocus-native.log"));
				return 0;
			}
			BridgeJob job;
			if (args.Length == 2 && args[0] == "--job")
			{
				job = ReadJob(args[1]);
			}
			else
			{
				if (args.Length < 2 || (!(args[0] == "4") && !(args[0] == "6")))
				{
					Say("Phocus Pixel Shift Native Bridge — Windows 4.1.1 (experimental)");
					Say("自检: native-fff-bridge.exe --self-test [Phocus目录]");
					Say("组帧: native-fff-bridge.exe 4 输出.fff 01.3fr 02.3fr 03.3fr 04.3fr");
					Say("支持 4 / 6 张未压缩 X2D RAW。输出是 FFF 中间文件，需在 Phocus 打开并导出。");
					return (args.Length != 0) ? 2 : 0;
				}
				int num = int.Parse(args[0]);
				if (args.Length != num + 2)
				{
					throw new ArgumentException("参数中的帧数与输入文件数量不一致。");
				}
				string[] array = new string[num];
				Array.Copy(args, 2, array, 0, num);
				BridgeJob bridgeJob = new BridgeJob();
				bridgeJob.PhocusHome = Installation.FindHome();
				bridgeJob.Inputs = array;
				bridgeJob.Output = args[1];
				bridgeJob.OrderConfirmed = true;
				job = bridgeJob;
			}
			return Execute(job);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine(ErrorText(e));
			return 1;
		}
	}

	private static int Execute(BridgeJob job)
	{
		if (job == null || job.Inputs == null || (job.Inputs.Length != 4 && job.Inputs.Length != 6))
		{
			throw new ArgumentException("请选择恰好 4 张或 6 张 .3fr。");
		}
		if (!job.OrderConfirmed)
		{
			throw new ArgumentException("需要确认文件按实际拍摄顺序排列。");
		}
		string fullPath = Path.GetFullPath(job.Output);
		if (!string.Equals(Path.GetExtension(fullPath), ".fff", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("输出扩展名必须是 .fff。");
		}
		string directoryName = Path.GetDirectoryName(fullPath);
		if (!Directory.Exists(directoryName))
		{
			throw new DirectoryNotFoundException("输出文件夹不存在。");
		}
		if (File.Exists(fullPath))
		{
			throw new IOException("输出已存在，程序不会覆盖它。");
		}
		string text = Path.Combine(directoryName, Path.GetFileNameWithoutExtension(fullPath) + ".partial.fff");
		if (File.Exists(text))
		{
			throw new IOException("上次的部分结果仍在：" + text + "。请更换输出文件名。");
		}
		int num = job.Inputs.Length;
		string[] array = new string[num];
		RawFile[] array2 = new RawFile[num];
		List<FileStream> list = new List<FileStream>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		RunRecord runRecord = new RunRecord();
		runRecord.Output = fullPath;
		runRecord.Inputs = new InputRecord[num];
		RunRecord runRecord2 = runRecord;
		string text2 = Path.Combine(directoryName, "phocus-run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
		Directory.CreateDirectory(text2);
		string text3 = Path.Combine(text2, "run.json");
		NativeEngine nativeEngine = null;
		try
		{
			for (int i = 0; i < num; i++)
			{
				array[i] = Path.GetFullPath(job.Inputs[i]);
				if (!hashSet.Add(array[i]))
				{
					throw new ArgumentException("不能重复选择同一个文件。");
				}
				if (!string.Equals(Path.GetExtension(array[i]), ".3fr", StringComparison.OrdinalIgnoreCase))
				{
					throw new ArgumentException("输入必须是 .3fr。");
				}
				if (string.Equals(array[i], fullPath, StringComparison.OrdinalIgnoreCase) || string.Equals(array[i], text, StringComparison.OrdinalIgnoreCase))
				{
					throw new ArgumentException("输出不能覆盖原片。");
				}
				list.Add(new FileStream(array[i], FileMode.Open, FileAccess.Read, FileShare.Read));
				Say("检查原片 " + (i + 1) + " / " + num);
				array2[i] = new RawFile(array[i]);
				RawFile rawFile = array2[i];
				if (i > 0 && (rawFile.Width != array2[0].Width || rawFile.Height != array2[0].Height || rawFile.Model != array2[0].Model || rawFile.ExposureKey != array2[0].ExposureKey))
				{
					throw new InvalidDataException("原片尺寸、机型或曝光参数不一致。");
				}
				if (rawFile.SessionCount != 0 && (rawFile.SessionCount != num || rawFile.SessionIndex != i))
				{
					throw new InvalidDataException("第 " + (i + 1) + " 张的序列元数据与排列不符。请按实际拍摄顺序重新排列。");
				}
				runRecord2.Inputs[i] = new InputRecord
				{
					Path = array[i],
					SHA256 = NativeEngine.Hash(array[i]),
					Bytes = rawFile.Length,
					Width = rawFile.Width,
					Height = rawFile.Height,
					SessionCount = rawFile.SessionCount,
					SessionIndex = rawFile.SessionIndex
				};
			}
			long num2 = (long)array2[0].Width * (long)array2[0].Height * num * 2;
			if (num2 > 4160749567u)
			{
				throw new InvalidOperationException("此组输入超过当前 FFF 写出接口的大小范围。");
			}
			Say("预计多帧 RAW 数据约 " + num2 / 1048576 + " MB；处理期间请保留足够内存和磁盘空间。");
			runRecord2.Status = "processing";
			WriteJson(text3, runRecord2, typeof(RunRecord));
			nativeEngine = new NativeEngine(job.PhocusHome, Path.Combine(text2, "state"), Say);
			nativeEngine.Merge(array, array2, text, num);
			for (int j = 0; j < num; j++)
			{
				if (new FileInfo(array[j]).Length != runRecord2.Inputs[j].Bytes || NativeEngine.Hash(array[j]) != runRecord2.Inputs[j].SHA256)
				{
					throw new IOException("处理期间原片发生变化，未将部分结果标为成功。");
				}
			}
			runRecord2.OriginalsVerifiedUnchanged = true;
			File.Move(text, fullPath);
			runRecord2.OutputSHA256 = NativeEngine.Hash(fullPath);
			runRecord2.Status = "fff-ready";
			Say("已生成并通过 Phocus 回读：" + fullPath);
			Say("下一步：在 Phocus 打开这个 FFF，检查图像，然后导出 TIFF。");
			return 0;
		}
		catch (Exception e)
		{
			runRecord2.Status = "failed";
			runRecord2.Error = ErrorText(e);
			Console.Error.WriteLine(runRecord2.Error);
			Say("失败记录：" + text3);
			Say("原片不删除；部分输出不会被标为成功，也不会自动清理。");
			return 1;
		}
		finally
		{
			if (nativeEngine != null)
			{
				nativeEngine.SaveLog(Path.Combine(text2, "phocus-native.log"));
			}
			runRecord2.EndedUtc = DateTime.UtcNow.ToString("o");
			WriteJson(text3, runRecord2, typeof(RunRecord));
			foreach (FileStream item in list)
			{
				item.Dispose();
			}
		}
	}
}
