using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;

static class FirebaseTodo {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer { MaxJsonLength=16000000 };
    [STAThread] static int Main(string[] args) {
        Console.OutputEncoding=new UTF8Encoding(false);
        try {
            if(args.Length==1 && args[0]=="login") {
                Application.EnableVisualStyles(); Application.Run(new FirebaseLogin());
                Console.WriteLine(Json.Serialize(new { connected=File.Exists(Path.Combine(MateClient.Folder,"firebase-auth.bin")) })); return 0;
            }
            Console.WriteLine(Json.Serialize(Run(args).GetAwaiter().GetResult())); return 0;
        } catch(Exception error) { Console.Error.WriteLine(Json.Serialize(new { error=error is TaskCanceledException ? "서버 응답이 지연됩니다. 새로고침으로 반영 여부를 확인한 뒤 다시 시도해 주세요." : error.Message })); return 1; }
    }
    static DateTime Day(string value) { return DateTime.ParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture); }
    static object Item(Todo t) { return new { id=t.Id,content=t.Content,goal_id=t.GoalId,category=t.Category ?? "할 일",date=t.Date.ToString("yyyy-MM-dd"),done=t.Done,reminder=t.Reminder ?? "" }; }
    static async Task<object> Run(string[] args) {
        if(args.Length==0) throw new ArgumentException("명령을 지정해 주세요.");
        var options=new Dictionary<string,string>();
        for(int i=1;i<args.Length;i++) {
            string key=args[i]; if(!key.StartsWith("--") || options.ContainsKey(key)) throw new ArgumentException("잘못된 옵션입니다.");
            if(key=="--refresh" || key=="--clear-reminder") options[key]="true";
            else { if(++i>=args.Length) throw new ArgumentException("옵션 값이 필요합니다."); options[key]=args[i]; }
        }
        string command=args[0];
        string[] allowed=command=="list" ? new[]{"--date","--view","--refresh"} : command=="goals" ? new[]{"--refresh"} : command=="add" ? new[]{"--goal","--content","--date"} : command=="edit" ? new[]{"--id","--content","--date","--goal","--done","--remind-at","--clear-reminder"} : command=="delete" ? new[]{"--id"} : new string[0];
        if(!new[]{"status","list","goals","add","edit","delete"}.Contains(command) || options.Keys.Except(allowed).Any()) throw new ArgumentException("지원하지 않는 명령 또는 옵션입니다.");
        Func<string,string> opt=k=>options.ContainsKey(k) ? options[k] : null;
        DateTime date=opt("--date")==null ? DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date : Day(opt("--date"));
        bool? done=null; if(opt("--done")!=null) { bool b; if(!bool.TryParse(opt("--done"),out b)) throw new ArgumentException("완료 값은 true/false입니다."); done=b; }
        DateTime? reminder=null;
        if(opt("--remind-at")!=null) reminder=DateTime.ParseExact(opt("--remind-at"),"yyyy-MM-ddTHH:mm",CultureInfo.InvariantCulture);
        if(reminder.HasValue && options.ContainsKey("--clear-reminder")) throw new ArgumentException("알림 설정과 해제를 동시에 지정할 수 없습니다.");
        if(command=="add") { MateClient.RequireText(opt("--goal"),"카테고리"); MateClient.RequireText(opt("--content"),"내용"); }
        if(command=="edit" || command=="delete") MateClient.Id(opt("--id"));
        if(opt("--content")!=null) MateClient.RequireText(opt("--content"),"내용");
        using(var client=new MateClient(MateClient.Folder)) {
            TodoSnapshots.AccountKey=client.AccountKey;
            if(command=="status") return new { provider="firebase",connected=client.Connected,read_fallback=client.ReadStatus };
            if(!client.Connected) throw new InvalidOperationException("Firebase 계정 연결이 필요합니다. daydesk todo login을 실행해 주세요.");
            using(await TodoSnapshots.Enter()) {
                if(command=="goals") {
                    var goals=options.ContainsKey("--refresh") ? null : TodoSnapshots.ReadGoals();
                    if(goals==null) { goals=await client.LoadGoals(); TodoSnapshots.SaveGoals(goals); }
                    return goals.Select(g=>new { id=g.Id,title=g.Title }).ToArray();
                }
                if(command=="list") {
                    string view=opt("--view") ?? "day"; if(!new[]{"day","week","month"}.Contains(view)) throw new ArgumentException("보기는 day/week/month입니다.");
                    DateTime start=view=="day" ? date : view=="week" ? MateClient.WeekStart(date) : new DateTime(date.Year,date.Month,1);
                    DateTime end=view=="day" ? date : view=="week" ? start.AddDays(6) : start.AddMonths(1).AddDays(-1);
                    var saved=options.ContainsKey("--refresh") ? null : TodoSnapshots.Read(start); bool cached=saved!=null && saved.End>=end;
                    if(!cached) {
                        var lo=MateClient.PeriodStart(date,true); var hi=MateClient.PeriodEnd(date,true);
                        if(start<lo) lo=start; if(end>hi) hi=end;
                        var fresh=await client.LoadRange(lo,hi); TodoSnapshots.SaveMonth(fresh,date,lo,hi); saved=TodoSnapshots.Read(date);
                    }
                    return new { start=start.ToString("yyyy-MM-dd"),end=end.ToString("yyyy-MM-dd"),todos=saved.Data.Todos.Where(t=>t.Date>=start && t.Date<=end).Select(Item).ToArray(),cached=cached,updated_at=saved.Updated.ToString("o"),provider=saved.Data.Source ?? "firebase",notice=saved.Data.Notice ?? "" };
                }
                if(command=="delete") {
                    await client.Delete(opt("--id"));
                    try { TodoSnapshots.Remove(opt("--id")); return new { deleted=true,id=opt("--id") }; }
                    catch { return new { deleted=true,id=opt("--id"),cache_warning="서버 삭제는 완료되었습니다. 캐시 갱신을 위해 새로고침해 주세요." }; }
                }
                Todo updated=command=="add" ? await client.Add(opt("--goal"),opt("--content"),date) : await client.Update(opt("--id"),opt("--content"),options.ContainsKey("--date") ? (DateTime?)date : null,opt("--goal"),done,reminder.HasValue || options.ContainsKey("--clear-reminder"),reminder);
                string warning=null;
                try {
                    TodoSnapshots.Patch(updated);
                    if(TodoSnapshots.Read(updated.Date)==null) TodoSnapshots.SaveDay(await client.LoadRange(updated.Date,updated.Date),updated.Date);
                } catch { warning="서버 저장은 완료되었습니다. 캐시 갱신을 위해 새로고침해 주세요."; }
                return new { saved=true,todo=Item(updated),cache_warning=warning };
            }
        }
    }
}

