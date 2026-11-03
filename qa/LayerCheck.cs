using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

static class LayerCheck {
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    static IntPtr Handle(Window window) { return new System.Windows.Interop.WindowInteropHelper(window).Handle; }
    static bool Below(Window bottom,Window top) {
        for(var next=GetWindow(Handle(top),2);next!=IntPtr.Zero;next=GetWindow(next,2)) if(next==Handle(bottom)) return true;
        return false;
    }
    static void Pump() { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); }
    [STAThread] static int Main(string[] args) {
        var previous=GetForegroundWindow(); Window widget=null,other=null,child=null;
        Environment.SetEnvironmentVariable("DAYDESK_DATA_DIR",Path.Combine(Path.GetTempPath(),"daydesk-layer-check-"+Guid.NewGuid().ToString("N")));
        try {
            var assembly=Assembly.LoadFrom(args[0]); var type=assembly.GetType("CalendarWindow");
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            widget=(Window)Activator.CreateInstance(type,new object[]{"",false});
            widget.Left=widget.Top=-30000; widget.ShowActivated=false; widget.Show();
            other=new Window { Left=-30000,Top=-30000,Width=200,Height=100,ShowActivated=false }; other.Show();
            var method=type.GetMethod("SetWindowLayer",BindingFlags.Instance|BindingFlags.NonPublic);
            Action<bool,bool> layer=(above,below)=> { method.Invoke(widget,new object[]{above,below}); Pump(); };
            layer(false,true);
            if(widget.Topmost || !Below(widget,other)) throw new Exception("Widget did not move behind another window");
            widget.Activate(); Pump();
            if(!Below(widget,other)) throw new Exception("Activating widget broke its back pin");
            layer(true,false); if(!widget.Topmost) throw new Exception("Top pin failed to replace back pin");
            layer(false,false); if(widget.Topmost) throw new Exception("Normal layer was not restored");
            child=new Window { Owner=widget,Left=-30000,Top=-30000,Width=200,Height=100 }; child.Show(); Pump();
            layer(false,true); if(!child.IsVisible || !Below(widget,child)) throw new Exception("Owned editor became unusable");
            child.Close(); child=null; widget.Activate(); Pump();
            if(!Below(widget,other)) throw new Exception("Back pin did not resume after editor closed");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layer-check-result.json"),"{\"passed\":true,\"backPin\":true,\"activationKeepsBack\":true,\"topPinExclusive\":true,\"ownedEditorUsable\":true}");
            return 0;
        } catch(Exception error) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layer-check-error.txt"),error.ToString()); return 1; }
        finally { if(child!=null) child.Close(); if(other!=null) other.Close(); if(widget!=null) widget.Close(); if(previous!=IntPtr.Zero) SetForegroundWindow(previous); }
    }
}
