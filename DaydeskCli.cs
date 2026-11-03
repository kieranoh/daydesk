using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

static class DaydeskCli {
    static string Quote(string value) {
        var output=new StringBuilder("\""); int slashes=0;
        foreach(char c in value) {
            if(c=='\\') { slashes++; continue; }
            output.Append('\\',c=='\"' ? slashes*2+1 : slashes); output.Append(c); slashes=0;
        }
        output.Append('\\',slashes*2); output.Append('"'); return output.ToString();
    }
    static int Main(string[] args) {
        Console.OutputEncoding=new UTF8Encoding(false);
        string root=AppDomain.CurrentDomain.BaseDirectory;
        string python=Path.Combine(root,"runtime","python","python.exe");
        if(!File.Exists(python)) { Console.Error.WriteLine("Bundled Python runtime was not found. Reinstall Daydesk."); return 1; }
        var start=new ProcessStartInfo(python,"-X utf8 -B "+Quote(Path.Combine(root,"daydesk.py"))+" "+string.Join(" ",args.Select(Quote))) { WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
        using(var process=Process.Start(start)) {
            var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
            process.WaitForExit(); Console.Out.Write(output.GetAwaiter().GetResult()); Console.Error.Write(error.GetAwaiter().GetResult()); return process.ExitCode;
        }
    }
}
