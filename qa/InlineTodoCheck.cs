using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using D=System.Collections.Generic.Dictionary<string,object>;

static class InlineTodoCheck {
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static Assembly assembly;
    static object Call(object item,string method,params object[] args) { return item.GetType().GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(item,args); }
    static object Static(string type,string method,params object[] args) { return assembly.GetType(type).GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args); }
    static object Field(object item,string name) { return item.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(item); }
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    static void Pump() { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); }
    static void Await(object value) { var task=(Task)value; var watch=Stopwatch.StartNew(); while(!task.IsCompleted && watch.ElapsedMilliseconds<10000) { Pump(); Thread.Sleep(1); } Check(task.IsCompleted,"Inline request did not finish"); task.GetAwaiter().GetResult(); Pump(); }
    static IEnumerable<T> Children<T>(DependencyObject item) where T:DependencyObject { for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++) { var child=VisualTreeHelper.GetChild(item,i); if(child is T) yield return (T)child; foreach(var nested in Children<T>(child)) yield return nested; } }
    static void Image(FrameworkElement element,string name) {
        var visual=new DrawingVisual(); using(var drawing=visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name))) encoder.Save(file);
    }
    sealed class Fake : HttpMessageHandler {
        internal Queue<Func<HttpRequestMessage,string,object>> Calls=new Queue<Func<HttpRequestMessage,string,object>>();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            Check(Calls.Count>0,"Unexpected network request"); string body=request.Content==null ? "" : await request.Content.ReadAsStringAsync();
            var result=Calls.Dequeue()(request,body); return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(Json.Serialize(result),Encoding.UTF8,"application/json") };
        }
    }
    static D V(object value) { return value is bool ? new D { {"booleanValue",value} } : new D { {"stringValue",value} }; }
    static long Stamp(DateTime day) { return (long)Static("MateClient","Stamp",day); }
    static D Document(DateTime day,string content="기존 내용",bool remind=false) {
        return new D { {"name","projects/mate-914f3/databases/(default)/documents/TodoItem/sample-task"},{"updateTime","2026-11-04T00:00:00.123456Z"},
            {"fields",new D { {"writerID",V("synthetic-owner")},{"content",V(content)},{"date",new D { {"integerValue",Stamp(day).ToString()} }},{"goalID",V("sample-goal")},{"isDone",V(false)},{"remindAt",remind ? new D { {"integerValue",((long)(new DateTimeOffset(day.AddHours(9),TimeSpan.FromHours(9))-new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero)).TotalMilliseconds).ToString()} } : new D { {"nullValue",null} }} }} };
    }
    static D Fields(string body) { return (D)((D)Json.DeserializeObject(body))["fields"]; }
    static void Goals(Fake fake) {
        var document=new D { {"name","projects/mate-914f3/databases/(default)/documents/Goal/empty-goal"},{"fields",new D { {"userID",V("synthetic-owner")},{"title",V("빈 카테고리")} }} };
        var existing=new D { {"name","projects/mate-914f3/databases/(default)/documents/Goal/sample-goal"},{"fields",new D { {"userID",V("synthetic-owner")},{"title",V("샘플")} }} };
        fake.Calls.Enqueue((r,b)=>new[]{new D { {"document",existing} },new D { {"document",document} }});
    }
    [STAThread] static int Main(string[] args) {
        Environment.SetEnvironmentVariable("DAYDESK_DATA_DIR",Path.Combine(Path.GetTempPath(),"daydesk-inline-check-"+Guid.NewGuid().ToString("N")));
        Window window=null;
        try {
            assembly=Assembly.LoadFrom(args[0]); var day=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date; string profile=Environment.GetEnvironmentVariable("DAYDESK_DATA_DIR");
            Static("SnapshotStore","WriteFile",Path.Combine(profile,"firebase","firebase-auth.bin"),Json.Serialize(new { uid="synthetic-owner",id_token="sample-id",refresh_token="sample-refresh",expires=DateTime.UtcNow.AddHours(1).ToString("o") }),null);
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            window=(Window)Activator.CreateInstance(assembly.GetType("CalendarWindow"),new object[]{"",false}); window.Left=window.Top=-30000; window.ShowActivated=false;
            var panel=(FrameworkElement)Field(window,"todoPanel"); var fake=new Fake();
            var client=Activator.CreateInstance(assembly.GetType("MateClient"),new object[]{Path.Combine(profile,"firebase"),fake,"synthetic-key"}); panel.GetType().GetField("client",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(panel,client);
            var data=Json.Deserialize(Json.Serialize(new { Goals=new[]{new { Id="sample-goal",Title="샘플" },new { Id="empty-goal",Title="빈 카테고리" }},Todos=new[]{new { Id="sample-task",Content="기존 내용",GoalId="sample-goal",Category="샘플",Date=day,Done=false,Reminder="" }} }),assembly.GetType("WeekData"));
            foreach(var todo in ((IEnumerable)Field(data,"Todos"))) todo.GetType().GetField("Date").SetValue(todo,day);
            assembly.GetType("TodoSnapshots").GetField("AccountKey",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,Static("MateClient","Hash","synthetic-owner"));
            Static("TodoSnapshots","SaveMonth",data,day,day.AddDays(-3),day.AddDays(3)); Call(panel,"ApplyData",data,day.AddDays(-3),(DateTime?)day.AddDays(3));
            window.Show(); Call(window,"SetWindowLayer",false,true); Pump();
            var goals=((IEnumerable)Field(data,"Goals")).Cast<object>().ToList(); var original=((IEnumerable)Field(data,"Todos")).Cast<object>().First();
            Call(panel,"BeginInline",null,goals[1]); Pump();
            var input=(TextBox)Field(panel,"inlineInput"); Check(input!=null && input.IsKeyboardFocused,"Back-pinned inline input cannot receive keyboard focus");
            Check(window.OwnedWindows.Count==0 && input.ActualHeight<65,"Adding opened a popup or oversized editor");
            window.UpdateLayout();
            var adding=(Border)Field(panel,"inlineEditor");
            Check(((SolidColorBrush)input.Background).Color.A==0 && input.BorderThickness==new Thickness(0) && adding.BorderThickness==new Thickness(0,0,0,1),"Adding is not transparent with only one bottom line");
            Check(!Children<Button>(adding).Any() && adding.ActualHeight<30,"Adding still has a button panel or oversized row");
            Image(panel,"inline-add-underline.png");
            input.Text="한글 인라인 입력 & $(예시)"; Goals(fake);
            fake.Calls.Enqueue((r,b)=> { Check(r.Method==HttpMethod.Post && r.RequestUri.AbsolutePath.EndsWith("/TodoItem"),"Add did not use Firebase TodoItem"); var fields=Fields(b); Check((string)((D)fields["content"])["stringValue"]==input.Text,"Inline content changed"); return new D { {"name","projects/mate-914f3/databases/(default)/documents/TodoItem/created-task"},{"fields",fields} }; });
            Await(Call(panel,"SaveInline",true)); Check(input.Text=="" && Field(panel,"inlineInput")==input,"Enter did not keep an empty row for the next task"); Check(fake.Calls.Count==0,"Add request sequence incomplete");
            input.Text="취소할 내용";
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape) { RoutedEvent=UIElement.PreviewKeyDownEvent }); Pump();
            Check(Field(panel,"inlineInput")==null && fake.Calls.Count==0,"Escape saved or lost the cancelled draft state");
            Check((GetWindowLongPtr(new System.Windows.Interop.WindowInteropHelper(window).Handle,-20).ToInt64()&0x08000000L)!=0,"Closing inline input left the widget activatable");
            window.UpdateLayout();
            var label=Children<TextBlock>(panel).Single(t=>t.Text==(string)Field(original,"Content"));
            Point position=label.TranslatePoint(new Point(),panel); double height=label.ActualHeight;
            Image(panel,"inline-edit-before.png");
            Call(panel,"BeginInline",original,goals[0]); Pump(); window.UpdateLayout(); input=(TextBox)Field(panel,"inlineInput");
            Check(((SolidColorBrush)input.Background).Color.A==0 && input.BorderThickness==new Thickness(0) && input.Padding==new Thickness(0),"Editing added an opaque field or frame");
            Check(((Border)Field(panel,"inlineEditor")).BorderThickness==new Thickness(0,0,0,1),"Editing is missing its subtle bottom line");
            Check(input.Foreground.ToString()==label.Foreground.ToString() && input.FontSize==label.FontSize,"Editing changed text appearance");
            Check((input.TranslatePoint(new Point(),panel)-position).Length<2 && Math.Abs(input.ActualHeight-height)<2,"Editing moved or resized the original text row");
            Check(Children<CheckBox>((DependencyObject)Field(panel,"list")).Count()==2,"Editing removed the original row checkbox");
            Check(!Children<Button>((DependencyObject)Field(panel,"inlineEditor")).Any(),"Editing added a button panel");
            Image(panel,"inline-edit-after.png");
            input.Text="수정된 내용";
            fake.Calls.Enqueue((r,b)=>Document(day)); fake.Calls.Enqueue((r,b)=> { Check(r.Method.Method=="PATCH" && Fields(b).Count==1 && Fields(b).ContainsKey("content"),"Inline edit overwrote metadata"); return Document(day,"수정된 내용"); });
            Await(Call(panel,"SaveInline",false)); Check(Field(panel,"inlineInput")==null && fake.Calls.Count==0,"Inline edit did not finish");
            Check((string)Field(((IEnumerable)Field(data,"Todos")).Cast<object>().First(),"Id")=="sample-task","Editing moved a task out of its existing position");
            var moved=((IEnumerable)Field(data,"Todos")).Cast<object>().Single(t=>(string)Field(t,"Id")=="sample-task"); moved.GetType().GetField("Reminder").SetValue(moved,day.ToString("yyyy-MM-dd")+"T09:00");
            fake.Calls.Enqueue((r,b)=>Document(day,"수정된 내용",true)); fake.Calls.Enqueue((r,b)=> {
                var fields=Fields(b); Check(fields.Count==2 && fields.ContainsKey("date") && fields.ContainsKey("remindAt"),"Postpone lost reminder or changed other fields");
                Check(Convert.ToInt64(((D)fields["date"])["integerValue"])==Stamp(day.AddDays(1)) && Convert.ToInt64(((D)fields["remindAt"])["integerValue"])==Stamp(day.AddDays(1)),"Postpone did not move date and 09:00 KST reminder by one day");
                return Document(day.AddDays(1),"수정된 내용",true);
            });
            Await(Call(panel,"Postpone",moved)); var cached=Static("TodoSnapshots","Read",day); var rows=((IEnumerable)Field(Field(cached,"Data"),"Todos")).Cast<object>().ToList();
            Check(rows.Single(t=>(string)Field(t,"Id")=="sample-task") is object && rows.All(t=>(string)Field(t,"Id")!="sample-task" || (DateTime)Field(t,"Date")==day.AddDays(1)),"Postpone did not move the cached task");
            Call(panel,"BeginInline",null,goals[1]); Pump(); input=(TextBox)Field(panel,"inlineInput"); input.Text="응답 불명확 검증"; Goals(fake); int writes=0;
            fake.Calls.Enqueue((r,b)=> { writes++; throw new TaskCanceledException(); });
            Await(Call(panel,"SaveInline",true)); Await(Call(panel,"SaveInline",true)); Check(writes==1 && input.Text=="응답 불명확 검증" && input.IsReadOnly,"Uncertain add was repeated or draft was lost");
            Call(panel,"CloseInline");
            if(args.Length>1 && args[1]=="--preview") { window.Title="Daydesk 인라인 입력 검증"; window.Left=100; window.Top=100; Call(window,"SetWindowLayer",false,false); window.Closed+=delegate { app.Shutdown(); }; app.Run(); return 0; }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"inline-todo-result.json"),"{\"passed\":true,\"inlineAdd\":true,\"inlineEdit\":true,\"postponeAndReminder\":true,\"backPinKeyboardFocus\":true,\"uncertainWriteNotRepeated\":true}");
            string errorFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"inline-todo-error.txt"); if(File.Exists(errorFile)) File.Delete(errorFile);
            return 0;
        } catch(Exception error) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"inline-todo-error.txt"),error.ToString()); return 1; }
        finally { if(window!=null) window.Close(); }
    }
}
