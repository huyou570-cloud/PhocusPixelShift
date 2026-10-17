using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

public static class Installation
{
	private static void Add(List<string> paths, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}
		try
		{
			value = Path.GetFullPath(value.Trim().Trim('"'));
			if (!paths.Contains(value))
			{
				paths.Add(value);
			}
		}
		catch
		{
		}
	}

	public static string FindHome()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("PHOCUS_HOME");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			return environmentVariable;
		}
		List<string> list = new List<string>();
		string[] array = new string[2]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
		};
		foreach (string path in array)
		{
			try
			{
				string path2 = Path.Combine(path, "Hasselblad");
				if (Directory.Exists(path2))
				{
					string[] directories = Directory.GetDirectories(path2, "Phocus*");
					foreach (string value in directories)
					{
						Add(list, value);
					}
				}
			}
			catch
			{
			}
		}
		RegistryHive[] array2 = new RegistryHive[2]
		{
			RegistryHive.LocalMachine,
			RegistryHive.CurrentUser
		};
		foreach (RegistryHive hKey in array2)
		{
			RegistryView[] array3 = new RegistryView[2]
			{
				RegistryView.Registry64,
				RegistryView.Registry32
			};
			foreach (RegistryView view in array3)
			{
				try
				{
					using (RegistryKey registryKey = RegistryKey.OpenBaseKey(hKey, view))
					{
						using (RegistryKey registryKey2 = registryKey.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall"))
						{
							if (registryKey2 == null)
							{
								continue;
							}
							string[] subKeyNames = registryKey2.GetSubKeyNames();
							foreach (string name in subKeyNames)
							{
								using (RegistryKey registryKey3 = registryKey2.OpenSubKey(name))
								{
									if (registryKey3 == null)
									{
										continue;
									}
									string text = registryKey3.GetValue("DisplayName", "") as string;
									if (text == null || text.IndexOf("Phocus", StringComparison.OrdinalIgnoreCase) < 0)
									{
										continue;
									}
									Add(list, registryKey3.GetValue("InstallLocation", "") as string);
									string text2 = registryKey3.GetValue("DisplayIcon", "") as string;
									if (string.IsNullOrWhiteSpace(text2))
									{
										continue;
									}
									try
									{
										int num = text2.LastIndexOf(',');
										if (num >= 0)
										{
											text2 = text2.Substring(0, num);
										}
										Add(list, Path.GetDirectoryName(text2.Trim().Trim('"')));
									}
									catch
									{
									}
								}
							}
						}
					}
				}
				catch
				{
				}
			}
		}
		foreach (string item in list)
		{
			try
			{
				string text3 = Path.Combine(item, "PhocusApi64.dll");
				string text4 = Path.Combine(item, "Phocus.dll");
				if (File.Exists(text3) && File.Exists(text4) && NativeEngine.Hash(text3) == "70b697d839378c58de086cb82b81ecefd2b1d723ea7361bcdaef0c8a04a6211a" && NativeEngine.Hash(text4) == "09c2e2b658b5cba6aeb18c1a1f93df39b3c2cf2d7069a6faabde5bf09624f25f")
				{
					return item;
				}
			}
			catch
			{
			}
		}
		return "";
	}

	public static string Resolve(string home)
	{
		if (string.IsNullOrWhiteSpace(home))
		{
			home = FindHome();
		}
		if (string.IsNullOrWhiteSpace(home))
		{
			throw new InvalidOperationException("未找到匹配的 Phocus 4.1.1。请安装后在工具中浏览选择安装文件夹。");
		}
		return home;
	}

	public static string TemporaryDirectory(string category)
	{
		string text = Path.Combine(Path.GetTempPath(), "PhocusPixelShift", category, Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		return text;
	}
}
