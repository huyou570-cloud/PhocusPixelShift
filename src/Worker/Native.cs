using System;
using System.IO;
using System.Reflection;

internal class Native
{
	public object Engine;

	public Type T;

	public Assembly Api;

	private static BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	public string AppFolder;

	public object Main
	{
		get
		{
			return Invoke(Api.GetType("Phocus.Native.PhocusNative"), "get_gMainController");
		}
	}

	public Native(Job j, string stage)
	{
		Assembly assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native-fff-bridge.exe"));
		T = assembly.GetType("NativeEngine");
		Engine = Activator.CreateInstance(T, F, null, new object[3]
		{
			j.Home,
			Path.Combine(j.Folder, ".work", stage + "-state"),
			(Action<string>)delegate(string s)
			{
				Console.WriteLine(s);
			}
		}, null);
		Api = Assembly.LoadFrom(Path.Combine(j.Home, "Phocus.dll"));
	}

	public static object Invoke(object obj, string name, params object[] args)
	{
		Type type = (obj as Type) ?? obj.GetType();
		MethodInfo[] methods = type.GetMethods(F);
		foreach (MethodInfo methodInfo in methods)
		{
			if (!(methodInfo.Name != name) && methodInfo.GetParameters().Length == args.Length)
			{
				try
				{
					return methodInfo.Invoke((obj is Type) ? null : obj, args);
				}
				catch (ArgumentException)
				{
				}
			}
		}
		throw new MissingMethodException(type.FullName, name);
	}

	public object E(string name, params object[] args)
	{
		return Invoke(Engine, name, args);
	}

	public void Log(string stage)
	{
		E("SaveLog", Path.Combine(AppFolder, stage + ".log"));
	}
}
