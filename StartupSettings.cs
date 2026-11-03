using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

public partial class CalendarWindow {
    bool StartupState(string mode) {
        string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
        var info=new ProcessStartInfo(powershell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+Path.Combine(Root,"startup.ps1")+"\" "+mode) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8 };
        using(var process=Process.Start(info)) {
            var output=process.StandardOutput.ReadToEndAsync(); var errors=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(20000)) { try { process.Kill(); } catch { } throw new TimeoutException("자동 실행 설정 확인이 지연됩니다."); }
            errors.GetAwaiter().GetResult();
            if(process.ExitCode!=0) throw new InvalidOperationException("자동 실행 설정을 처리하지 못했습니다. 다른 설치본의 시작프로그램인지 확인해 주세요.");
            return output.GetAwaiter().GetResult().Trim().EndsWith("True",StringComparison.OrdinalIgnoreCase);
        }
    }
    async Task LoadStartup(CheckBox control,TextBlock notice) {
        control.IsEnabled=false;
        try { bool enabled=await Task.Run(()=>StartupState("status")); if(settings!=null) control.IsChecked=enabled; }
        catch(Exception error) { if(settings!=null) notice.Text=error.Message; }
        finally { if(settings!=null) control.IsEnabled=true; }
    }
}
