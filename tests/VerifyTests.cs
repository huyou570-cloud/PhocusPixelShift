using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Web.Script.Serialization;

class VerifyTests {
    static void WriteNumber(BinaryWriter w, uint value, int bytes, bool big) {
        for(int i=0;i<bytes;i++) w.Write((byte)(value >> (8*(big ? bytes-1-i : i))));
    }
    static void Image(string path, bool big, int bits, bool truncated) {
        using(var w=new BinaryWriter(File.Create(path))) {
            w.Write(big ? (byte)'M' : (byte)'I'); w.Write(big ? (byte)'M' : (byte)'I');
            WriteNumber(w,42,2,big); WriteNumber(w,8,4,big); WriteNumber(w,6,2,big);
            uint[,] tags={{256,4,1,3},{257,4,1,2},{258,3,3,86},{273,4,1,92},{277,3,1,3},{279,4,1,36}};
            for(int i=0;i<6;i++) {
                WriteNumber(w,tags[i,0],2,big); WriteNumber(w,tags[i,1],2,big); WriteNumber(w,tags[i,2],4,big);
                if(tags[i,1]==3 && tags[i,2]==1) { WriteNumber(w,tags[i,3],2,big); WriteNumber(w,0,2,big); }
                else WriteNumber(w,tags[i,3],4,big);
            }
            WriteNumber(w,0,4,big);
            for(int i=0;i<3;i++) WriteNumber(w,(uint)bits,2,big);
            w.Write(new byte[truncated ? 12 : 36]);
        }
    }
    static void Expect(bool ok,string label) { if(!ok) throw new Exception(label); Console.WriteLine("PASS: "+label); }
    static bool Rejected(MethodInfo verify,string path) {
        try { verify.Invoke(null,new object[]{path}); return false; } catch(TargetInvocationException) { return true; }
    }
    static int Main(string[] args) {
        try {
            string root=Path.GetFullPath(args[0]);
            string temp=Path.Combine(root,"test-results",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            Assembly a=Assembly.LoadFrom(Path.Combine(root,"PixelShiftWorker.exe"));
            MethodInfo verify=a.GetType("Worker").GetMethod("Verify",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(bool big in new[]{false,true}) {
                string path=Path.Combine(temp,big?"big.tif":"little.tif"); Image(path,big,16,false);
                int[] dims=(int[])verify.Invoke(null,new object[]{path}); Expect(dims[0]==3&&dims[1]==2,"RGB16 "+(big?"big endian":"little endian"));
            }
            string bad=Path.Combine(temp,"bad.tif"); Image(bad,false,8,false); Expect(Rejected(verify,bad),"Reject RGB8");
            Image(bad,false,16,true); Expect(Rejected(verify,bad),"Reject truncated pixel strip");
            File.WriteAllText(bad,"invalid TIFF header"); Expect(Rejected(verify,bad),"Reject invalid header");
            foreach(string[] inputs in new[]{new[]{"1","2","3"},new[]{"1","2","3","1"},new[]{"1","2","3","4","5"}}) {
                string jobDir=Path.Combine(temp,"job-"+Guid.NewGuid().ToString("N"));
                string job=Path.Combine(temp,"job.json");
                File.WriteAllText(job,new JavaScriptSerializer().Serialize(new {Home=root,Inputs=inputs,Folder=jobDir}));
                var start=new ProcessStartInfo(Path.Combine(root,"PixelShiftWorker.exe"),"--run \""+job+"\"") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                using(var p=Process.Start(start)) {
                    string text=p.StandardOutput.ReadToEnd(); string error=p.StandardError.ReadToEnd(); p.WaitForExit();
                    Expect(p.ExitCode==1 && text.Contains("四张或六张不同") && !File.Exists(Path.Combine(jobDir,"验证记录.json")),"Reject invalid/duplicate input group: "+string.Join(",",inputs));
                    File.AppendAllText(Path.Combine(temp,"worker-rejections.log"),text+error);
                }
            }
            Console.WriteLine("Tests completed. Real RAW merge/export still requires original photographs."); return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