sealed class FirebaseLogin : Form {
    readonly TextBox email=new TextBox { AccessibleName="Todo Mate 이메일" },password=new TextBox { UseSystemPasswordChar=true,AccessibleName="Todo Mate 비밀번호" };
    readonly Label status=new Label { AutoSize=false,Height=70 };
    readonly Button login=new Button { Text="로그인하고 연결",Height=36 };
    bool working,verified;
    internal FirebaseLogin() {
        Text="Todo Mate · 위젯/CLI Firebase 계정 연결"; Font=new Font("Malgun Gothic",10); ClientSize=new Size(480,350);
        StartPosition=FormStartPosition.CenterScreen; TopMost=true; ShowInTaskbar=true; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false;
        var panel=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1 };
        foreach(Control c in new Control[]{new Label { Text="본인 Todo Mate 계정으로 로그인해 주세요.",Height=28 },new Label { Text="이메일",Height=24 },email,new Label { Text="비밀번호",Height=24 },password,login,status}) { c.Dock=DockStyle.Top; c.Margin=new Padding(0,0,0,10); panel.Controls.Add(c); }
        Controls.Add(panel); AcceptButton=login; status.Text="비밀번호는 저장하지 않습니다. 갱신 토큰은 현재 Windows 계정으로 암호화해 저장됩니다.";
        FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(working) e.Cancel=true; };
        login.Click+=async delegate {
            if(verified) { Close(); return; }
            if(working) return; working=true; login.Enabled=email.Enabled=password.Enabled=false; status.Text="계정 연결 및 본인 할 일 조회 중…";
            string secret=password.Text; password.Clear();
            try {
                using(var client=new MateClient(MateClient.Folder)) {
                    using(await TodoSnapshots.Enter()) {
                        await client.Login(email.Text,secret); secret=null; TodoSnapshots.AccountKey=client.AccountKey;
                        DateTime day=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date,lo=MateClient.PeriodStart(day,true),hi=MateClient.PeriodEnd(day,true);
                        var data=await client.LoadRange(lo,hi); TodoSnapshots.SaveMonth(data,day,lo,hi);
                        status.Text="연결 완료 · 이번 달 할 일 "+data.Todos.Count+"개 · 카테고리 "+data.Goals.Count+"개\n이 창을 닫으셔도 위젯과 CLI에서 계속 사용하실 수 있습니다.";
                        Directory.CreateDirectory(MateClient.Folder);
                        File.WriteAllText(Path.Combine(MateClient.Folder,"connection-status.json"),new JavaScriptSerializer().Serialize(new { connected=true,verified=true,todo_count=data.Todos.Count,goal_count=data.Goals.Count,updated_at=DateTime.UtcNow.ToString("o") }));
                    }
                }
                verified=true; login.Text="닫기"; login.Enabled=true; email.Enabled=password.Enabled=false;
            } catch(Exception error) { status.Text=error.Message; login.Enabled=email.Enabled=password.Enabled=true; }
            finally { secret=null; working=false; }
        };
    }
}

static class FirebaseAccount {
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [STAThread] static void Main() {
        Application.EnableVisualStyles();
        var form=new FirebaseLogin();
        form.Shown+=delegate { ShowWindow(form.Handle,5); SetForegroundWindow(form.Handle); form.Activate(); };
        Application.Run(form);
    }
}
