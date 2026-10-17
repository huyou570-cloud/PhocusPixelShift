using System;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

internal static class Launcher {
    const string Gui = "Phocus像素位移合并-v0.4.exe";
    static readonly string[] Files = { Gui, Gui + ".config", "PixelShiftWorker.exe", "PixelShiftWorker.exe.config", "native-fff-bridge.exe", "native-fff-bridge.exe.config", "BridgeInitFix.dll" };
    static string Hash(string path) { using (var s = File.OpenRead(path)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
    static void CheckPhocus(string home) {
        string[] names = { "PhocusApi64.dll", "Phocus.dll" };
        string[] hashes = { "70b697d839378c58de086cb82b81ecefd2b1d723ea7361bcdaef0c8a04a6211a", "09c2e2b658b5cba6aeb18c1a1f93df39b3c2cf2d7069a6faabde5bf09624f25f" };
        for (int i=0;i<names.Length;i++) {
            string p=Path.Combine(home,names[i]);
            if (!File.Exists(p)) throw new Exception("未找到 Phocus 4.1.1 文件：\r\n"+p);
            if (Hash(p)!=hashes[i]) throw new Exception("Phocus 构建版本不匹配："+names[i]+"。\r\n请使用之前验证的 Phocus 4.1.1 安装版本。");
        }
    }
    [STAThread] static int Main(string[] args) {
        bool check = args.Length > 0 && args[0] == "--check";
        string root=AppDomain.CurrentDomain.BaseDirectory;
        try {
            if (!Environment.Is64BitOperatingSystem) throw new Exception("本软件需要 64 位 Windows。");
            foreach (string file in Files) if (!File.Exists(Path.Combine(root,file))) throw new Exception("软件包缺少文件："+file+"\r\n请完整解压压缩包，不要只复制启动程序。");
            string home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Hasselblad", "Phocus 4.1.1");
            CheckPhocus(home);
            if (check) {
                File.WriteAllText(Path.Combine(root,"启动检查.txt"),"启动检查通过\r\n时间："+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"\r\n运行文件齐全，Phocus 两个核心文件的 SHA-256 与验证版本一致。\r\n这项检查不代表本次重新执行了原片合并。\r\n",new UTF8Encoding(true));
                return 0;
            }
            Process.Start(new ProcessStartInfo { FileName=Path.Combine(root,Gui), WorkingDirectory=root, UseShellExecute=true });
            return 0;
        } catch (Exception e) {
            if (check) { File.WriteAllText(Path.Combine(root,"启动检查.txt"),"启动检查失败："+e.Message,new UTF8Encoding(true)); }
            else MessageBox.Show(e.Message,"像素位移合并 · 启动检查",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}
