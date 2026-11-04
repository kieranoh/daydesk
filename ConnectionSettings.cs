using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using D=System.Collections.Generic.Dictionary<string,object>;

public partial class CalendarWindow {
    bool connectionBusy;
    bool RequiredAccountsConnected() {
        using(var client=new MateClient(MateClient.Folder)) return AppPaths.TreeConnected && client.Connected;
    }
    bool ShouldPromptForAccounts { get { return awaitingInitialConnections && !B(prefs,"account_setup_deferred"); } }
    void DeferAccountSetup() {
        if(!CanClose()) throw new InvalidOperationException("진행 중인 연결·저장을 마친 뒤 건너뛰어 주세요.");
        prefs["account_setup_deferred"]=true;
        if(!Save()) throw new IOException("나중에 연결 설정을 저장하지 못했습니다. 다시 시도해 주세요.");
    }
    async Task<D> CheckTreeConnection(string email,string password) {
        var result=await RunTreeRequest(new D { {"action","test-timetree"},{"email",email},{"password",password} });
        if(!B(result,"connected")) throw new InvalidOperationException("TimeTree 계정 연결을 확인하지 못했습니다.");
        return result;
    }
    async Task<D> RunTreeRequest(D request) {
        var info=new ProcessStartInfo(python,"-X utf8 -B \""+Path.Combine(Root,"connections.py")+"\"") {
            WorkingDirectory=Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        };
        using(var process=new Process { StartInfo=info }) {
            process.Start(); var output=process.StandardOutput.ReadToEndAsync(); var errors=process.StandardError.ReadToEndAsync();
            process.StandardInput.WriteLine(Json.Serialize(request)); process.StandardInput.Close();
            if(!await Task.Run(()=>process.WaitForExit(240000))) {
                try { using(var kill=Process.Start(new ProcessStartInfo("taskkill.exe","/PID "+process.Id+" /T /F") { UseShellExecute=false,CreateNoWindow=true })) kill.WaitForExit(5000); } catch { }
                throw new TimeoutException("TimeTree 응답이 지연됩니다. 창을 닫고 새로고침으로 반영 여부를 확인해 주세요.");
            }
            string body=await output; await errors;
            D result;
            try { result=Json.Deserialize<D>(body); } catch { throw new InvalidOperationException("TimeTree 연결 확인을 실행하지 못했습니다. 프로그램을 다시 설치해 주세요."); }
            if(process.ExitCode!=0 || result.ContainsKey("error")) throw new InvalidOperationException(S(result,"error","TimeTree 요청을 확인하지 못했습니다. 새로고침으로 반영 여부를 확인해 주세요."));
            return result;
        }
    }
    async Task ConnectJob(FrameworkElement page,TextBlock message,Func<Task> action) {
        if(connectionBusy || todoPanel.HasPendingWrite) { message.Text="진행 중인 저장을 마친 뒤 다시 시도해 주세요."; return; }
        connectionBusy=true; page.IsEnabled=false; message.Text="연결을 확인하고 있습니다…";
        try { await action(); }
        catch(Exception error) { message.Text=error.Message; }
        finally {
            connectionBusy=false; page.IsEnabled=true;
            if(awaitingInitialConnections && RequiredAccountsConnected()) { awaitingInitialConnections=false; SetTransparency(backgroundTransparency); }
        }
    }
    void ResetTreeView() {
        generation++; loading=false; model=null; paintedModel=""; calendarId=""; calendarList.Clear(); calendars.ItemsSource=null;
        calendar.Children.Clear(); calendar.RowDefinitions.Clear(); calendar.ColumnDefinitions.Clear(); dateButtons.Clear();
    }
    FrameworkElement AccountSettingsPage() {
        var page=new StackPanel { Margin=new Thickness(0,0,10,0) };
        Action<string> label=text=>page.Children.Add(new TextBlock { Text=text,Foreground=Brush("#D5E3E8"),FontSize=12,Margin=new Thickness(0,7,0,5),TextWrapping=TextWrapping.Wrap });
        bool firstTitle=true;
        Action<string> title=text=> { var item=Text(text,16,true); item.Margin=new Thickness(0,firstTitle ? 0 : 24,0,5); firstTitle=false; page.Children.Add(item); };
        Func<string,TextBox> emailField=name=> { var input=new TextBox { MinHeight=32,Margin=new Thickness(0,0,0,4) }; AutomationProperties.SetName(input,name); page.Children.Add(input); return input; };
        Func<string,PasswordBox> secretField=name=> { var input=new PasswordBox { MinHeight=32,Margin=new Thickness(0,0,0,5) }; AutomationProperties.SetName(input,name); page.Children.Add(input); return input; };
        Func<string,TextBlock> statusField=text=> { var message=Text(text,12,false); message.TextWrapping=TextWrapping.Wrap; message.Margin=new Thickness(0,6,0,0); page.Children.Add(message); return message; };
        Action<string,string> guide=delegate(string name,string instructions) {
            var details=new Border { Background=Brush("#0CFFFFFF"),CornerRadius=new CornerRadius(7),Padding=new Thickness(10),Margin=new Thickness(0,5,0,8),Visibility=Visibility.Collapsed,Child=new TextBlock { Text=instructions,Foreground=Brush("#D5E3E8"),FontSize=12,TextWrapping=TextWrapping.Wrap,LineHeight=19 } };
            var toggle=Btn("연결 방법 ▾",delegate { details.Visibility=details.Visibility==Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; });
            toggle.HorizontalAlignment=HorizontalAlignment.Left; toggle.FontSize=12; AutomationProperties.SetName(toggle,name+" 연결 방법");
            toggle.Click+=delegate { toggle.Content=details.Visibility==Visibility.Visible ? "방법 접기 ▴" : "연결 방법 ▾"; };
            var heading=(TextBlock)page.Children[page.Children.Count-1]; page.Children.Remove(heading);
            var row=new DockPanel { Margin=heading.Margin }; heading.Margin=new Thickness(0); DockPanel.SetDock(toggle,Dock.Right); row.Children.Add(toggle); row.Children.Add(heading);
            page.Children.Add(row); page.Children.Add(details);
        };

        label("PlayMCP는 선택 사항입니다.\n나중에도 설정 → 계정 연결에서 연결할 수 있습니다.");
        if(awaitingInitialConnections) label("두 계정 연결 전에는 불투명한 배경을 유지합니다.");

        title("TimeTree");
        guide("TimeTree","1. TimeTree에서 사용하는 이메일과 비밀번호를 입력합니다.\n2. ‘로그인·연결’을 누릅니다.\n3. 아래 ‘표시할 캘린더’에서 사용할 캘린더를 선택합니다.\n\nTimeTree MCP는 설치파일에 함께 설치됩니다. Codex·Node·Python이나 API 키를 따로 준비하지 않으셔도 됩니다.");
        label("TimeTree 이메일"); var treeEmail=emailField("TimeTree 이메일"); label("비밀번호"); var treePassword=secretField("TimeTree 비밀번호");
        var treeActions=new StackPanel { Orientation=Orientation.Horizontal }; page.Children.Add(treeActions);
        var treeConnect=Btn("로그인·연결",delegate {}); var treeDisconnect=Btn("연결 해제",delegate {}); treeActions.Children.Add(treeConnect); treeActions.Children.Add(treeDisconnect);
        var treeStatus=statusField(AppPaths.TreeConnected ? "연결됨 · 로그인 정보는 이 PC에 암호화해 저장됩니다." : !AppPaths.Packaged && calendarList.Count>0 ? "기존 PC 연결을 사용 중입니다. 새 계정 연결도 가능합니다." : "자동 연결을 위해 로그인 정보가 이 PC에 암호화 저장됩니다.");
        label("표시할 캘린더"); var selectCalendar=new ComboBox { Height=32,Margin=new Thickness(0,0,0,4) }; StyleCalendarSelector(selectCalendar); AutomationProperties.SetName(selectCalendar,"계정의 TimeTree 캘린더"); page.Children.Add(selectCalendar);
        bool filling=false;
        Action fillCalendars=delegate { filling=true; selectCalendar.ItemsSource=calendarList.Select(c=>S(c,"name")).ToArray(); selectCalendar.SelectedIndex=calendarList.FindIndex(c=>S(c,"id")==calendarId); filling=false; };
        fillCalendars();
        selectCalendar.SelectionChanged+=delegate { if(filling || selectCalendar.SelectedIndex<0) return; calendarId=S(calendarList[selectCalendar.SelectedIndex],"id"); calendars.SelectedIndex=selectCalendar.SelectedIndex; ScheduleSave(); };
        treeConnect.Click+=async delegate {
            string email=treeEmail.Text.Trim(),password=treePassword.Password; treePassword.Clear();
            await ConnectJob(page,treeStatus,async delegate {
                MateClient.RequireText(email,"TimeTree 이메일"); MateClient.RequireText(password,"비밀번호");
                var verified=await CheckTreeConnection(email,password);
                if(S(verified,"account_key")!=MateClient.Hash(email.ToLowerInvariant())) throw new InvalidOperationException("TimeTree 계정 확인 결과가 올바르지 않습니다.");
                StopBackend();
                SnapshotStore.WriteFile(AppPaths.TreeAuth,Json.Serialize(new { connected=true,email=email,password=password,account_key=S(verified,"account_key") }));
                ResetTreeView(); SetCalendarList(verified); SnapshotStore.Write("timetree","calendars",Json.Serialize(new { calendars=A(verified,"calendars") }));
                fillCalendars(); Save(); Refresh(); treeStatus.Text="연결 완료 · 캘린더 "+calendarList.Count+"개";
            }); password=null;
        };
        treeDisconnect.Click+=async delegate {
            await ConnectJob(page,treeStatus,delegate {
                StopBackend(); SnapshotStore.WriteFile(AppPaths.TreeAuth,Json.Serialize(new { connected=false,account_key="disconnected" })); ResetTreeView(); fillCalendars(); Save();
                status.Text="설정의 계정 연결에서 TimeTree에 로그인해 주세요."; treeStatus.Text="연결을 해제했습니다. 저장된 일정은 설치파일로 내보내지지 않습니다."; return Task.FromResult(0);
            });
        };

        title("Todo Mate");
        guide("Todo Mate","1. Todo Mate의 이메일·비밀번호 계정을 입력합니다.\n2. ‘로그인·연결’을 누르면 할 일을 불러옵니다.\n\nFirebase에 본인 계정으로 연결하며 별도 Firebase 프로젝트나 API 키는 필요하지 않습니다. 비밀번호 대신 로그인 토큰만 이 PC에 암호화해 저장합니다. PlayMCP를 연결하지 않아도 기본 조회·추가·수정·삭제·완료 기능을 사용할 수 있습니다.");
        label("Todo Mate 이메일"); var mateEmail=emailField("Todo Mate 이메일"); label("비밀번호"); var matePassword=secretField("Todo Mate 비밀번호");
        var mateActions=new StackPanel { Orientation=Orientation.Horizontal }; page.Children.Add(mateActions); var mateConnect=Btn("로그인·연결",delegate {}); var mateDisconnect=Btn("연결 해제",delegate {}); mateActions.Children.Add(mateConnect); mateActions.Children.Add(mateDisconnect);
        bool mateConnected; using(var client=new MateClient(MateClient.Folder)) mateConnected=client.Connected;
        var mateStatus=statusField(mateConnected ? "연결됨 · 비밀번호는 저장하지 않습니다." : "본인 계정으로 Firebase에 직접 연결합니다. 비밀번호는 저장하지 않습니다.");
        mateConnect.Click+=async delegate {
            string email=mateEmail.Text.Trim(),password=matePassword.Password; matePassword.Clear();
            await ConnectJob(page,mateStatus,async delegate {
                using(await TodoSnapshots.Enter()) using(var client=new MateClient(MateClient.Folder)) {
                    await client.Login(email,password); TodoSnapshots.AccountKey=client.AccountKey;
                    var day=Today(); var start=MateClient.PeriodStart(day,true); var end=MateClient.PeriodEnd(day,true);
                    var data=await client.LoadRange(start,end); TodoSnapshots.SaveMonth(data,day,start,end);
                }
                todoPanel.ReloadCached(); mateStatus.Text="연결 완료 · 로그인 토큰은 이 PC에 암호화해 저장됩니다.";
            }); password=null;
        };
        mateDisconnect.Click+=async delegate {
            await ConnectJob(page,mateStatus,async delegate {
                using(await TodoSnapshots.Enter()) { string path=Path.Combine(MateClient.Folder,"firebase-auth.bin"); if(File.Exists(path)) File.Delete(path); }
                todoPanel.ReloadCached(); mateStatus.Text="Todo Mate 연결을 해제했습니다.";
            });
        };

        title("PlayMCP 보조 연결 · 선택");
        label("Firebase 조회 한도에 걸렸을 때만 사용하는 보조 조회입니다. 지금 연결하지 않으셔도 됩니다.");
        guide("PlayMCP","1. ‘PlayMCP 열기’를 누르고 카카오 계정으로 로그인합니다.\n2. ‘투두메이트’를 찾아 도구함에 추가하고, 위에서 연결한 것과 같은 Todo Mate 계정으로 인증합니다.\n3. 도구함에서 ‘OpenClaw와 연결’ → 데이터 제공 동의 → ‘연결 프롬프트 생성’을 선택합니다.\n4. 만들어진 연결 프롬프트 전체를 복사해 아래 칸에 붙여 넣고 ‘연결’을 누릅니다.\n\n발급 후 10분 안에 붙여 넣어 주세요. 만료되면 새로 생성하시면 됩니다. ‘OpenClaw와 연결’에서 만든 텍스트를 이 위젯이 처리하므로 OpenClaw를 따로 설치할 필요는 없습니다.\n\nPlayMCP에도 자체 조회 한도가 있습니다. 이 연결은 조회만 보조하며, 추가·수정·삭제·완료는 Firebase로 처리합니다. 나중에도 설정 → 계정 연결에서 연결할 수 있습니다.");
        label("일회용 키(OTT)만 또는 연결 프롬프트 전체를 붙여 넣으세요. 키만 자동 추출합니다.");
        var mcpPrompt=secretField("PlayMCP 일회용 키 또는 연결 프롬프트 전체");
        var mcpActions=new WrapPanel(); page.Children.Add(mcpActions);
        var mcpConnect=Btn("연결",delegate {}); mcpActions.Children.Add(mcpConnect);
        mcpActions.Children.Add(Btn("PlayMCP 열기 ↗",delegate { Process.Start(new ProcessStartInfo("https://playmcp.kakao.com/") { UseShellExecute=true }); }));
        var mcpDisconnect=Btn("해제",delegate {}); mcpActions.Children.Add(mcpDisconnect);
        var mcpStatus=statusField(File.Exists(Path.Combine(AppPaths.McpFolder,"playmcp-token.bin")) ? "보조 연결 정보가 저장되어 있습니다." : "미연결 · 나중에 연결하셔도 됩니다.");
        mcpConnect.Click+=async delegate {
            string prompt=mcpPrompt.Password; mcpPrompt.Clear();
            await ConnectJob(page,mcpStatus,async delegate {
                using(await TodoSnapshots.Enter()) {
                    Directory.CreateDirectory(AppPaths.McpFolder);
                    using(var client=new LegacyMcp.MateClient(AppPaths.McpFolder)) { await client.Connect(prompt); await client.Tools(); }
                }
                mcpStatus.Text="MCP 연결 완료 · 보조 조회 시 Firebase 계정과 카테고리 일치를 확인합니다.";
            }); prompt=null;
        };
        mcpDisconnect.Click+=async delegate { await ConnectJob(page,mcpStatus,async delegate { using(await TodoSnapshots.Enter()) { string path=Path.Combine(AppPaths.McpFolder,"playmcp-token.bin"); if(File.Exists(path)) File.Delete(path); } mcpStatus.Text="MCP 보조 연결을 해제했습니다."; }); };
        return page;
    }
}
