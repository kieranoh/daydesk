using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using System.Collections.Generic;

static class LayerCheck {
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    static IntPtr Handle(Window window) { return new System.Windows.Interop.WindowInteropHelper(window).Handle; }
    static bool Below(Window bottom,Window top) {
        return Below(Handle(bottom),Handle(top));
    }
    static bool Below(IntPtr bottom,IntPtr top) {
        for(var next=GetWindow(top,2);next!=IntPtr.Zero;next=GetWindow(next,2)) if(next==bottom) return true;
        return false;
    }
    static void Pump() { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); }
    [STAThread] static int Main(string[] args) {
        if(args[0]=="--peer") {
            var peerApp=new Application(); var peerWindow=new Window { Left=-30000,Top=-30000,Width=200,Height=100,ShowActivated=false };
            peerWindow.Loaded+=delegate { File.WriteAllText(args[1],Handle(peerWindow).ToInt64().ToString()); };
            peerApp.Run(peerWindow); return 0;
        }
        var previous=GetForegroundWindow(); Window widget=null,other=null,child=null;
        var peers=new List<Process>(); var peerFiles=new List<string>();
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
            var peerHandles=new List<IntPtr>();
            for(int i=0;i<2;i++) {
                string file=Path.Combine(Path.GetTempPath(),"daydesk-peer-"+Guid.NewGuid().ToString("N")+".txt"); peerFiles.Add(file);
                peers.Add(Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--peer \""+file+"\"") { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden }));
                for(int attempt=0;attempt<150 && !File.Exists(file);attempt++) Thread.Sleep(20);
                peerHandles.Add(new IntPtr(long.Parse(File.ReadAllText(file))));
            }
            int transientRaises=0;
            bool watching=true;
            var source=System.Windows.Interop.HwndSource.FromHwnd(Handle(widget));
            source.AddHook(delegate(IntPtr window,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
                if(watching && message==0x0047 && (!Below(Handle(widget),peerHandles[0]) || !Below(Handle(widget),peerHandles[1]))) transientRaises++;
                return IntPtr.Zero;
            });
            IntPtr foreground=GetForegroundWindow();
            for(int attempt=0;attempt<20;attempt++) {
                SetWindowPos(peerHandles[attempt%2],IntPtr.Zero,0,0,0,0,0x0213); Pump();
                bool order=Below(peerHandles[0],peerHandles[1]);
                if(SendMessage(Handle(widget),0x0021,Handle(widget),new IntPtr((0x0201<<16)|1)).ToInt32()!=3) throw new Exception("Clicking back-pinned widget would activate it");
                SetWindowPos(Handle(widget),IntPtr.Zero,0,0,0,0,0x0213); Pump();
                if(!Below(Handle(widget),peerHandles[0]) || !Below(Handle(widget),peerHandles[1])) throw new Exception("Back pin allowed widget above another process");
                if(order!=Below(peerHandles[0],peerHandles[1])) throw new Exception("Widget reordered unrelated windows");
                if(GetForegroundWindow()!=foreground) throw new Exception("Widget stole foreground focus");
            }
            watching=false;
            if(transientRaises!=0) throw new Exception("Widget briefly appeared above external windows");
            widget.Activate(); Pump();
            if(!Below(widget,other)) throw new Exception("Activating widget broke its back pin");
            layer(true,false); if(!widget.Topmost) throw new Exception("Top pin failed to replace back pin");
            layer(false,false); if(widget.Topmost) throw new Exception("Normal layer was not restored");
            var input=new System.Windows.Controls.TextBox { Text="sample" };
            child=new Window { Owner=widget,Left=-30000,Top=-30000,Width=200,Height=100,Content=input }; child.Show(); Pump();
            input.Focus(); Pump();
            if(!input.IsKeyboardFocused) throw new Exception("Owned editor could not accept keyboard input");
            layer(false,true); if(!child.IsVisible || !Below(widget,child)) throw new Exception("Owned editor became unusable");
            child.Close(); child=null; widget.Activate(); Pump();
            if(!Below(widget,other)) throw new Exception("Back pin did not resume after editor closed");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layer-check-result.json"),"{\"passed\":true,\"backPin\":true,\"activationKeepsBack\":true,\"topPinExclusive\":true,\"ownedEditorUsable\":true,\"externalProcesses\":2,\"switchCycles\":20,\"externalOrderAndFocusPreserved\":true,\"transientRaises\":0,\"editorKeyboardFocus\":true}");
            string errorPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layer-check-error.txt"); if(File.Exists(errorPath)) File.Delete(errorPath);
            return 0;
        } catch(Exception error) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layer-check-error.txt"),error.ToString()); return 1; }
        finally {
            foreach(var peer in peers) { try { peer.CloseMainWindow(); if(!peer.WaitForExit(2000)) peer.Kill(); } catch { } peer.Dispose(); }
            foreach(string file in peerFiles) if(File.Exists(file)) File.Delete(file);
            if(child!=null) child.Close(); if(other!=null) other.Close(); if(widget!=null) widget.Close(); if(previous!=IntPtr.Zero) SetForegroundWindow(previous);
        }
    }
}
