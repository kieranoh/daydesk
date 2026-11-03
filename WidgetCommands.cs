using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Threading;
using D = System.Collections.Generic.Dictionary<string, object>;

// Commands are restricted to the signed-in user and executed on the UI thread.
sealed class WidgetCommands : IDisposable {
    readonly Dispatcher dispatcher;
    readonly Func<D,D> handle;
    readonly Action close;
    readonly object gate=new object();
    NamedPipeServerStream pipe;
    bool stopped;
    internal WidgetCommands(Dispatcher ui,Func<D,D> handler,Action shutdown) {
        dispatcher=ui; handle=handler; close=shutdown;
        new Thread(Run) { IsBackground=true, Name="Widget commands" }.Start();
    }
    internal static D Execute(Func<D,D> handler,D request) {
        try { return new D { {"ok",true},{"result",handler(request)} }; }
        catch(Exception error) { return new D { {"ok",false},{"error",error.GetBaseException().Message} }; }
    }
    void Run() {
        while(true) {
            try {
                var security=new PipeSecurity();
                security.SetAccessRuleProtection(true,false);
                security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
                NamedPipeServerStream current;
                lock(gate) {
                    if(stopped) return;
                    pipe=current=new NamedPipeServerStream(AppPaths.CommandPipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);
                }
                using(current) {
                    current.WaitForConnection();
                    using(var timeout=new Timer(delegate { try { current.Dispose(); } catch {} },null,8000,Timeout.Infinite)) {
                        var bytes=new List<byte>(); int next;
                        while((next=current.ReadByte())!=-1 && next!=10) {
                            if(bytes.Count>=8192) throw new InvalidDataException("Command is too long");
                            bytes.Add((byte)next);
                        }
                        if(next!=10) continue;
                        var json=new JavaScriptSerializer { MaxJsonLength=8192 };
                        D result; bool quit=false;
                        try {
                            var request=json.Deserialize<D>(Encoding.UTF8.GetString(bytes.ToArray()));
                            var op=dispatcher.BeginInvoke(DispatcherPriority.Normal,new Func<D>(()=>Execute(handle,request)));
                            if(op.Wait(TimeSpan.FromSeconds(4))!=DispatcherOperationStatus.Completed) { op.Abort(); throw new TimeoutException("위젯이 다른 작업을 처리 중입니다. 잠시 후 다시 시도해 주세요."); }
                            result=(D)op.Result; quit=(bool)result["ok"] && request.ContainsKey("op") && Convert.ToString(request["op"])=="close";
                        } catch(Exception error) {
                            var inner=error.GetBaseException();
                            result=new D { {"ok",false},{"error",inner.Message} };
                        }
                        byte[] reply=Encoding.UTF8.GetBytes(json.Serialize(result)+"\n");
                        current.Write(reply,0,reply.Length); current.Flush();
                        if(quit) dispatcher.BeginInvoke(DispatcherPriority.Background,close);
                    }
                }
            } catch { lock(gate) { if(stopped) return; } Thread.Sleep(250); }
        }
    }
    public void Dispose() { lock(gate) { stopped=true; if(pipe!=null) pipe.Dispose(); } }
}
