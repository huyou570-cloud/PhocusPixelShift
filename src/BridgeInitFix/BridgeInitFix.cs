using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

public static class BridgeInitFix
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate bool SetInts(IntPtr owner, int space, int tag, IntPtr data, int count, int type);

	public static void SetFrameLayout(object engine, IntPtr header, int shots)
	{
		BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		IntPtr pointer = (IntPtr)engine.GetType().GetField("module", bindingAttr).GetValue(engine);
		SetInts setInts = (SetInts)Marshal.GetDelegateForFunctionPointer(IntPtr.Add(pointer, 5342816), typeof(SetInts));
		IntPtr intPtr = Marshal.AllocHGlobal(shots * 4);
		try
		{
			Marshal.WriteInt32(intPtr, shots);
			if (!setInts(header, 3, 33, intPtr, 1, 4) || !setInts(header, 1, 277, intPtr, 1, 3))
			{
				throw new Exception("Cannot set frame count metadata");
			}
			Marshal.WriteInt32(intPtr, 1);
			if (!setInts(header, 1, 284, intPtr, 1, 3))
			{
				throw new Exception("Cannot set planar layout");
			}
			Marshal.WriteInt32(intPtr, 2);
			if (!setInts(header, 1, 262, intPtr, 1, 3))
			{
				throw new Exception("Cannot set multishot photometric");
			}
			Marshal.WriteInt32(intPtr, 1);
			if (!setInts(header, 1, 259, intPtr, 1, 3))
			{
				throw new Exception("Cannot set uncompressed RAW");
			}
			for (int i = 0; i < shots; i++)
			{
				Marshal.WriteInt32(intPtr, i * 4, 16);
			}
			if (!setInts(header, 1, 258, intPtr, shots, 3))
			{
				throw new Exception("Cannot set frame bit depths");
			}
			IntPtr ptr = Marshal.ReadIntPtr(header, 32);
			IntPtr pointer2 = Marshal.ReadIntPtr(ptr, 16);
			int num = (ushort)Marshal.ReadInt16(ptr, 10);
			bool flag = false;
			for (int i = 0; i < num; i++)
			{
				IntPtr ptr2 = IntPtr.Add(pointer2, i * 32);
				if ((ushort)Marshal.ReadInt16(ptr2) == 33)
				{
					Marshal.WriteInt32(ptr2, 12, shots);
					Marshal.WriteIntPtr(ptr2, 16, IntPtr.Zero);
					Marshal.WriteByte(ptr2, 24, 0);
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				throw new Exception("Missing frame-count tag");
			}
		}
		finally
		{
			Marshal.FreeHGlobal(intPtr);
		}
	}

	public static void Initialize(object engine)
	{
		BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		Type type = engine.GetType();
		Type type2 = (Type)type.GetMethod("T", bindingAttr).Invoke(engine, new object[1] { "PhocusApi" });
		object obj = type.GetMethod("Call", bindingAttr).Invoke(engine, new object[3]
		{
			type2,
			"InitPhocusSuspended",
			new object[4]
			{
				IntPtr.Zero,
				Process.GetCurrentProcess().MainModule.BaseAddress,
				IntPtr.Zero,
				false
			}
		});
		if ((int)obj != 0)
		{
			throw new InvalidOperationException("Phocus import initialization failed: 0x" + ((int)obj).ToString("x8"));
		}
	}
}
