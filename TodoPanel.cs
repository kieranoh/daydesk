using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

sealed class TodoPanel : UserControl, IDisposable {
    readonly string accountFolder=AppPaths.McpFolder;
    readonly string preferencesFile=Path.Combine(AppPaths.DataRoot,"todo-panel.json");
    internal Action ConnectRequested;
    readonly Action<DateTime> select;
    readonly bool connect;
    readonly StackPanel list=new StackPanel();
    readonly WrapPanel categories=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right };
    readonly UniformGrid week=new UniformGrid { Columns=7 };
    readonly TextBlock dateTitle=Theme.Text("",14,true), status=Theme.Text("연결 중…",11);
    readonly ScrollViewer scroll;
    readonly Button addButton,loginButton;
    internal FrameworkElement CategoryControls { get { return categories; } }
    internal FrameworkElement HeaderControls { get; private set; }
    readonly DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromMinutes(1) };
    readonly HashSet<string> hidden=new HashSet<string>();
    readonly string[] palette={"#BCA5F3","#56C4EC","#54CEC6","#F4BB80","#F292BC","#90B5F5","#C9AAE0","#B0CF86"};
    MateClient client;
    WeekData data;
    DateTime selected, loadedStart, loadedEnd, syncedAt, nextSync;
    string dataSignature="";
    int syncMinutes=30;
    bool showAllCategories;
    bool busy, pending, pendingForce, disposed, mutating, refreshAfterBrowser;
    TextBox inlineInput;
    Border inlineEditor;
    Todo inlineTodo;
    Goal inlineGoal;
    DateTime inlineDate;
    bool inlineUncertain;
    internal bool HasInlineEditor { get { return inlineInput!=null && inlineDate==selected; } }
    internal bool HasPendingWrite { get { return mutating || (inlineInput!=null && (inlineTodo==null ? !string.IsNullOrWhiteSpace(inlineInput.Text) : inlineInput.Text!=inlineTodo.Content)); } }

    internal static System.Windows.Shapes.Path CreateLogo() {
        return new System.Windows.Shapes.Path { Width=18,Height=22,Margin=new Thickness(0,0,6,0),Stretch=Stretch.Uniform,Fill=Brushes.Black,IsHitTestVisible=false,
            Data=Geometry.Parse("M19.5002 28.75C19.5002 35.2338 23.4499 40.9489 29.4539 44.316C23.4498 47.6831 19.5 53.3983 19.5 59.8821C19.5 66.4423 23.5433 72.2156 29.6668 75.5662C23.5434 78.9168 19.5002 84.69 19.5002 91.2501C19.5002 101.605 29.5751 110 42.0031 110C49.6673 110 56.4366 106.808 60.5 101.931C64.5634 106.808 71.3327 110 78.9969 110C91.4249 110 101.5 101.605 101.5 91.2501C101.5 84.69 97.4565 78.9168 91.3331 75.5662C97.4565 72.2155 101.5 66.4423 101.5 59.8822C101.5 53.3984 97.55 47.6832 91.5459 44.3161C97.55 40.949 101.5 35.2338 101.5 28.75C101.5 18.3947 91.4249 10 78.9969 10C71.3327 10 64.5634 13.1925 60.5 18.0686C56.4366 13.1925 49.6673 10 42.0031 10C29.5751 10 19.5002 18.3947 19.5002 28.75Z") };
    }
    internal TodoPanel(DateTime day,Action<DateTime> onSelect,bool connectAccount) {
        selected=day.Date; select=onSelect; connect=connectAccount;
        if(connect) ReadPreferences();
        var layout=new Grid(); Content=layout;
        for(int i=0;i<4;i++) layout.RowDefinitions.Add(new RowDefinition { Height=i==2 ? new GridLength(1,GridUnitType.Star) : GridLength.Auto });
        var header=new DockPanel { Margin=new Thickness(12,0,12,10) }; HeaderControls=header;
        var refresh=Theme.Button("↻",Refresh,"Todo Mate 새로고침"); DockPanel.SetDock(refresh,Dock.Right); header.Children.Add(refresh);
        addButton=Theme.Button("+",ChooseCategory,"할 일 추가"); DockPanel.SetDock(addButton,Dock.Right); header.Children.Add(addButton);
        loginButton=Theme.Button("계정 연결",OpenLogin,"Firebase 계정 연결"); DockPanel.SetDock(loginButton,Dock.Right); header.Children.Add(loginButton);
        var brand=new DockPanel { VerticalAlignment=VerticalAlignment.Center };
        var logo=CreateLogo();
        DockPanel.SetDock(logo,Dock.Left); brand.Children.Add(logo);
        var brandTitle=Theme.Text("Todo Mate",18,true); brandTitle.Foreground=Brushes.Black; brandTitle.Effect=null; brand.Children.Add(brandTitle);
        header.Children.Add(brand);
        var navigation=new DockPanel(); Grid.SetRow(navigation,0); layout.Children.Add(navigation);
        var next=Theme.Button("›",delegate { select(selected.AddDays(7)); },"다음 주 할 일"); DockPanel.SetDock(next,Dock.Right); navigation.Children.Add(next);
        var previous=Theme.Button("‹",delegate { select(selected.AddDays(-7)); },"이전 주 할 일"); DockPanel.SetDock(previous,Dock.Left); navigation.Children.Add(previous); navigation.Children.Add(dateTitle);
        week.Margin=new Thickness(0,4,0,10); Grid.SetRow(week,1); layout.Children.Add(week);
        scroll=new ScrollViewer { Content=list, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll,2); layout.Children.Add(scroll);
        status.TextWrapping=TextWrapping.Wrap; status.Margin=new Thickness(0,8,0,0); Grid.SetRow(status,3); layout.Children.Add(status);
        timer.Tick+=delegate { if(syncMinutes>0 && DateTime.UtcNow>=nextSync) Refresh(); };
        if(connect) Loaded+=async delegate {
            if(disposed) return;
            try { CheckAccount(); TryCached(); Render(); if(client.Connected) { await Reload(false); timer.Start(); } else { status.Text="계정 연결을 누르거나 daydesk todo login을 실행해 주세요."; } }
            catch { status.Text="Todo Mate 계정 연결 정보를 읽지 못했습니다."; }
        };
        if(!connect) status.Text="디자인 검증용 샘플 할 일";
        if(connect) { client=new MateClient(MateClient.Folder); TodoSnapshots.AccountKey=client.AccountKey; TryCached(); }
        Render();
    }
    void ReadPreferences() {
        try {
            string path=File.Exists(preferencesFile) ? preferencesFile : Path.Combine(accountFolder,"window.json");
            if(!File.Exists(path)) return;
            var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
            object value;
            if(d.TryGetValue("HiddenGoals",out value) && value is IEnumerable) foreach(var id in (IEnumerable)value) hidden.Add(Convert.ToString(id));
            if(d.TryGetValue("ShowAllCategories",out value) && value is bool) showAllCategories=(bool)value;
        } catch { status.Text="카테고리 표시 설정을 기본값으로 시작합니다."; }
    }
    void SavePreferences() {
        if(!connect) return;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(preferencesFile)); string temp=preferencesFile+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(new { HiddenGoals=hidden.ToArray(),ShowAllCategories=showAllCategories }));
            if(File.Exists(preferencesFile)) File.Replace(temp,preferencesFile,null); else File.Move(temp,preferencesFile);
        } catch { status.Text="카테고리 표시 설정을 저장하지 못했습니다."; }
    }
    Brush ColorFor(string id) { int index=data==null ? 0 : data.Goals.FindIndex(g=>g.Id==id); return Theme.Brush(palette[Math.Max(0,index)%palette.Length]); }
    bool Visible(Todo todo) { return data!=null && !hidden.Contains(todo.GoalId) && data.Goals.Any(g=>g.Id==todo.GoalId); }
    internal List<Todo> VisibleTodos { get { return data==null ? new List<Todo>() : data.Todos.Where(t=>Visible(t) && t.Date==selected).ToList(); } }
    internal IEnumerable<Goal> OccupiedCategories { get { return data==null ? Enumerable.Empty<Goal>() : data.Goals.Where(g=>data.Todos.Any(t=>t.GoalId==g.Id && t.Date==selected)); } }
    internal IEnumerable<Goal> ListedCategories { get { return (data==null ? Enumerable.Empty<Goal>() : showAllCategories ? data.Goals : OccupiedCategories).Where(g=>!hidden.Contains(g.Id)); } }
    bool Current { get { return data!=null && selected>=loadedStart && selected<=loadedEnd; } }
    internal void SetSyncMinutes(int minutes) { syncMinutes=minutes; nextSync=(syncedAt==default(DateTime) ? DateTime.UtcNow : syncedAt).AddMinutes(Math.Max(1,minutes)); }
    bool TryCached() {
        var saved=TodoSnapshots.Read(selected); if(saved==null) return false;
        ApplyData(saved.Data,saved.Start,saved.End); syncedAt=saved.Updated;
        nextSync=syncedAt.AddMinutes(Math.Max(1,syncMinutes));
        status.Text=saved.Data.Source=="mcp" ? "MCP 보조 조회" : ""; return true;
    }
    internal void ReloadCached() { if(inlineInput!=null) { pending=true; return; } if(connect) CheckAccount(); TryCached(); Render(); }
    void CheckAccount() {
        if(client!=null && client.Connected) return;
        if(client!=null) client.Dispose();
        client=new MateClient(MateClient.Folder);
        if(TodoSnapshots.AccountKey!=client.AccountKey) { data=null; dataSignature=""; loadedStart=loadedEnd=default(DateTime); }
        TodoSnapshots.AccountKey=client.AccountKey;
    }
    internal void SelectDate(DateTime day) {
        if(selected==day.Date) return;
        selected=day.Date; if(connect) TryCached(); Render();
        if(connect && !Current) { var ignored=Reload(false); }
    }
    internal void ApplyData(WeekData updated,DateTime start,DateTime? end=null) {
        DateTime last=(end ?? start.AddDays(6)).Date; string signature=TodoSnapshots.Payload(updated,start.Date,last);
        if(data!=null && signature==dataSignature) return;
        data=updated; loadedStart=start.Date; loadedEnd=last;
        dataSignature=signature; Render();
    }
    internal void SetCategory(string id,bool visible) {
        if(visible) hidden.Remove(id); else hidden.Add(id);
        RenderWeek(); RenderList(); SavePreferences();
    }
    internal void SetAllCategories(bool value) { showAllCategories=value; RenderList(); SavePreferences(); }
    internal Dictionary<string,object> PreferenceValues() { return new Dictionary<string,object> { {"ShowAllCategories",showAllCategories},{"HiddenGoals",hidden.ToArray()} }; }
    internal void RestorePreferences(Dictionary<string,object> value) {
        object all,excluded;
        if(!value.TryGetValue("ShowAllCategories",out all) || !(all is bool) || !value.TryGetValue("HiddenGoals",out excluded) || !(excluded is IEnumerable) || excluded is string)
            throw new ArgumentException("추천 카테고리 설정을 읽지 못했습니다.");
        var ids=((IEnumerable)excluded).Cast<object>().ToArray();
        if(ids.Any(id=>!(id is string))) throw new ArgumentException("추천 카테고리 설정이 올바르지 않습니다.");
        hidden.Clear(); foreach(string id in ids) hidden.Add(id); showAllCategories=(bool)all;
        Render(); SavePreferences();
    }
    internal void Refresh() {
        if(!connect || disposed) return;
        if(inlineInput!=null) { pending=pendingForce=true; status.Text="입력을 저장하거나 취소하면 새로고침합니다."; return; }
        if(busy) { pending=pendingForce=true; return; }
        try { CheckAccount(); Render(); } catch { status.Text="Todo Mate 계정 연결을 확인해 주세요."; return; }
        var ignored=Reload(true);
    }
    internal void ReportReadError(string message) {
        Render(); status.Text=message;
    }
    async Task Reload(bool force) {
        if(inlineInput!=null) { pending=true; pendingForce|=force; return; }
        if(!force && TryCached()) return;
        nextSync=DateTime.UtcNow.AddMinutes(Math.Max(1,syncMinutes));
        if(disposed || client==null || !client.Connected) { status.Text="계정 연결을 누르거나 daydesk todo login을 실행해 주세요."; return; }
        if(busy) { pending=true; pendingForce|=force; return; }
        busy=true; status.Text="할 일 동기화 중…"; DateTime requested=selected;
        DateTime start=MateClient.PeriodStart(requested,true),end=MateClient.PeriodEnd(requested,true);
        try {
          using(await TodoSnapshots.Enter()) {
            if(disposed) return;
            var updated=await client.LoadRange(start,end);
            if(!disposed) {
                TodoSnapshots.SaveMonth(updated,requested,start,end);
                if(selected>=start && selected<=end) ApplyData(updated,start,end);
                syncedAt=DateTime.UtcNow; timer.Start(); status.Text=(updated.Source=="mcp" ? updated.Notice+" · " : "동기화 ")+DateTime.Now.ToString("HH:mm")+(syncMinutes==0 ? " · 수동 새로고침" : " · "+syncMinutes+"분마다 확인");
            }
          }
        } catch(Exception error) { if(!disposed) ReportReadError(error.Message); }
        finally { busy=false; }
        if(!disposed && (pending || selected<start || selected>end)) { bool again=pendingForce; pending=pendingForce=false; await Reload(again); }
    }
    void Render() { loginButton.Visibility=connect && (client==null || !client.Connected) ? Visibility.Visible : Visibility.Collapsed; addButton.IsEnabled=Current && client!=null && client.Connected; RenderCategories(); RenderWeek(); RenderList(); }
    void RenderCategories() {
        categories.Children.Clear();
        categories.Visibility=data!=null && data.Goals.Count>0 ? Visibility.Visible : Visibility.Collapsed;
        if(data==null) return;
        var toggle=CategorySwitch("전체 카테고리",showAllCategories,SetAllCategories,Theme.Green);
        AutomationProperties.SetName(toggle,"전체 카테고리 표시");
        toggle.ToolTip="켬: 전체 카테고리 · 끔: 선택한 날짜에 할 일이 있는 카테고리만\n설정에서 끈 카테고리는 항상 숨깁니다.";
        categories.Children.Add(toggle);
    }
    internal FrameworkElement CategoryPreferences() {
        var panel=new StackPanel();
        if(data==null || data.Goals.Count==0) { panel.Children.Add(Theme.Text("카테고리를 불러온 뒤 설정하실 수 있습니다.",12)); return panel; }
        foreach(var goal in data.Goals) {
            var target=goal;
            var toggle=CategorySwitch(string.IsNullOrWhiteSpace(goal.Title) ? "기본" : goal.Title,!hidden.Contains(goal.Id),visible=>SetCategory(target.Id,visible),ColorFor(goal.Id));
            toggle.Tag=goal.Id; toggle.HorizontalAlignment=HorizontalAlignment.Stretch;
            panel.Children.Add(toggle);
        }
        return panel;
    }
    internal static ToggleButton CategorySwitch(string label,bool enabled,Action<bool> change,Brush accent) {
            var toggle=new ToggleButton { IsChecked=enabled,ToolTip=label };
            Theme.StyleButton(toggle,label+" 카테고리 표시"); toggle.Padding=new Thickness(10,8,10,8); toggle.FontSize=12; toggle.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            row.Children.Add(new TextBlock { Text=label,MaxWidth=200,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center });
            var state=new TextBlock { Margin=new Thickness(12,0,8,0),VerticalAlignment=VerticalAlignment.Center,FontSize=10 }; Grid.SetColumn(state,1); row.Children.Add(state);
            var thumb=new Border { Width=12,Height=12,CornerRadius=new CornerRadius(6),Margin=new Thickness(2),VerticalAlignment=VerticalAlignment.Center };
            var track=new Border { Width=32,Height=18,CornerRadius=new CornerRadius(9),Child=thumb,VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(track,2); row.Children.Add(track); toggle.Content=row;
            Action update=delegate {
                bool on=toggle.IsChecked==true; state.Text=on ? "켬" : "끔";
                track.Background=on ? accent : Theme.Brush("#637989");
                thumb.HorizontalAlignment=on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                thumb.Background=on ? Theme.Brush("#102A3E") : Theme.Ink;
                toggle.Background=Theme.Brush(on ? "#20FFFFFF" : "#08000000");
            };
            toggle.Checked+=delegate { update(); }; toggle.Unchecked+=delegate { update(); }; update();
            toggle.Click+=delegate { change(toggle.IsChecked==true); }; return toggle;
    }
    void RenderWeek() {
        dateTitle.Text=selected.ToString("M월 d일 (ddd)"); week.Children.Clear(); DateTime start=MateClient.WeekStart(selected);
        for(int i=0;i<7;i++) {
            DateTime day=start.AddDays(i); var items=data==null ? new List<Todo>() : data.Todos.Where(t=>t.Date==day && Visible(t)).ToList();
            var button=Theme.Button("",delegate { select(day); },day.ToString("M월 d일")+" 할 일"); button.Padding=new Thickness(0,4,0,4); button.Margin=new Thickness(1); button.Background=day==selected ? Theme.Brush("#402ECC87") : Brushes.Transparent;
            var stack=new StackPanel(); var name=Theme.Text("일월화수목금토"[i].ToString(),10); name.HorizontalAlignment=HorizontalAlignment.Center;
            var number=Theme.Text(day.Day.ToString(),13,true); number.HorizontalAlignment=HorizontalAlignment.Center;
            var count=Theme.Text(items.Count==0 ? "·" : items.Count(t=>t.Done)+"/"+items.Count,10); count.HorizontalAlignment=HorizontalAlignment.Center;
            stack.Children.Add(name); stack.Children.Add(number); stack.Children.Add(count); button.Content=stack; week.Children.Add(button);
        }
    }
    void RenderList() {
        double offset=scroll.VerticalOffset; list.Children.Clear();
        if(!Current) { var message=Theme.Text(data==null ? "할 일을 불러오고 있습니다." : "선택한 기간의 할 일을 불러오고 있습니다.",12); message.TextWrapping=TextWrapping.Wrap; list.Children.Add(message); return; }
        var items=VisibleTodos;
        var groups=ListedCategories.ToList();
        if(HasInlineEditor && groups.All(g=>g.Id!=inlineGoal.Id)) groups.Add(inlineGoal);
        foreach(var goal in groups) {
            var target=goal; var heading=new DockPanel { Margin=new Thickness(0,10,0,6) };
            var add=Theme.Button("+",delegate { BeginInline(null,target); },goal.Title+"에 할 일 추가"); add.Background=Brushes.Transparent; add.IsEnabled=client!=null && client.Connected; DockPanel.SetDock(add,Dock.Right); heading.Children.Add(add);
            var title=new TextBlock { Text=string.IsNullOrWhiteSpace(goal.Title) ? "기본" : goal.Title, Foreground=Theme.Brush("#13272C"), FontSize=12, FontWeight=FontWeights.SemiBold, TextTrimming=TextTrimming.CharacterEllipsis };
            heading.Children.Add(new Border { Child=title, Background=ColorFor(goal.Id), CornerRadius=new CornerRadius(12),Padding=new Thickness(10,5,10,5),VerticalAlignment=VerticalAlignment.Center }); list.Children.Add(heading);
            foreach(var todo in items.Where(t=>t.GoalId==goal.Id)) {
                bool editing=HasInlineEditor && inlineTodo!=null && inlineTodo.Id==todo.Id;
                var targetTodo=todo; var row=new DockPanel { Margin=new Thickness(0,0,0,4) };
                Button edit=null; edit=Theme.Button("···",delegate { ShowActions(targetTodo,target,edit); },todo.Content+" 작업"); edit.Background=Brushes.Transparent; edit.Padding=new Thickness(4); DockPanel.SetDock(edit,Dock.Right); row.Children.Add(edit);
                var done=new CheckBox { IsChecked=todo.Done, VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,2,6,0),IsEnabled=client!=null };
                AutomationProperties.SetName(done,todo.Content+" 완료 상태");
                done.Click+=async delegate { bool desired=done.IsChecked==true; done.IsChecked=targetTodo.Done; await SetDone(targetTodo,desired); };
                DockPanel.SetDock(done,Dock.Left); row.Children.Add(done);
                if(editing) { edit.IsHitTestVisible=done.IsHitTestVisible=false; edit.Focusable=done.Focusable=false; row.Children.Add(inlineEditor); list.Children.Add(row); continue; }
                var content=Theme.Text(todo.Content,13); content.TextWrapping=TextWrapping.Wrap; content.Margin=new Thickness(0,4,0,4); if(todo.Done) content.TextDecorations=TextDecorations.Strikethrough;
                content.Focusable=true; content.ToolTip="더블클릭 또는 F2로 내용 수정";
                content.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) { if(e.ClickCount==2) { BeginInline(targetTodo,target); e.Handled=true; } };
                content.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.F2 || e.Key==Key.Enter) { BeginInline(targetTodo,target); e.Handled=true; } };
                row.Children.Add(content); list.Children.Add(row);
            }
            if(HasInlineEditor && inlineGoal.Id==goal.Id && (inlineTodo==null || items.All(t=>t.Id!=inlineTodo.Id))) list.Children.Add(inlineEditor);
        }
        if(groups.Count==0) { var empty=Theme.Text("표시할 할 일이 없습니다.",12); empty.Margin=new Thickness(0,10,0,0); list.Children.Add(empty); }
        scroll.ScrollToVerticalOffset(offset);
    }
    internal static ContextMenu ActionMenu(FrameworkElement anchor) {
        var menu=new ContextMenu { Background=Theme.Brush("#F2102A3E"),Foreground=Theme.Ink,Padding=new Thickness(5),MinWidth=200,MaxWidth=300,PlacementTarget=anchor,Placement=PlacementMode.Custom,HasDropShadow=true };
        menu.CustomPopupPlacementCallback=delegate(Size popup,Size target,Point offset) {
            return new[] { new CustomPopupPlacement(new Point(target.Width-popup.Width,target.Height+4),PopupPrimaryAxis.Vertical),new CustomPopupPlacement(new Point(target.Width-popup.Width,-popup.Height-4),PopupPrimaryAxis.Vertical) };
        };
        var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));
        border.SetValue(Border.BorderBrushProperty,Theme.Line); border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        border.SetBinding(Border.BackgroundProperty,new Binding("Background") { RelativeSource=RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty,new Binding("Padding") { RelativeSource=RelativeSource.TemplatedParent });
        var scroll=new FrameworkElementFactory(typeof(ScrollViewer)); scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Auto); scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter))); border.AppendChild(scroll);
        menu.Template=new ControlTemplate(typeof(ContextMenu)) { VisualTree=border }; return menu;
    }
    internal static MenuItem ActionItem(string label,bool danger=false) {
        var item=new MenuItem { Header=label,ToolTip=label,Foreground=danger ? Theme.Brush("#FFADB3") : Theme.Ink,FontFamily=new FontFamily("Malgun Gothic"),FontSize=13,Padding=new Thickness(12,8,12,8),Margin=new Thickness(1),HorizontalContentAlignment=HorizontalAlignment.Stretch };
        AutomationProperties.SetName(item,label);
        var border=new FrameworkElementFactory(typeof(Border),"Row"); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(6)); border.SetValue(Border.BackgroundProperty,Brushes.Transparent);
        border.SetBinding(Border.PaddingProperty,new Binding("Padding") { RelativeSource=RelativeSource.TemplatedParent });
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.ContentSourceProperty,"Header"); presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center); border.AppendChild(presenter);
        var template=new ControlTemplate(typeof(MenuItem)) { VisualTree=border };
        var highlight=new Trigger { Property=MenuItem.IsHighlightedProperty,Value=true };
        highlight.Setters.Add(new Setter(Border.BackgroundProperty,Theme.Brush(danger ? "#45D65060" : "#30FFFFFF"),"Row")); template.Triggers.Add(highlight);
        var disabled=new Trigger { Property=UIElement.IsEnabledProperty,Value=false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.45)); template.Triggers.Add(disabled);
        item.Template=template; return item;
    }
    void ChooseCategory() {
        if(!Current || busy || client==null) return;
        var menu=ActionMenu(addButton);
        foreach(var goal in data.Goals) {
            var target=goal; var item=ActionItem(string.IsNullOrWhiteSpace(goal.Title) ? "기본" : goal.Title);
            item.Click+=delegate { BeginInline(null,target); }; menu.Items.Add(item);
        }
        if(menu.Items.Count==0) { status.Text="Todo Mate에서 추가할 카테고리를 먼저 만들어 주세요."; return; }
        menu.IsOpen=true;
    }
    void ShowActions(Todo todo,Goal goal,FrameworkElement anchor) {
        var menu=ActionMenu(anchor);
        var change=ActionItem("수정"); change.IsEnabled=client!=null && client.Connected;
        change.Click+=delegate { BeginInline(todo,goal); }; menu.Items.Add(change);
        var postpone=ActionItem(todo.Date.Date==DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date ? "내일로 미루기" : "다음 날로 미루기"); postpone.IsEnabled=client!=null && client.Connected;
        postpone.Click+=async delegate { await Postpone(todo); }; menu.Items.Add(postpone);
        var details=ActionItem("날짜·카테고리·알림"); details.IsEnabled=client!=null && client.Connected;
        details.Click+=async delegate { await Edit(todo,goal); }; menu.Items.Add(details);
        var remove=ActionItem("삭제",true); remove.IsEnabled=client!=null && client.Connected; remove.Click+=async delegate { await Delete(todo); }; menu.Items.Add(remove);
        menu.IsOpen=true;
    }
    void OpenLogin() {
        if(ConnectRequested!=null) { ConnectRequested(); return; }
        try { Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"FirebaseAccount.exe")) { UseShellExecute=true }); refreshAfterBrowser=true; }
        catch { status.Text="Firebase 계정 연결 창을 열지 못했습니다."; }
    }
    void InlineFocus(bool focus) {
        var owner=Window.GetWindow(this) as CalendarWindow;
        if(owner!=null) owner.InlineInputChanged(focus);
        if(focus && inlineInput!=null) Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(delegate {
            if(inlineInput==null || !HasInlineEditor) return;
            inlineInput.Focus(); Keyboard.Focus(inlineInput); inlineInput.CaretIndex=inlineInput.Text.Length; inlineInput.BringIntoView();
        }));
    }
    internal void BeginInline(Todo todo,Goal goal) {
        if(busy || disposed || client==null || !client.Connected || !Current) return;
        if(inlineInput!=null) { InlineFocus(true); status.Text="작성 중인 내용을 저장하거나 취소해 주세요."; return; }
        inlineTodo=todo; inlineGoal=goal; inlineDate=selected; inlineUncertain=false; timer.Stop();
        inlineInput=new TextBox { Text=todo==null ? "" : todo.Content,FontSize=13,MinLines=1,MaxLines=3,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            Padding=new Thickness(0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),VerticalAlignment=VerticalAlignment.Center };
        inlineInput.SetResourceReference(Control.ForegroundProperty,"WidgetInk"); inlineInput.SetResourceReference(TextBox.CaretBrushProperty,"WidgetInk"); inlineInput.SetResourceReference(UIElement.EffectProperty,"WidgetTextEffect");
        if(todo!=null && todo.Done) inlineInput.TextDecorations=TextDecorations.Strikethrough;
        var host=new FrameworkElementFactory(typeof(ScrollViewer),"PART_ContentHost");
        inlineInput.Template=new ControlTemplate(typeof(TextBox)) { VisualTree=host };
        AutomationProperties.SetName(inlineInput,todo==null ? "새 할 일 내용" : "목록에서 할 일 내용 수정");
        inlineInput.ToolTip="Enter 저장 · Shift+Enter 줄바꿈 · Esc 취소\n다른 곳을 클릭해도 저장됩니다.";
        inlineEditor=new Border { Child=inlineInput,Margin=todo==null ? new Thickness(25,4,28,3) : new Thickness(0,4,0,3),BorderBrush=Theme.Brush("#802ECC87"),BorderThickness=new Thickness(0,0,0,1) };
        var editor=inlineEditor;
        inlineInput.PreviewKeyDown+=async delegate(object sender,KeyEventArgs e) {
            if(e.Key==Key.Escape) { e.Handled=true; CloseInline(); }
            else if(e.Key==Key.Enter && (Keyboard.Modifiers&ModifierKeys.Shift)==0) { e.Handled=true; if(!e.IsRepeat) await SaveInline(todo==null); }
        };
        inlineInput.LostKeyboardFocus+=delegate {
            Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(async delegate {
                if(inlineEditor!=editor || busy || disposed || inlineUncertain || editor.IsKeyboardFocusWithin) return;
                if(string.IsNullOrWhiteSpace(inlineInput.Text)) CloseInline(); else await SaveInline(false);
            }));
        };
        RenderList(); InlineFocus(true); status.Text="Enter 저장 · Shift+Enter 줄바꿈 · Esc 취소";
    }
    internal void CloseInline() {
        if(busy || inlineInput==null) return;
        bool uncertain=inlineUncertain;
        inlineInput=null; inlineEditor=null; inlineTodo=null; inlineGoal=null; inlineUncertain=false;
        InlineFocus(false); Render(); if(connect) timer.Start();
        if(uncertain) pending=pendingForce=true;
        if(pending && connect && !disposed) { bool force=pendingForce; pending=pendingForce=false; var ignored=Reload(force); }
    }
    internal async Task SaveInline(bool keepAdding) {
        if(busy || disposed || inlineInput==null || inlineUncertain) return;
        string content=inlineInput.Text.Trim();
        if(content=="") { status.Text="할 일 내용을 입력해 주세요."; InlineFocus(true); return; }
        if(inlineTodo!=null && content==inlineTodo.Content) { CloseInline(); return; }
        var editor=inlineEditor; var todo=inlineTodo; var goal=inlineGoal; var day=inlineDate;
        busy=mutating=true; list.IsEnabled=false; status.Text="저장 중…";
        bool saved=false; string warning=null;
        try {
            using(await TodoSnapshots.Enter()) {
                var updated=todo==null ? await client.Add(goal.Id,content,day) : await client.Update(todo.Id,content:content);
                saved=true; updated.Category=goal.Title;
                int index=data.Todos.FindIndex(t=>t.Id==updated.Id); if(index<0) data.Todos.Add(updated); else data.Todos[index]=updated;
                try { TodoSnapshots.Patch(updated); if(TodoSnapshots.Read(updated.Date)==null) TodoSnapshots.SaveDay(await client.LoadRange(updated.Date,updated.Date),updated.Date); }
                catch { warning="서버 저장은 완료되었습니다. 캐시 갱신을 위해 새로고침해 주세요."; }
            }
        } catch(Exception error) {
            inlineUncertain=true; inlineInput.IsReadOnly=true;
            status.Text="Esc로 닫고 새로고침하여 저장 여부를 확인해 주세요. "+error.Message;
        } finally { busy=mutating=false; if(!disposed) list.IsEnabled=true; }
        if(disposed || !saved || inlineEditor!=editor) return;
        if(keepAdding && todo==null && selected==day) { inlineInput.Clear(); Render(); InlineFocus(true); }
        else CloseInline();
        status.Text=warning ?? "저장했습니다.";
    }
    internal async Task Postpone(Todo todo) {
        if(busy || disposed || inlineInput!=null || client==null || !client.Connected) return;
        DateTime next=todo.Date.AddDays(1),when; DateTime? reminder=null;
        bool hasReminder=!string.IsNullOrWhiteSpace(todo.Reminder);
        if(hasReminder) {
            if(!DateTime.TryParseExact(todo.Reminder,"yyyy-MM-ddTHH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out when)) { status.Text="알림 시간을 확인한 뒤 다시 미뤄 주세요."; return; }
            reminder=when.AddDays(1);
        }
        busy=mutating=true; list.IsEnabled=false; string warning=null;
        try {
            using(await TodoSnapshots.Enter()) {
                var updated=await client.Update(todo.Id,date:next,changeReminder:hasReminder,reminder:reminder);
                var goal=data.Goals.FirstOrDefault(g=>g.Id==updated.GoalId); updated.Category=goal==null ? todo.Category : goal.Title;
                data.Todos.RemoveAll(t=>t.Id==updated.Id); data.Todos.Add(updated);
                try { TodoSnapshots.Patch(updated); if(TodoSnapshots.Read(updated.Date)==null) TodoSnapshots.SaveDay(await client.LoadRange(updated.Date,updated.Date),updated.Date); }
                catch { warning="서버 이동은 완료되었습니다. 캐시 갱신을 위해 새로고침해 주세요."; }
            }
            Render(); status.Text=warning ?? next.ToString("M월 d일")+"로 미뤘습니다.";
        } catch(Exception error) { status.Text="새로고침으로 이동 여부를 확인해 주세요. "+error.Message; }
        finally { busy=mutating=false; if(!disposed) list.IsEnabled=true; }
        if(pending && !disposed) { bool force=pendingForce; pending=pendingForce=false; await Reload(force); }
    }
    async Task Delete(Todo todo) {
        if(busy || client==null || disposed) return;
        if(Forms.MessageBox.Show("이 할 일을 Todo Mate에서도 삭제하시겠습니까?\n\n"+todo.Content,"할 일 삭제",Forms.MessageBoxButtons.YesNo,Forms.MessageBoxIcon.Question,Forms.MessageBoxDefaultButton.Button2)!=Forms.DialogResult.Yes) return;
        busy=mutating=true; list.IsEnabled=false;
        try {
            using(await TodoSnapshots.Enter()) {
                await client.Delete(todo.Id); data.Todos.RemoveAll(t=>t.Id==todo.Id); ApplyData(data,loadedStart,loadedEnd);
                try { TodoSnapshots.Remove(todo.Id); status.Text="삭제했습니다."; }
                catch { status.Text="서버 삭제는 완료되었습니다. 캐시 갱신을 위해 새로고침해 주세요."; }
            }
        } catch(Exception error) { status.Text=error.Message; }
        finally { busy=mutating=false; if(!disposed) list.IsEnabled=true; }
        if(pending && !disposed) { bool force=pendingForce; pending=pendingForce=false; await Reload(force); }
    }
    internal void RefreshAfterBrowser() {
        if(!refreshAfterBrowser || disposed) return;
        refreshAfterBrowser=false; Refresh();
    }
    async Task SetDone(Todo todo,bool value) {
        if(busy || client==null || disposed) return;
        busy=mutating=true; list.IsEnabled=false; status.Text="완료 상태 저장 중…";
        try {
          using(await TodoSnapshots.Enter()) {
            var updated=await client.SetDone(todo.Id,value); todo.Done=updated.Done; todo.Reminder=updated.Reminder;
            if(!disposed) { ApplyData(data,loadedStart,loadedEnd); status.Text="완료 상태를 저장했습니다."; TodoSnapshots.Patch(todo); }
          }
        } catch(Exception error) { if(!disposed) status.Text=error.Message; }
        finally { busy=mutating=false; if(!disposed) list.IsEnabled=true; }
        if(pending && !disposed) { bool force=pendingForce; pending=pendingForce=false; await Reload(force); }
    }
    async Task Edit(Todo todo,Goal goal) {
        if(busy || client==null || !Current || disposed) return;
        busy=mutating=true; timer.Stop();
        try {
            var owner=Window.GetWindow(this);
            var dialog=new TodoDialog(todo,goal,selected,client,data.Goals) { Owner=owner };
                if(dialog.ShowDialog()==true) {
                    select(dialog.SavedDate); TryCached(); Render();
                    if(!String.IsNullOrEmpty(dialog.SaveWarning)) ReportReadError(dialog.SaveWarning);
                }
                else if(dialog.Attempted) pending=pendingForce=true;
        } catch(Exception error) { status.Text="저장 결과를 확인해 주세요. "+error.Message; }
        finally { busy=mutating=false; if(!disposed) timer.Start(); }
        if(pending && !disposed) { bool force=pendingForce; pending=pendingForce=false; await Reload(force); }
    }
    public void Dispose() { disposed=true; timer.Stop(); if(client!=null) client.Dispose(); }
}
