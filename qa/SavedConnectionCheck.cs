using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

static class SavedConnectionCheck {
    static object Call(Type type,string name,params object[] args) { return type.GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,args); }
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    [STAThread] static int Main(string[] args) {
        string profile=Path.Combine(Path.GetTempPath(),"daydesk-connection-check-"+Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("DAYDESK_DATA_DIR",profile);
        try {
            var assembly=Assembly.LoadFrom(args[0]); var snapshots=assembly.GetType("SnapshotStore"); var mate=assembly.GetType("MateClient");
            var json=new JavaScriptSerializer(); var day=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date;
            string account=(string)Call(mate,"Hash","synthetic-owner");
            Call(snapshots,"WriteFile",Path.Combine(profile,"firebase","firebase-auth.bin"),json.Serialize(new { uid="synthetic-owner",id_token="synthetic-id",refresh_token="synthetic-refresh",expires=DateTime.UtcNow.AddHours(1).ToString("o") }),null);
            string payload=json.Serialize(new { account_key=account,source="firebase",notice="",start=day.AddDays(-10).ToString("yyyy-MM-dd"),end=day.AddDays(10).ToString("yyyy-MM-dd"),
                goals=new[]{new { id="sample-goal",title="샘플" }},todos=new[]{new { id="sample-task",content="저장된 할 일",goal_id="sample-goal",date=day.ToString("yyyy-MM-dd"),done=false,reminder="" }} });
            Call(snapshots,"Write","todo",account+"|"+day.ToString("yyyy-MM"),payload,null);
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            var panelType=assembly.GetType("TodoPanel");
            for(int attempt=0;attempt<2;attempt++) {
                var panel=(FrameworkElement)Activator.CreateInstance(panelType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{day,new Action<DateTime>(date=>{}),true},null);
                var login=(Button)panelType.GetField("loginButton",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(panel);
                var add=(Button)panelType.GetField("addButton",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(panel);
                Check(login.Visibility==Visibility.Collapsed && add.IsEnabled,"Saved account was shown as disconnected before first paint");
                var window=new Window { Content=panel,Left=-30000,Top=-30000,Width=400,Height=500,ShowActivated=false }; window.Show();
                var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame);
                Check(login.Visibility==Visibility.Collapsed && add.IsEnabled,"Saved account was not retained after Loaded or reopen");
                ((IDisposable)panel).Dispose(); window.Close();
            }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"saved-connection-result.json"),"{\"passed\":true,\"beforeFirstPaint\":true,\"afterLoaded\":true,\"reopenedWithoutLogin\":true}");
            return 0;
        } catch(Exception error) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"saved-connection-error.txt"),error.ToString()); return 1; }
    }
}
