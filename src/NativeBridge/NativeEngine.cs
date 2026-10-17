using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

public sealed class NativeEngine
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr Constructor(IntPtr p, int mode, [MarshalAs(UnmanagedType.I1)] bool corrections);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void BadMap(IntPtr p, IntPtr calibration, IntPtr info);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool AddShot(IntPtr p, IntPtr info, IntPtr unused, IntPtr capture, int method, IntPtr context);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr Factory(IntPtr unused, IntPtr result, IntPtr spec);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool SetBuffer(IntPtr p, IntPtr pixels, int w, int h, int count, IntPtr hs, IntPtr columns, IntPtr blemish, IntPtr hot, IntPtr positions);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool SetInts(IntPtr header, int dir, int id, IntPtr values, int count, int type);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool Writer(IntPtr p, IntPtr preview, [MarshalAs(UnmanagedType.I1)] bool flag1, [MarshalAs(UnmanagedType.I1)] bool flag2, IntPtr flags, int mask);

	public const string SupportedHash = "70b697d839378c58de086cb82b81ecefd2b1d723ea7361bcdaef0c8a04a6211a";

	public const string SupportedManagedHash = "09c2e2b658b5cba6aeb18c1a1f93df39b3c2cf2d7069a6faabde5bf09624f25f";

	private Assembly assembly;

	private IntPtr module;

	private readonly List<object> keep = new List<object>();

	private readonly Action<string> log;

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetDllDirectory(string p);

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibrary(string p);

	public static string Hash(string p)
	{
		using (FileStream inputStream = File.OpenRead(p))
		{
			using (SHA256 sHA = SHA256.Create())
			{
				return BitConverter.ToString(sHA.ComputeHash(inputStream)).Replace("-", "").ToLowerInvariant();
			}
		}
	}

	private static IntPtr Off(IntPtr p, int n)
	{
		return new IntPtr(p.ToInt64() + n);
	}

	private static IntPtr Zero(int size)
	{
		IntPtr intPtr = Marshal.AllocHGlobal(size);
		Marshal.Copy(new byte[size], 0, intPtr, size);
		return intPtr;
	}

	private Delegate Fn(int rva, Type type)
	{
		return Marshal.GetDelegateForFunctionPointer(Off(module, rva), type);
	}

	private Type T(string name)
	{
		return assembly.GetType("Phocus.Native." + name, true);
	}

	private object New(string name, params object[] args)
	{
		object obj = Activator.CreateInstance(T(name), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
		keep.Add(obj);
		return obj;
	}

	private object Call(object o, string name, params object[] args)
	{
		Type type = ((o is Type) ? ((Type)o) : o.GetType());
		MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo in methods)
		{
			if (methodInfo.Name != name || methodInfo.GetParameters().Length != args.Length)
			{
				continue;
			}
			ParameterInfo[] parameters = methodInfo.GetParameters();
			bool flag = true;
			for (int j = 0; j < parameters.Length; j++)
			{
				Type type2 = parameters[j].ParameterType;
				if (type2.IsByRef)
				{
					type2 = type2.GetElementType();
				}
				if (args[j] != null && !type2.IsInstanceOfType(args[j]))
				{
					flag = false;
					break;
				}
			}
			if (!flag)
			{
				continue;
			}
			try
			{
				object obj = methodInfo.Invoke((o is Type) ? null : o, args);
				if (obj != null && !obj.GetType().IsValueType && !(obj is string))
				{
					keep.Add(obj);
				}
				return obj;
			}
			catch (TargetInvocationException ex)
			{
				throw new InvalidOperationException("Phocus " + name + " 调用失败。", ex.InnerException);
			}
		}
		throw new MissingMethodException(type.FullName, name);
	}

	private IntPtr Ptr(object obj)
	{
		if (obj == null)
		{
			return IntPtr.Zero;
		}
		MethodInfo method = obj.GetType().GetMethod("getCPtr", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		return ((HandleRef)method.Invoke(null, new object[1] { obj })).Handle;
	}

	private IntPtr Obj(object obj)
	{
		IntPtr intPtr = Ptr(obj);
		if (!(intPtr == IntPtr.Zero))
		{
			return Marshal.ReadIntPtr(intPtr);
		}
		return intPtr;
	}

	public NativeEngine(string home, string state, Action<string> logger)
	{
		log = logger;
		if (!Environment.Is64BitProcess)
		{
			throw new InvalidOperationException("需要 Windows x64 运行环境。");
		}
		home = Path.GetFullPath(Installation.Resolve(home));
		string text = Path.Combine(home, "PhocusApi64.dll");
		if (!File.Exists(text) || Hash(text) != "70b697d839378c58de086cb82b81ecefd2b1d723ea7361bcdaef0c8a04a6211a")
		{
			throw new InvalidOperationException("Phocus 引擎版本不匹配。仅支持已核对的 Windows Phocus 4.1.1，程序未加载其他版本。");
		}
		if (!File.Exists(Path.Combine(home, "Phocus.dll")) || Hash(Path.Combine(home, "Phocus.dll")) != "09c2e2b658b5cba6aeb18c1a1f93df39b3c2cf2d7069a6faabde5bf09624f25f")
		{
			throw new InvalidOperationException("Phocus 托管接口版本不匹配，已停止。");
		}
		AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
		{
			string text2 = Path.Combine(home, new AssemblyName(e.Name).Name + ".dll");
			return (!File.Exists(text2)) ? null : Assembly.LoadFrom(text2);
		};
		if (!SetDllDirectory(home))
		{
			throw new InvalidOperationException("无法设置 Phocus 运行目录。");
		}
		module = LoadLibrary(text);
		if (module == IntPtr.Zero)
		{
			throw new InvalidOperationException("无法加载 Phocus 引擎，错误 " + Marshal.GetLastWin32Error());
		}
		assembly = Assembly.LoadFrom(Path.Combine(home, "Phocus.dll"));
		Directory.CreateDirectory(state);
		Call(T("PhocusApi"), "CreateMainController");
		int num = (int)Call(T("PhocusApi"), "InitGlobals", state, "auto", "BridgePref.xml", "PhocusPixelShiftBridge");
		if (num != 0)
		{
			throw new InvalidOperationException("Phocus 初始化失败：" + num);
		}
		BridgeInitFix.Initialize(this);
		log("Phocus 4.1.1 引擎已加载；未连接或操作相机。");
	}

	private object OpenRaw(string path)
	{
		object obj = Call(T("CRawImageFile"), "FromFileSpec", New("CFileSpec", path));
		if (obj == null)
		{
			throw new InvalidDataException("Phocus 无法识别：" + Path.GetFileName(path));
		}
		if (Convert.ToInt32(Call(obj, "Open", true)) != 0 || !(bool)Call(obj, "Valid"))
		{
			throw new InvalidDataException("Phocus 无法只读打开：" + Path.GetFileName(path));
		}
		return obj;
	}

	public void SelfTest()
	{
		int[] array = new int[2] { 4, 6 };
		foreach (int num in array)
		{
			IntPtr intPtr = Zero(1536);
			IntPtr intPtr2 = Zero(1024);
			IntPtr intPtr3 = Zero(256);
			IntPtr intPtr4 = Marshal.AllocHGlobal(8192);
			Marshal.WriteInt32(intPtr2, 0, 64);
			Marshal.WriteInt32(intPtr2, 4, 64);
			Marshal.WriteIntPtr(intPtr2, 120, intPtr4);
			Marshal.WriteByte(intPtr3, 16, 1);
			((Constructor)Fn(1385344, typeof(Constructor)))(intPtr, (num == 4) ? 1 : 7, false);
			((BadMap)Fn(10464304, typeof(BadMap)))(intPtr, IntPtr.Zero, intPtr2);
			short[] array2 = new short[4096];
			for (int j = 0; j < num; j++)
			{
				for (int k = 0; k < array2.Length; k++)
				{
					array2[k] = (short)(1000 + 100 * j);
				}
				Marshal.Copy(array2, 0, intPtr4, array2.Length);
				if (!((AddShot)Fn(10459872, typeof(AddShot)))(intPtr, intPtr2, IntPtr.Zero, IntPtr.Zero, 2, intPtr3) || Marshal.ReadInt16(intPtr, 496) != j + 1)
				{
					throw new Exception("原生逐帧接口自检失败。");
				}
			}
			IntPtr intPtr5 = Marshal.ReadIntPtr(intPtr3, 96);
			if (intPtr5 == IntPtr.Zero || Marshal.ReadInt32(intPtr5, 20) != num)
			{
				throw new Exception("原生组帧结果缺失。");
			}
			IntPtr source = Marshal.ReadIntPtr(intPtr5, 48);
			short[] array3 = new short[64 * num];
			Marshal.Copy(source, array3, 0, array3.Length);
			for (int l = 0; l < 64; l++)
			{
				for (int m = 0; m < num; m++)
				{
					if (array3[l * num + m] != 1000 + 100 * m)
					{
						throw new Exception("原生组帧数据检查失败。");
					}
				}
			}
			Marshal.FreeHGlobal(intPtr4);
			log(num + " 张：原生初始化、逐帧调用、帧数及原始数据排列检查通过。");
		}
		log("自检不等于真实照片合成或显色验证。");
	}

	public void Merge(string[] paths, RawFile[] layouts, string partial, int count)
	{
		RawFile rawFile = layouts[0];
		object obj = OpenRaw(paths[0]);
		object obj2 = Call(obj, "GetRawIFDs");
		bool flag = obj2 == null;
		string path = Path.Combine(Path.GetDirectoryName(partial), "native-inputs-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		if (flag)
		{
			log("3FR 需要原生格式转换；仅对另存的副本调用 Phocus 导入接口。");
			obj = ImportCopy(paths[0], Path.Combine(path, "01"));
			obj2 = Call(obj, "GetRawIFDs");
		}
		if (obj2 == null)
		{
			throw new InvalidDataException("Phocus 未提供原生 RAW 元数据头。为避免生成缺少校准信息的无效 FFF，已停止，未生成结果。");
		}
		object o = Call(obj, "GetRawDescription");
		object o2 = Call(o, "get_rawCrop");
		int num = (int)Call(o, "GetRawWidth");
		int num2 = (int)Call(o, "GetRawHeight");
		if (num < 8 || num2 < 8 || num > 20000 || num2 > 20000)
		{
			throw new InvalidDataException("Phocus RAW 尺寸不受支持。");
		}
		long num3 = (long)num * (long)num2 * 2;
		int num4 = (int)Call(o2, "get_left");
		int num5 = (int)Call(o2, "get_top");
		int num6 = (int)Call(o2, "get_right");
		int num7 = (int)Call(o2, "get_bottom");
		if (num4 < 0 || num5 < 0 || num6 > num || num7 > num2 || num6 <= num4 || num7 <= num5)
		{
			throw new InvalidDataException("Phocus 返回的原始图像裁切信息不受支持。");
		}
		IntPtr intPtr = Zero(1536);
		IntPtr intPtr2 = Zero(1024);
		IntPtr intPtr3 = Zero(256);
		IntPtr intPtr4 = Marshal.AllocHGlobal(new IntPtr(num3));
		Marshal.WriteInt32(intPtr2, 0, num);
		Marshal.WriteInt32(intPtr2, 4, num2);
		Marshal.WriteInt32(intPtr2, 8, num4);
		Marshal.WriteInt32(intPtr2, 12, num - num6);
		Marshal.WriteInt32(intPtr2, 16, num5);
		Marshal.WriteInt32(intPtr2, 20, num2 - num7);
		Marshal.WriteIntPtr(intPtr2, 120, intPtr4);
		Marshal.WriteByte(intPtr3, 16, 1);
		((Constructor)Fn(1385344, typeof(Constructor)))(intPtr, (count == 4) ? 1 : 7, false);
		((BadMap)Fn(10464304, typeof(BadMap)))(intPtr, IntPtr.Zero, intPtr2);
		for (int i = 0; i < count; i++)
		{
			log("读取并送入 Phocus：" + (i + 1) + " / " + count + " — " + Path.GetFileName(paths[i]));
			object o3 = ((i == 0) ? obj : (flag ? ImportCopy(paths[i], Path.Combine(path, (i + 1).ToString("D2"))) : OpenRaw(paths[i])));
			object o4 = Call(o3, "GetRawDescription");
			if ((int)Call(o4, "GetRawWidth") != num || (int)Call(o4, "GetRawHeight") != num2 || Convert.ToInt32(Call(o3, "GetRawComponents")) != 1)
			{
				throw new InvalidDataException("Phocus 解码后的原片尺寸或 RAW 分量不一致。");
			}
			object[] array = new object[2]
			{
				intPtr4,
				checked((uint)num3)
			};
			uint num8 = (uint)Call(o3, "ReadRawData", array);
			if ((num8 & 0x80000000u) != 0 || (uint)array[1] != (uint)num3)
			{
				throw new InvalidDataException("Phocus RAW 读取失败或长度不符，状态 0x" + num8.ToString("x8"));
			}
			if (!((AddShot)Fn(10459872, typeof(AddShot)))(intPtr, intPtr2, IntPtr.Zero, IntPtr.Zero, 2, intPtr3) || Marshal.ReadInt16(intPtr, 496) != i + 1)
			{
				throw new InvalidOperationException("Phocus 拒绝第 " + (i + 1) + " 帧。");
			}
			if (i > 0)
			{
				Call(o3, "Close");
			}
		}
		Marshal.FreeHGlobal(intPtr4);
		IntPtr intPtr5 = Marshal.ReadIntPtr(intPtr3, 96);
		if (intPtr5 == IntPtr.Zero || Marshal.ReadInt32(intPtr5, 8) != num || Marshal.ReadInt32(intPtr5, 12) != num2 || Marshal.ReadInt32(intPtr5, 20) != count)
		{
			throw new InvalidOperationException("Phocus 组帧缓冲的尺寸或帧数错误。");
		}
		log("原生组帧完成，调用 Phocus FFF 写入接口。");
		object obj3 = New("CFileSpec", partial);
		IntPtr intPtr6 = Zero(16);
		((Factory)Fn(1914272, typeof(Factory)))(IntPtr.Zero, intPtr6, Obj(obj3));
		object obj4 = New("CRawImageFile", intPtr6, false);
		IntPtr intPtr7 = Obj(obj4);
		if (intPtr7 == IntPtr.Zero)
		{
			throw new InvalidOperationException("无法创建原生 FFF 文件对象。");
		}
		IntPtr ptr = Obj(obj);
		IntPtr intPtr8 = Ptr(obj2);
		if (Marshal.ReadIntPtr(ptr, 80) != intPtr8)
		{
			throw new InvalidOperationException("RAW 头部所有权布局与已核对版本不一致。");
		}
		Marshal.WriteIntPtr(ptr, 80, IntPtr.Zero);
		Call(obj4, "AddRawIFDs", obj2);
		IntPtr intPtr9 = Zero(4);
		Marshal.WriteInt32(intPtr9, count);
		if (!((SetInts)Fn(5342816, typeof(SetInts)))(intPtr8, 3, 33, intPtr9, 1, 9))
		{
			throw new InvalidOperationException("无法设置 MultiShot 帧数。");
		}
		BridgeInitFix.SetFrameLayout(this, intPtr8, count);
		IntPtr blemish = Zero(24);
		IntPtr positions = Zero(24);
		if (!((SetBuffer)Fn(7230112, typeof(SetBuffer)))(intPtr7, Marshal.ReadIntPtr(intPtr5, 48), num, num2, count, Off(intPtr3, 48), Off(intPtr3, 24), blemish, Off(intPtr3, 72), positions))
		{
			throw new InvalidOperationException("Phocus 拒绝 MultiShot RAW 缓冲。");
		}
		if (!((Writer)Fn(7228784, typeof(Writer)))(intPtr7, IntPtr.Zero, false, true, IntPtr.Zero, 10240))
		{
			throw new InvalidOperationException("Phocus 原生 FFF 写入失败。");
		}
		Call(obj4, "Close");
		ValidateResult(partial, count);
	}

	private object ImportCopy(string input, string directory)
	{
		string text = Path.Combine(directory, "source-copy");
		string text2 = Path.Combine(directory, "converted");
		Directory.CreateDirectory(text);
		Directory.CreateDirectory(text2);
		string text3 = Path.Combine(text, Path.GetFileName(input));
		File.Copy(input, text3, false);
		object o = Call(T("PhocusNative"), "get_gMainController");
		object obj = Call(o, "GetImportController");
		if (obj == null)
		{
			throw new InvalidOperationException("本机 Phocus 未创建离线导入控制器。");
		}
		Call(obj, "SetImportAs3FR", false);
		Call(obj, "SetThreadImport", false);
		int num = (int)Call(T("PhocusApi"), "Import", text2, new List<string> { text3 }, -1, true, 0);
		if (num < 0)
		{
			throw new InvalidOperationException("Phocus 对原片副本的 FFF 转换失败，状态 0x" + num.ToString("x8"));
		}
		string[] files = Directory.GetFiles(text2, "*.fff", SearchOption.TopDirectoryOnly);
		if (files.Length != 1)
		{
			throw new InvalidOperationException("Phocus 未同步写出唯一的 FFF 模板。保留导入副本与日志；未继续使用不完整数据。");
		}
		return OpenRaw(files[0]);
	}

	public void ValidateResult(string path, int count)
	{
		if (!File.Exists(path) || new FileInfo(path).Length == 0)
		{
			throw new InvalidDataException("没有写出非空 FFF。");
		}
		object o = OpenRaw(path);
		int num = Convert.ToInt32(Call(o, "GetShots"));
		int num2 = Convert.ToInt32(Call(o, "GetRawComponents"));
		object[] array = new object[2] { 0, 0 };
		Call(o, "GetFullDimensions", array);
		if (num != count || num2 != count || (int)array[0] <= 0 || (int)array[1] <= 0)
		{
			throw new InvalidDataException(string.Concat("FFF 回读验证失败：帧数 ", num, "，通道 ", num2, "，尺寸 ", array[0], "×", array[1], "。保留部分文件用于诊断。"));
		}
		log(string.Concat("Phocus 回读通过：", num, " 帧，", num2, " 个 RAW 分量，尺寸 ", array[0], " × ", array[1], "。"));
		log("这是 MultiShot FFF 中间文件；在 Phocus 打开并导出 TIFF 后才是完成显色的成片。");
		Call(o, "Close");
	}

	public void SaveLog(string path)
	{
		try
		{
			Call(T("DebugApi"), "DebugSaveLog", path);
		}
		catch
		{
		}
	}
}
