using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Runtime.InteropServices;
using D = System.Collections.Generic.Dictionary<string, object>;

public partial class CalendarWindow : Window {
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static string SettingsFile = System.IO.Path.Combine(AppPaths.DataRoot,"settings.json");
    static readonly Color Ink = (Color)ColorConverter.ConvertFromString("#F4F8FC");
    static readonly Color DarkInk = (Color)ColorConverter.ConvertFromString("#17232E");
    static readonly Color Green = (Color)ColorConverter.ConvertFromString("#2ECC87");
    static readonly Brush Line = Brush("#25FFFFFF");
    readonly Border plate = new Border();
    readonly Grid calendar = new Grid();
    readonly Grid mainArea=new Grid();
    readonly ColumnDefinition calendarColumn=new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star),MinWidth=420 };
    readonly ColumnDefinition todoColumn=new ColumnDefinition { Width=new GridLength(300),MinWidth=260,MaxWidth=600 };
    readonly TodoPanel todoPanel;
    readonly TextBlock period = Text("", 25, true), status = Text("연결 중…", 11, false);
    readonly ComboBox calendars = new ComboBox();
    readonly DispatcherTimer autosync = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
    readonly DispatcherTimer saveDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
    readonly TextBlock brand = Text("TimeTree", 20, true);
    readonly string python;
    readonly bool live;
    D prefs = new D(), model;
    List<D> calendarList = new List<D>();
    readonly Dictionary<DateTime,Button> dateButtons=new Dictionary<DateTime,Button>();
    DateTime anchor = Today(), selected = Today(), lastToday = Today();
    DateTime? expandedWeekStart;
    const string view = "month";
    string calendarId = "";
    int generation;
    int syncMinutes=30;
    DateTime nextSync=DateTime.UtcNow.AddMinutes(30);
    bool backendReady, selectingCalendar, pendingForce;
    string paintedModel="";
    double backgroundTransparency = 65;
    double todoWidth=300;
    Process backend;
    WidgetCommands commands;
    Window settings;
    Action<bool> changeSettingsTab;
    bool closing, loading, awaitingInitialConnections;
    bool pinBack;
    ToggleButton backSwitch;
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    void PlaceBehind() {
        if(!pinBack || closing || !IsLoaded || OwnedWindows.Cast<Window>().Any(w=>w.IsVisible)) return;
        var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if(handle!=IntPtr.Zero) SetWindowPos(handle,new IntPtr(1),0,0,0,0,0x0013);
    }
    void SetWindowLayer(bool above,bool below) {
        if(above && below) throw new ArgumentException("항상 위와 가장 뒤 고정은 동시에 켤 수 없습니다.");
        bool wasBack=pinBack; pinBack=below; Topmost=above;
        if(backSwitch!=null) backSwitch.IsChecked=below;
        if(wasBack && !below && IsLoaded) {
            var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
            SetWindowPos(handle,new IntPtr(above ? -1 : -2),0,0,0,0,0x0013);
        }
        PlaceBehind(); ScheduleSave();
    }

    static string S(D d, string key, string fallback = "") { object o; return d.TryGetValue(key, out o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : fallback; }
    static double N(D d, string key, double fallback = 0) { double n; return Double.TryParse(S(d,key), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !Double.IsNaN(n) && !Double.IsInfinity(n) ? n : fallback; }
    static bool B(D d, string key) { object o; return d.TryGetValue(key,out o) && o is bool && (bool)o; }
    static object[] A(D d, string key) { object o; return d.TryGetValue(key,out o) && o is IEnumerable && !(o is string) ? ((IEnumerable)o).Cast<object>().ToArray() : new object[0]; }
    static DateTime Today() { return DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date; }
    static DateTime Start(D e) { return DateTimeOffset.Parse(S(e,"start"),CultureInfo.InvariantCulture).DateTime; }
    static DateTime End(D e) { return DateTimeOffset.Parse(S(e,"end"),CultureInfo.InvariantCulture).DateTime; }
    static SolidColorBrush Brush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    static TextBlock Text(string value, double size, bool bold) { return new TextBlock { Text=value, FontSize=size, Foreground=new SolidColorBrush(Ink), FontWeight=bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment=VerticalAlignment.Center, TextTrimming=TextTrimming.CharacterEllipsis, Effect=new DropShadowEffect { Color=Colors.Black, BlurRadius=3, ShadowDepth=0, Opacity=.9 } }; }
    static Button Btn(string label, Action action) {
        var button = new Button { Content=label, Padding=new Thickness(11,7,11,7), Margin=new Thickness(2), Background=Brush("#18FFFFFF"), Foreground=new SolidColorBrush(Ink), BorderBrush=Brush("#00000000"), BorderThickness=new Thickness(1), Cursor=Cursors.Hand, FontSize=13, HorizontalContentAlignment=HorizontalAlignment.Center };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));
        border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background") { RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush") { RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty,new System.Windows.Data.Binding("BorderThickness") { RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding("Padding") { RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty,new System.Windows.Data.Binding("HorizontalContentAlignment") { RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent });
        border.AppendChild(presenter);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree=border };
        button.MouseEnter += delegate { button.BorderBrush = new SolidColorBrush(Green); };
        button.MouseLeave += delegate { if(!button.IsKeyboardFocused) button.BorderBrush=Brush("#00000000"); };
        button.GotKeyboardFocus += delegate { button.BorderBrush=new SolidColorBrush(Green); };
        button.LostKeyboardFocus += delegate { button.BorderBrush=Brush("#00000000"); };
        button.Click += delegate { action(); };
        AutomationProperties.SetName(button,label);
        return button;
    }

    internal static void StyleCalendarSelector(ComboBox calendars,bool light=false) {
        calendars.Foreground=new SolidColorBrush(light ? DarkInk : Ink); calendars.Background=Brush(light ? "#F7F8F9" : "#08FFFFFF"); calendars.BorderBrush=Brush(light ? "#DDE2E5" : "#30FFFFFF");
        AutomationProperties.SetName(calendars,"캘린더 선택");
        string template=@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBox}'>
 <Grid>
  <ToggleButton Focusable='False' ClickMode='Press' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
   <ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'>
    <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6'/>
    <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#2ECC87'/></Trigger></ControlTemplate.Triggers>
   </ControlTemplate></ToggleButton.Template>
  </ToggleButton>
  <ContentPresenter Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='10,0,28,0' VerticalAlignment='Center' IsHitTestVisible='False'/>
  <TextBlock Text='⌄' Foreground='{TemplateBinding Foreground}' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,9,2' IsHitTestVisible='False'/>
  <Popup x:Name='PART_Popup' Placement='Bottom' AllowsTransparency='True' Focusable='False' StaysOpen='False' MinWidth='{TemplateBinding ActualWidth}' IsOpen='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
   <Border Background='#A0102A3E' BorderBrush='#50C7E3F8' BorderThickness='1' CornerRadius='6' Padding='4' Margin='0,3,0,0'>
    <ScrollViewer MaxHeight='240' CanContentScroll='True' VerticalScrollBarVisibility='Auto'><ItemsPresenter x:Name='ItemsPresenter'/></ScrollViewer>
   </Border>
  </Popup>
 </Grid>
</ControlTemplate>";
        if(light) template=template.Replace("#A0102A3E","#FFFFFFFF").Replace("#50C7E3F8","#DDE2E5");
        calendars.Template=(ControlTemplate)XamlReader.Parse(template);
        string itemStyle=@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBoxItem}'>
 <Setter Property='Foreground' Value='#F4F8FC'/><Setter Property='Padding' Value='9,6'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ComboBoxItem}'>
  <Border x:Name='Item' Background='Transparent' CornerRadius='4' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border>
  <ControlTemplate.Triggers>
   <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Item' Property='Background' Value='#402ECC87'/></Trigger>
   <Trigger Property='IsSelected' Value='True'><Setter TargetName='Item' Property='Background' Value='#252ECC87'/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
</Style>";
        if(light) itemStyle=itemStyle.Replace("#F4F8FC","#25272B").Replace("#402ECC87","#E9F8F1").Replace("#252ECC87","#E9F8F1");
        calendars.ItemContainerStyle=(Style)XamlReader.Parse(itemStyle);
    }

    public CalendarWindow(string pythonPath, bool connect) {
        python=pythonPath; live=connect;
        awaitingInitialConnections=connect && AppPaths.Packaged && !RequiredAccountsConnected();
        Title="TimeTree · 바탕화면 캘린더"; Width=960; Height=700; MinWidth=900; MinHeight=540;
        WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brush("#00000000"); Opacity=1; ResizeMode=ResizeMode.NoResize;
        FontFamily=new FontFamily("Malgun Gothic"); FontSize=13; UseLayoutRounding=true; SnapsToDevicePixels=true;
        prefs=AppPaths.Defaults();
        try { foreach(var item in Json.Deserialize<D>(File.ReadAllText(SettingsFile,Encoding.UTF8))) prefs[item.Key]=item.Value; } catch { }
        calendarId=S(prefs,"calendar_id"); Topmost=B(prefs,"pin");
        pinBack=B(prefs,"pin_back") && !Topmost;
        int savedSync=(int)N(prefs,"sync_minutes",30); syncMinutes=new[]{0,15,30,60}.Contains(savedSync) ? savedSync : 30;
        nextSync=DateTime.UtcNow.AddMinutes(Math.Max(1,syncMinutes));
        backgroundTransparency=Math.Max(0,Math.Min(95,N(prefs,"background_transparency",65)));
        todoWidth=Math.Max(260,Math.Min(600,N(prefs,"todo_width",300))); todoColumn.Width=new GridLength(todoWidth);
        Width=Math.Max(MinWidth,Math.Min(SystemParameters.VirtualScreenWidth,N(prefs,"width",960)));
        Height=Math.Max(MinHeight,Math.Min(SystemParameters.VirtualScreenHeight,N(prefs,"height",700)));
        Left=Math.Max(SystemParameters.VirtualScreenLeft,Math.Min(SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Width,N(prefs,"left",Math.Max(20,SystemParameters.WorkArea.Right-Width-24))));
        Top=Math.Max(SystemParameters.VirtualScreenTop,Math.Min(SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-Height,N(prefs,"top",60)));
        try { Icon=BitmapFrame.Create(new Uri(System.IO.Path.Combine(Root,"calendar.ico"))); } catch { }
        var outer=new Grid { Margin=new Thickness(12) };
        WidgetTheme.Apply(outer.Resources);
        Content=outer;
        plate.CornerRadius=new CornerRadius(9); plate.BorderThickness=new Thickness(1); plate.BorderBrush=Brush("#40C7E3F8");
        plate.Effect=new DropShadowEffect { BlurRadius=18, ShadowDepth=3, Opacity=.16, Color=Colors.Black };
        outer.Children.Add(plate);
        var layout=new Grid { Margin=new Thickness(22,14,22,12) };
        outer.Children.Add(layout);
        layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var header=new DockPanel { LastChildFill=true, Margin=new Thickness(0,0,0,13), Background=Brush("#01FFFFFF") };
        Grid.SetRow(header,0); layout.Children.Add(header);
        var controls=new StackPanel { Orientation=Orientation.Horizontal };
        DockPanel.SetDock(controls,Dock.Right); header.Children.Add(controls);
        backSwitch=TodoPanel.CategorySwitch("뒤로 고정",pinBack,on=>SetWindowLayer(on ? false : Topmost,on),Theme.Green); backSwitch.ToolTip="다른 프로그램 뒤로 고정"; backSwitch.Margin=new Thickness(0,0,8,0); controls.Children.Add(backSwitch);
        controls.Children.Add(Btn("일정 추가",delegate { EditTree(); })); controls.Children.Add(Btn("새로고침",delegate { Refresh(true); todoPanel.Refresh(); })); controls.Children.Add(Btn("설정",delegate { ShowSettings(); }));
        var exit=Btn("×",Close); exit.ToolTip="위젯 종료"; AutomationProperties.SetName(exit,"위젯 종료"); controls.Children.Add(exit);
        brand.Foreground=new SolidColorBrush(Green); brand.Margin=new Thickness(0,0,18,0);
        brand.Cursor=Cursors.SizeAll; brand.ToolTip="드래그하여 위젯 이동";
        DockPanel.SetDock(brand,Dock.Left); header.Children.Add(brand);
        calendars.MinWidth=100; calendars.MaxWidth=160; calendars.Height=30; calendars.VerticalAlignment=VerticalAlignment.Center; StyleCalendarSelector(calendars);
        calendars.GotKeyboardFocus+=delegate { calendars.BorderBrush=new SolidColorBrush(Green); };
        calendars.LostKeyboardFocus+=delegate { calendars.BorderBrush=Brush("#30FFFFFF"); };
        calendars.SelectionChanged += delegate { if(!selectingCalendar && calendars.SelectedIndex>=0 && calendars.SelectedIndex<calendarList.Count) { calendarId=S(calendarList[calendars.SelectedIndex],"id"); Refresh(); ScheduleSave(); } };
        header.Children.Add(calendars);
        header.MouseLeftButtonDown += delegate(object sender,MouseButtonEventArgs e) { if(e.OriginalSource==header || e.OriginalSource==brand) DragMove(); };
        var nav=new Grid { Margin=new Thickness(0,0,0,16) };
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        Grid.SetRow(nav,1); layout.Children.Add(nav);
        period.Margin=new Thickness(0,0,12,0); nav.Children.Add(period);
        var arrows=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
        arrows.Children.Add(Btn("‹",delegate { Navigate(-1); })); arrows.Children.Add(Btn("›",delegate { Navigate(1); })); arrows.Children.Add(Btn("오늘",delegate { expandedWeekStart=null; selected=anchor=Today(); Refresh(); }));
        Grid.SetColumn(arrows,1); nav.Children.Add(arrows);
        var main=mainArea; main.ColumnDefinitions.Add(calendarColumn); main.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(12) }); main.ColumnDefinitions.Add(todoColumn);
        Grid.SetRow(main,2); layout.Children.Add(main); main.Children.Add(calendar);
        var side=new Border { CornerRadius=new CornerRadius(8), Background=Brush("#100D2336"), BorderBrush=Brush("#20FFFFFF"), BorderThickness=new Thickness(1), Padding=new Thickness(12) };
        Grid.SetColumn(side,2); main.Children.Add(side);
        var divider=new GridSplitter { Width=12,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext,Background=Brushes.Transparent,Cursor=Cursors.SizeWE,KeyboardIncrement=12,DragIncrement=1,ToolTip="드래그하거나 방향키로 Todo Mate 폭 조절" };
        AutomationProperties.SetName(divider,"Todo Mate 폭 조절");
        var handle=new FrameworkElementFactory(typeof(Border),"Handle"); handle.SetValue(Border.BackgroundProperty,Brush("#50FFFFFF")); handle.SetValue(Border.WidthProperty,3.0); handle.SetValue(Border.HeightProperty,38.0); handle.SetValue(Border.CornerRadiusProperty,new CornerRadius(2)); handle.SetValue(Border.HorizontalAlignmentProperty,HorizontalAlignment.Center); handle.SetValue(Border.VerticalAlignmentProperty,VerticalAlignment.Center);
        var dividerFrame=new FrameworkElementFactory(typeof(Border)); dividerFrame.SetValue(Border.BackgroundProperty,Brushes.Transparent); dividerFrame.AppendChild(handle);
        var dividerTemplate=new ControlTemplate(typeof(GridSplitter)) { VisualTree=dividerFrame };
        foreach(var property in new[]{UIElement.IsMouseOverProperty,UIElement.IsKeyboardFocusedProperty}) { var highlight=new Trigger { Property=property,Value=true }; highlight.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Green),"Handle")); dividerTemplate.Triggers.Add(highlight); }
        divider.Template=dividerTemplate; Grid.SetColumn(divider,1); main.Children.Add(divider);
        divider.DragCompleted+=delegate { SetTodoWidth(todoColumn.ActualWidth); ScheduleSave(); };
        divider.KeyUp+=delegate { SetTodoWidth(todoColumn.ActualWidth); ScheduleSave(); };
        main.SizeChanged+=delegate { todoColumn.Width=new GridLength(Math.Min(todoWidth,MaxTodoWidth)); };
        todoPanel=new TodoPanel(selected,SelectFromTodo,connect); side.Child=todoPanel;
        todoPanel.ConnectRequested=delegate { ShowSettings(true); };
        todoPanel.SetSyncMinutes(syncMinutes);
        var categoryControls=new ScrollViewer { Content=todoPanel.CategoryControls,MaxHeight=94,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center };
        Grid.SetColumn(categoryControls,2); nav.Children.Add(categoryControls);
        var footer=new DockPanel { Margin=new Thickness(0,10,0,0) }; Grid.SetRow(footer,3); layout.Children.Add(footer);
        var resize=new Thumb { Width=18, Height=18, Cursor=Cursors.SizeNWSE, Background=Brush("#01FFFFFF") };
        var grip=new FrameworkElementFactory(typeof(TextBlock)); grip.SetValue(TextBlock.TextProperty,"⋰"); grip.SetValue(TextBlock.ForegroundProperty,Brush("#AAFFFFFF")); resize.Template=new ControlTemplate(typeof(Thumb)) { VisualTree=grip };
        resize.DragDelta += delegate(object sender,DragDeltaEventArgs e) { ResizeEdges(1,1,e.HorizontalChange,e.VerticalChange); };
        DockPanel.SetDock(resize,Dock.Right); footer.Children.Add(resize);
        AddResizeHandles(outer);
        var web=Btn("TimeTree 열기 ↗",delegate { Process.Start("https://timetreeapp.com/calendars"); }); DockPanel.SetDock(web,Dock.Right); footer.Children.Add(web); footer.Children.Add(status);
        saveDelay.Tick += delegate { saveDelay.Stop(); Save(); };
        LocationChanged += delegate { ScheduleSave(); }; SizeChanged += delegate { ScheduleSave(); };
        autosync.Tick += delegate { if(FollowToday(Today())) Refresh(); if(syncMinutes>0 && DateTime.UtcNow>=nextSync && !loading) Refresh(true); };
        KeyDown += delegate(object sender,KeyEventArgs e) { if(e.Key==Key.F5) { Refresh(true); todoPanel.Refresh(); } if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.D0) { SetTransparency(0); ScheduleSave(); } };
        Closing += delegate(object sender,System.ComponentModel.CancelEventArgs e) {
            if(!CanClose()) { e.Cancel=true; status.Text="할 일 저장을 마친 뒤 종료해 주세요."; }
        };
        Activated += delegate { todoPanel.RefreshAfterBrowser(); Dispatcher.BeginInvoke(new Action(PlaceBehind)); };
        Deactivated+=delegate { Dispatcher.BeginInvoke(new Action(PlaceBehind)); };
        Loaded+=delegate { PlaceBehind(); };
        Closed += delegate { closing=true; if(commands!=null) commands.Dispose(); autosync.Stop(); saveDelay.Stop(); Save(); todoPanel.Dispose(); StopBackend(); };
        SetTransparency(backgroundTransparency); UpdatePeriod(); ShowSelectedTodos();
        EnsureRecommended();
        if(connect) { Loaded += delegate { RestoreCalendarList(); Refresh(); autosync.Start(); commands=new WidgetCommands(Dispatcher,HandleCommand,Close); if(ShouldPromptForAccounts) Dispatcher.BeginInvoke(new Action(()=>ShowSettings(true))); }; }
    }

    bool CanClose() { return !todoPanel.HasPendingWrite && !connectionBusy && !calendarEditing; }
    bool FollowToday(DateTime day) {
        if(day==lastToday) return false;
        bool follow=selected==lastToday; lastToday=day;
        if(follow) { selected=anchor=day; expandedWeekStart=null; UpdatePeriod(); ShowSelectedTodos(); }
        return true;
    }
    D State() {
        return new D { {"running",true},{"pid",Process.GetCurrentProcess().Id},{"date",selected.ToString("yyyy-MM-dd")},
            {"view",view},{"transparency",backgroundTransparency},{"pin",Topmost},{"pin_back",pinBack},{"calendar_id",calendarId},
            {"loading",loading},{"message",status.Text},{"sync_minutes",syncMinutes},{"todo_width",todoWidth} };
    }
    D HandleCommand(D request) {
        if(request==null) throw new ArgumentException("명령이 비어 있습니다.");
        string op=S(request,"op");
        if(!new[]{"status","open","settings","refresh","todo-cache","timetree-cache","editor","close"}.Contains(op)) throw new ArgumentException("지원하지 않는 위젯 명령입니다.");
        string nextView=S(request,"view",view); DateTime day=selected;
        if(nextView!="month") throw new ArgumentException("위젯은 월간 달력으로 표시됩니다. 주간 전환 기능은 제거되었습니다.");
        if(request.ContainsKey("date") && !DateTime.TryParseExact(S(request,"date"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out day)) throw new ArgumentException("날짜는 YYYY-MM-DD 형식입니다.");
        double alpha=N(request,"transparency",-1);
        if(request.ContainsKey("transparency") && (alpha<0 || alpha>95)) throw new ArgumentException("투명도는 0~95입니다.");
        if(request.ContainsKey("pin") && !(request["pin"] is bool)) throw new ArgumentException("pin은 true 또는 false입니다.");
        if(request.ContainsKey("pin_back") && !(request["pin_back"] is bool)) throw new ArgumentException("pin_back은 true 또는 false입니다.");
        if(B(request,"pin") && B(request,"pin_back")) throw new ArgumentException("항상 위와 가장 뒤 고정은 동시에 켤 수 없습니다.");
        int interval=(int)N(request,"sync_minutes",-1);
        if(request.ContainsKey("sync_minutes") && (!new[]{0,15,30,60}.Contains(interval) || N(request,"sync_minutes")!=interval)) throw new ArgumentException("동기화 간격은 0, 15, 30, 60분 중 하나입니다.");
        double pane=N(request,"todo_width",-1);
        if(request.ContainsKey("todo_width") && (pane<260 || pane>600)) throw new ArgumentException("Todo Mate 폭은 260~600px입니다.");
        if(request.ContainsKey("recommended") && (!(request["recommended"] is bool) || (B(request,"recommended") && new[]{"transparency","pin","pin_back","sync_minutes","todo_width"}.Any(request.ContainsKey)))) throw new ArgumentException("추천 설정은 다른 설정 옵션과 함께 적용할 수 없습니다.");
        if(op=="close") { if(!CanClose()) throw new InvalidOperationException("할 일을 저장 중입니다. 저장을 마친 뒤 종료해 주세요."); return State(); }
        if(op=="open" || op=="settings") {
            bool redraw=request.ContainsKey("date") || request.ContainsKey("view");
            selected=day; if(redraw) { anchor=day; expandedWeekStart=null; ShowSelectedTodos(); UpdatePeriod(); Refresh(); }
            if(request.ContainsKey("transparency")) SetTransparency(alpha);
            if(request.ContainsKey("pin") || request.ContainsKey("pin_back")) {
                bool above=request.ContainsKey("pin") ? B(request,"pin") : Topmost;
                bool below=request.ContainsKey("pin_back") ? B(request,"pin_back") : pinBack;
                if(B(request,"pin")) below=false;
                if(B(request,"pin_back")) above=false;
                SetWindowLayer(above,below);
            }
            if(request.ContainsKey("sync_minutes")) SetSyncMinutes(interval);
            if(request.ContainsKey("todo_width")) SetTodoWidth(pane);
            if(B(request,"recommended")) ApplyRecommended();
            if(settings!=null) settings.Close();
            if(!Save()) throw new IOException("설정은 적용되었지만 저장하지 못했습니다.");
        }
        if(op=="open") { Show(); WindowState=WindowState.Normal; Activate(); }
        if(op=="editor") {
            if(!todoPanel.OpenOriginal(S(request,"task"))) throw new InvalidOperationException("기본 브라우저를 열지 못했습니다. Windows 기본 브라우저 설정을 확인해 주세요.");
            return new D { {"opened","browser"},{"url","https://www.todomate.net/"} };
        }
        if(op=="refresh") { Refresh(true); todoPanel.Refresh(); }
        if(op=="todo-cache") todoPanel.ReloadCached();
        if(op=="timetree-cache") Refresh();
        return State();
    }

    void SetTransparency(double value) {
        backgroundTransparency=Math.Max(0,Math.Min(95,value));
        // Only this brush changes alpha; the window and every foreground element stay opaque.
        plate.Background=new SolidColorBrush(Color.FromArgb(awaitingInitialConnections ? (byte)255 : (byte)Math.Round(255*(1-backgroundTransparency/100)),16,42,62));
    }
    void ScheduleSave() { if(!IsLoaded || closing) return; saveDelay.Stop(); saveDelay.Start(); }
    void SetSyncMinutes(int minutes) { syncMinutes=minutes; nextSync=DateTime.UtcNow.AddMinutes(Math.Max(1,minutes)); todoPanel.SetSyncMinutes(minutes); ScheduleSave(); }
    double MaxTodoWidth { get { return mainArea.ActualWidth>0 ? Math.Max(260,Math.Min(600,mainArea.ActualWidth-432)) : 600; } }
    void SetTodoWidth(double width) { todoWidth=Math.Max(260,Math.Min(MaxTodoWidth,width)); calendarColumn.Width=new GridLength(1,GridUnitType.Star); todoColumn.Width=new GridLength(todoWidth); ScheduleSave(); }
    void ResizeEdges(int horizontal,int vertical,double dx,double dy) {
        if(horizontal!=0) {
            double next=Math.Max(MinWidth,Math.Min(Math.Max(MinWidth,SystemParameters.VirtualScreenWidth),Width+(horizontal<0 ? -dx : dx)));
            if(horizontal<0) Left+=Width-next;
            Width=next;
        }
        if(vertical!=0) {
            double next=Math.Max(MinHeight,Math.Min(Math.Max(MinHeight,SystemParameters.VirtualScreenHeight),Height+(vertical<0 ? -dy : dy)));
            if(vertical<0) Top+=Height-next;
            Height=next;
        }
    }
    void AddResizeHandles(Grid outer) {
        foreach(int x in new[]{-1,0,1}) foreach(int y in new[]{-1,0,1}) {
            if(x==0 && y==0) continue;
            int horizontal=x,vertical=y;
            var edge=new Thumb { Width=x==0 ? Double.NaN : 10,Height=y==0 ? Double.NaN : 10,
                HorizontalAlignment=x<0 ? HorizontalAlignment.Left : x>0 ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment=y<0 ? VerticalAlignment.Top : y>0 ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                Margin=new Thickness(x==0 ? 10 : 0,y==0 ? 10 : 0,x==0 ? 10 : 0,y==0 ? 10 : 0),
                Cursor=x==0 ? Cursors.SizeNS : y==0 ? Cursors.SizeWE : x==y ? Cursors.SizeNWSE : Cursors.SizeNESW,Tag="edge-resize:"+x+","+y };
            var surface=new FrameworkElementFactory(typeof(Border)); surface.SetValue(Border.BackgroundProperty,Brushes.Transparent); edge.Template=new ControlTemplate(typeof(Thumb)) { VisualTree=surface };
            edge.DragDelta+=delegate(object sender,DragDeltaEventArgs e) { ResizeEdges(horizontal,vertical,e.HorizontalChange,e.VerticalChange); };
            AutomationProperties.SetName(edge,(y<0 ? "위쪽 " : y>0 ? "아래쪽 " : "")+(x<0 ? "왼쪽 " : x>0 ? "오른쪽 " : "")+"크기 조절");
            Panel.SetZIndex(edge,10); outer.Children.Add(edge);
        }
    }
    string RecommendedFile { get { return System.IO.Path.ChangeExtension(SettingsFile,"recommended.json"); } }
    void EnsureRecommended() {
        if(File.Exists(RecommendedFile)) return;
        try {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RecommendedFile)); string temp=RecommendedFile+".tmp";
            try { File.WriteAllText(temp,Json.Serialize(new { background_transparency=backgroundTransparency,pin=Topmost,pin_back=pinBack,sync_minutes=syncMinutes,todo_width=todoWidth,todo=todoPanel.PreferenceValues(),autostart=B(AppPaths.Defaults(),"autostart") }),new UTF8Encoding(false)); File.Move(temp,RecommendedFile); }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
        catch { status.Text="현재 구성을 추천 설정으로 저장하지 못했습니다."; }
    }
    void ApplyRecommended() {
        var value=Json.Deserialize<D>(File.ReadAllText(RecommendedFile,Encoding.UTF8));
        double alpha=N(value,"background_transparency",-1),width=N(value,"todo_width",-1),minutes=N(value,"sync_minutes",-1); object pin,todo;
        if(alpha<0 || alpha>95 || width<260 || width>600 || !new[]{0.0,15.0,30.0,60.0}.Contains(minutes) || !value.TryGetValue("pin",out pin) || !(pin is bool) || !value.TryGetValue("todo",out todo) || !(todo is D)) throw new ArgumentException("추천 설정을 읽지 못했습니다.");
        todoPanel.RestorePreferences((D)todo);
        SetTransparency(alpha); SetWindowLayer((bool)pin,B(value,"pin_back") && !(bool)pin); SetSyncMinutes((int)minutes); SetTodoWidth(width);
        if(live && value.ContainsKey("autostart")) StartupState(B(value,"autostart") ? "on" : "off");
        if(!Save()) throw new IOException("추천 설정은 적용되었지만 저장하지 못했습니다.");
    }
    bool Save() {
        try {
            prefs["view"]=view; prefs["calendar_id"]=calendarId; prefs["pin"]=Topmost; prefs["pin_back"]=pinBack;
            prefs["sync_minutes"]=syncMinutes;
            prefs["todo_width"]=todoWidth;
            prefs["background_transparency"]=backgroundTransparency; prefs.Remove("transparency");
            prefs["width"]=Width; prefs["height"]=Height; prefs["left"]=Left; prefs["top"]=Top;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile));
            var temp=SettingsFile+".tmp"; File.WriteAllText(temp,Json.Serialize(prefs),new UTF8Encoding(false));
            if(File.Exists(SettingsFile)) File.Replace(temp,SettingsFile,null); else File.Move(temp,SettingsFile);
            return true;
        } catch { status.Text="화면 설정을 저장하지 못했습니다."; return false; }
    }
    void UpdatePeriod() {
        period.Text=anchor.ToString("yyyy년 M월");
    }
    void Navigate(int delta) { expandedWeekStart=null; anchor=new DateTime(anchor.Year,anchor.Month,1).AddMonths(delta); selected=anchor; ShowSelectedTodos(); UpdatePeriod(); Refresh(); }
    void ExpandWeek(DateTime start) { expandedWeekStart=expandedWeekStart==start ? (DateTime?)null : start; SetModel(model); }
    void StartBackend() {
        if(backend!=null && !backend.HasExited) return;
        backendReady=false;
        try {
            backend=new Process { StartInfo=new ProcessStartInfo { FileName=python, Arguments="-X utf8 -B -u \""+System.IO.Path.Combine(Root,"backend.py")+"\"", WorkingDirectory=Root, UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Encoding.UTF8 } };
            backend.Start(); backend.BeginErrorReadLine();
            var process=backend;
            new Thread(delegate() {
                var readerJson=new JavaScriptSerializer { MaxJsonLength=16000000 };
                try { string line; while((line=process.StandardOutput.ReadLine())!=null) { var data=readerJson.Deserialize<D>(line); Dispatcher.BeginInvoke(new Action(delegate { if(!closing && Object.ReferenceEquals(backend,process)) Receive(data); })); } }
                catch { }
                if(!closing && Object.ReferenceEquals(backend,process)) Dispatcher.BeginInvoke(new Action(delegate { if(Object.ReferenceEquals(backend,process)) { loading=false; status.Text="연결이 종료되었습니다. 새로고침해 주세요."; } }));
            }) { IsBackground=true }.Start();
        } catch { loading=false; status.Text="로컬 Python 연결을 시작하지 못했습니다."; }
    }
    void StopBackend() {
        var process=backend; backend=null;
        if(process==null) return;
        try { process.StandardInput.Close(); if(!process.WaitForExit(2500)) { using(var kill=Process.Start(new ProcessStartInfo("taskkill.exe","/PID "+process.Id+" /T /F") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true })) kill.WaitForExit(3000); } } catch { }
        process.Dispose();
    }
    void Refresh(bool force=false) {
        if(calendarEditing) return;
        if(closing) return;
        if(AppPaths.Packaged && !AppPaths.TreeConnected) { status.Text="설정의 계정 연결에서 TimeTree에 로그인해 주세요."; return; }
        generation++; loading=false; UpdatePeriod();
        bool cached=TryCachedView();
        if(cached && !force) return;
        if(!cached) { calendar.Children.Clear(); calendar.RowDefinitions.Clear(); calendar.ColumnDefinitions.Clear(); dateButtons.Clear(); model=null; paintedModel=""; }
        loading=true; status.Text=cached ? "변경 사항 확인 중…" : "처음 불러오는 달입니다. 동기화 중…";
        if(force) nextSync=DateTime.UtcNow.AddMinutes(Math.Max(1,syncMinutes));
        if(backend==null || backend.HasExited) { pendingForce|=force; StartBackend(); return; }
        if(!backendReady) { pendingForce|=force; return; }
        try { backend.StandardInput.WriteLine(Json.Serialize(new D { {"id",generation},{"calendar_id",calendarId},{"anchor",anchor.ToString("yyyy-MM-dd")},{"view",view},{"force",force} })); backend.StandardInput.Flush(); }
        catch { loading=false; status.Text="연결이 끊겼습니다. 새로고침해 주세요."; }
    }
    void ApplyView(D data) { string payload=Json.Serialize(data); if(payload!=paintedModel) { SetModel(data); paintedModel=payload; } else ShowSelectedTodos(); }
    bool TryCachedView() {
        var saved=SnapshotStore.Read("timetree",calendarId+"|"+anchor.ToString("yyyy-MM"));
        if(saved==null) return false;
        try {
            var value=Json.Deserialize<D>(saved.Payload); var cached=(D)value["model"];
            if(!A(cached,"days").Any(d=>Convert.ToString(d)==anchor.ToString("yyyy-MM-dd"))) return false;
            ApplyView(cached); status.Text="";
            if(S(value,"warning")!="") status.Text=S(value,"warning");
            nextSync=saved.Updated.AddMinutes(Math.Max(1,syncMinutes)); return true;
        } catch { return false; }
    }
    void SetCalendarList(D value) {
        selectingCalendar=true;
        try {
            calendarList=A(value,"calendars").Cast<D>().ToList(); calendars.ItemsSource=calendarList.Select(c=>S(c,"name")).ToList();
            int index=calendarList.FindIndex(c=>S(c,"id")==calendarId); calendars.SelectedIndex=index<0 && calendarList.Count>0 ? 0 : index;
            if(calendars.SelectedIndex>=0) calendarId=S(calendarList[calendars.SelectedIndex],"id");
        } finally { selectingCalendar=false; }
    }
    void RestoreCalendarList() { var saved=SnapshotStore.Read("timetree","calendars"); if(saved!=null) try { SetCalendarList(Json.Deserialize<D>(saved.Payload)); } catch {} }
    void Receive(D data) {
        switch(S(data,"kind")) {
            case "ready":
                backendReady=true; SetCalendarList(data); bool force=pendingForce; pendingForce=false; Refresh(force);
                break;
            case "calendars": string previousCalendar=calendarId; SetCalendarList(data); if(previousCalendar!=calendarId) Refresh(); break;
            case "error": if(N(data,"id")!=0 && (int)N(data,"id")!=generation) return; loading=false; status.Text=S(data,"message")+(model!=null ? " · 저장된 일정 유지" : ""); nextSync=DateTime.UtcNow.AddMinutes(Math.Max(1,syncMinutes)); break;
            case "view":
                if((int)N(data,"id")!=generation) return;
                loading=false; ApplyView((D)data["model"]); nextSync=DateTime.UtcNow.AddMinutes(Math.Max(1,syncMinutes));
                status.Text="동기화 "+DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm")+(syncMinutes==0 ? " · 수동 새로고침" : " · "+syncMinutes+"분마다 확인");
                if(S(data,"warning")!="") status.Text=S(data,"warning");
                break;
        }
    }
    public void SetModel(D data) {
        model=data; period.Text=S(model,"period");
        calendar.Children.Clear(); calendar.RowDefinitions.Clear(); calendar.ColumnDefinitions.Clear(); dateButtons.Clear();
        DrawMonth(); ShowSelectedTodos();
    }
    void Select(DateTime day) {
        selected=day;
        foreach(var pair in dateButtons) pair.Value.Background=pair.Key==Today() ? new SolidColorBrush(Green) : pair.Key==selected ? Brush("#402ECC87") : Brush("#00000000");
        ShowSelectedTodos();
    }
    void SelectFromTodo(DateTime day) {
        Select(day);
        if(model==null || !A(model,"days").Any(d=>Convert.ToString(d)==day.ToString("yyyy-MM-dd"))) { anchor=day; expandedWeekStart=null; Refresh(); }
    }
    Button DateButton(DateTime day, int column) {
        string label=day.Day.ToString();
        var button=Btn(label,delegate { Select(day); }); button.FontSize=12; button.FontWeight=FontWeights.SemiBold; button.Padding=new Thickness(0); button.Height=28; button.MinWidth=28; button.Margin=new Thickness(3,4,3,2);
        button.Background=day==Today() ? new SolidColorBrush(Green) : day==selected ? Brush("#402ECC87") : Brush("#00000000");
        if(day==Today()) button.Foreground=new SolidColorBrush(DarkInk); else if(day.Month!=anchor.Month) button.Foreground=Brush("#B1BDC9"); else if(column==0) button.Foreground=Brush("#FFB9C0"); else if(column==6) button.Foreground=Brush("#B0D0FF");
        var text=Text(label,12,true); text.Foreground=button.Foreground; button.Content=text; dateButtons[day]=button;
        return button;
    }
    Button EventButton(D e, string prefix) {
        var button=Btn(prefix+S(e,"title"),delegate { Details(e); });
        button.Background=Brush(S(e,"background")); button.Foreground=Brush(S(e,"foreground")); button.Padding=new Thickness(5,2,5,2); button.Margin=new Thickness(2,1,2,1); button.HorizontalContentAlignment=HorizontalAlignment.Left;
        button.FontSize=12; button.Content=new TextBlock { Text=prefix+S(e,"title"), TextTrimming=TextTrimming.CharacterEllipsis, Foreground=button.Foreground, FontSize=12 };
        button.ToolTip=S(e,"title"); button.Tag=e; return button;
    }
    void DrawMonth() {
        var days=A(model,"days").Select(x=>DateTime.Parse((string)x,CultureInfo.InvariantCulture)).ToArray();
        calendar.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var header=new UniformGrid { Columns=7 };
        for(int c=0;c<7;c++) { var t=Text("일월화수목금토"[c].ToString(),12,false); t.HorizontalAlignment=HorizontalAlignment.Center; t.Margin=new Thickness(0,0,0,10); if(c==0)t.Foreground=Brush("#FFB9C0"); if(c==6)t.Foreground=Brush("#B0D0FF"); header.Children.Add(t); }
        calendar.Children.Add(header);
        var weeks=A(model,"weeks");
        calendar.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        var weekStack=new Grid();
        var monthScroll=new ScrollViewer { Content=weekStack, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, CanContentScroll=false };
        Grid.SetRow(monthScroll,1); calendar.Children.Add(monthScroll);
        Action sizeWeeks=delegate {
            double normal=Math.Max(120,monthScroll.ViewportHeight)/weeks.Length, total=0;
            header.Width=Math.Max(0,monthScroll.ViewportWidth);
            for(int index=0;index<weeks.Length;index++) {
                var placements=((IEnumerable)weeks[index]).Cast<D>().ToList();
                double rows=placements.Count==0 ? 0 : placements.Max(p=>N(p,"row"))+1;
                double sparseRows=0;
                for(int column=0;column<7;column++) {
                    var entries=placements.Where(p=>N(p,"first")<=column && N(p,"last")>=column).ToList();
                    if(entries.Count>0 && entries.Count<=2) sparseRows=Math.Max(sparseRows,entries.Max(p=>N(p,"row"))+1);
                }
                double height=expandedWeekStart==days[index*7] ? Math.Max(normal,34+rows*24+30) : Math.Max(normal,34+sparseRows*24);
                weekStack.RowDefinitions[index].Height=new GridLength(height); total+=height;
            }
            weekStack.Height=total;
        };
        for(int w=0;w<weeks.Length;w++) {
            weekStack.RowDefinitions.Add(new RowDefinition());
            var frame=new Grid { Tag=days[w*7] }; Grid.SetRow(frame,w); weekStack.Children.Add(frame);
            var cells=new UniformGrid { Columns=7 }; frame.Children.Add(cells);
            for(int c=0;c<7;c++) {
                DateTime day=days[w*7+c];
                var cell=new Border { BorderBrush=Line, BorderThickness=new Thickness(0,1,c==6 ? 0 : 1,0), Background=Brush(c==6 ? "#133E345C" : "#060A2338") };
                var date=DateButton(day,c); date.HorizontalAlignment=HorizontalAlignment.Left; date.VerticalAlignment=VerticalAlignment.Top; cell.Child=date;
                cell.MouseLeftButtonUp += delegate { Select(day); }; cells.Children.Add(cell);
            }
            DateTime weekStart=days[w*7];
            if(expandedWeekStart==weekStart) {
                var collapse=Btn("접기",delegate { ExpandWeek(weekStart); }); collapse.HorizontalAlignment=HorizontalAlignment.Right; collapse.VerticalAlignment=VerticalAlignment.Bottom; collapse.Height=24; collapse.Padding=new Thickness(6,1,6,1); collapse.Margin=new Thickness(0,0,3,3); Panel.SetZIndex(collapse,1); frame.Children.Add(collapse);
            }
            var canvas=new Canvas { Tag="month-events", ClipToBounds=true, Margin=new Thickness(0,34,0,0) }; frame.Children.Add(canvas);
            var moreHeader=new Canvas { Tag="month-more", Height=34, VerticalAlignment=VerticalAlignment.Top }; frame.Children.Add(moreHeader);
            var placements=((IEnumerable)weeks[w]).Cast<D>().ToList(); int week=w;
            Action arrange=delegate {
                canvas.Children.Clear(); moreHeader.Children.Clear(); double width=canvas.ActualWidth/7; const double height=24;
                int capacity=Math.Max(0,(int)(canvas.ActualHeight/height));
                var hidden=new int[7];
                foreach(var p in placements) {
                    int first=(int)N(p,"first"),last=(int)N(p,"last"),row=(int)N(p,"row"); var ev=(D)p["event"];
                    if(row>=capacity) { for(int c=first;c<=last;c++)hidden[c]++; continue; }
                    var button=EventButton(ev,B(ev,"all_day") ? "" : Start(ev).ToString("HH:mm "));
                    button.Width=Math.Max(1,(last-first+1)*width-2); button.Height=22; Canvas.SetLeft(button,first*width); Canvas.SetTop(button,row*height); canvas.Children.Add(button);
                }
                for(int c=0;c<7;c++) if(hidden[c]>0) {
                    DateTime day=days[week*7+c]; var more=Btn("전체 보기",delegate { selected=day; ExpandWeek(weekStart); }); more.Content="전체"; more.ToolTip="숨겨진 일정 "+hidden[c]+"개 · 이 주 전체 펼치기"; more.Margin=new Thickness(0); more.Padding=new Thickness(3,0,3,0); more.FontSize=11; more.Width=30; more.Height=22;
                    Canvas.SetLeft(more,(c+1)*width-33); Canvas.SetTop(more,5); moreHeader.Children.Add(more);
                }
            };
            canvas.SizeChanged += delegate { arrange(); }; canvas.Loaded += delegate { arrange(); };
        }
        monthScroll.SizeChanged += delegate { sizeWeeks(); };
        monthScroll.ScrollChanged += delegate(object sender,ScrollChangedEventArgs e) { if(e.ViewportHeightChange!=0 || e.ViewportWidthChange!=0) sizeWeeks(); };
        monthScroll.Loaded += delegate {
            sizeWeeks();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate {
                int index=expandedWeekStart.HasValue ? Array.IndexOf(days,expandedWeekStart.Value)/7 : 0;
                double offset=0; for(int i=0;i<Math.Max(0,index);i++) offset+=weekStack.RowDefinitions[i].Height.Value;
                monthScroll.ScrollToVerticalOffset(offset);
            }));
        };
    }
    void ShowSelectedTodos() { todoPanel.SelectDate(selected); }
    void Details(D e) {
        EditTree(e);
    }
    void ShowSettings(bool showAccounts=false) {
        if(settings!=null) { if(showAccounts && changeSettingsTab!=null) changeSettingsTab(true); settings.Activate(); return; }
        settings=new Window { Title="위젯 설정", Owner=this, Width=390, Height=520, MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-40), ResizeMode=ResizeMode.NoResize, WindowStyle=WindowStyle.None,AllowsTransparency=true,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.Transparent,Foreground=new SolidColorBrush(Ink),FontFamily=FontFamily,FontSize=13 };
        WidgetTheme.Apply(settings.Resources);
        var settingsLayout=new DockPanel();
        settings.Content=new Border { Background=Brush("#102A3E"),BorderBrush=Brush("#40C7E3F8"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(18),Child=settingsLayout };
        var heading=new DockPanel { Margin=new Thickness(0,0,0,12) }; DockPanel.SetDock(heading,Dock.Top); settingsLayout.Children.Add(heading);
        var dismiss=Btn("×",settings.Close); AutomationProperties.SetName(dismiss,"설정 닫기"); DockPanel.SetDock(dismiss,Dock.Right); heading.Children.Add(dismiss); heading.Children.Add(Text("위젯 설정",20,true));
        heading.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) { if(e.OriginalSource is TextBlock || e.OriginalSource==heading) settings.DragMove(); };
        var tabs=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10) }; DockPanel.SetDock(tabs,Dock.Top); settingsLayout.Children.Add(tabs);
        var actions=new StackPanel { Margin=new Thickness(0,10,0,0) }; DockPanel.SetDock(actions,Dock.Bottom); settingsLayout.Children.Add(actions);
        var notice=Text("",12,false); notice.Margin=new Thickness(2,0,0,4); notice.TextWrapping=TextWrapping.Wrap; actions.Children.Add(notice);
        var actionRow=new DockPanel(); actions.Children.Add(actionRow);
        var close=Btn("닫기",settings.Close); DockPanel.SetDock(close,Dock.Right); actionRow.Children.Add(close);
        var skip=Btn("건너뛰기 · 나중에 연결",delegate { try { DeferAccountSetup(); settings.Close(); } catch(Exception error) { notice.Text=error.Message; } });
        skip.ToolTip="다음 실행부터 연결 팝업을 자동으로 띄우지 않습니다. 설정 → 계정 연결에서 다시 연결할 수 있습니다."; actionRow.Children.Add(skip);
        var reset=Btn("추천 설정 적용",delegate {}); reset.Background=new SolidColorBrush(Green); reset.Foreground=new SolidColorBrush(DarkInk); actionRow.Children.Add(reset);
        var body=new Grid(); settingsLayout.Children.Add(body);
        var stack=new StackPanel { Margin=new Thickness(0,0,10,0) }; var appearance=new ScrollViewer { Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; body.Children.Add(appearance);
        var accounts=new ScrollViewer { Content=AccountSettingsPage(),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; body.Children.Add(accounts);
        Button appearanceTab=null,accountTab=null;
        Action<bool> selectTab=delegate(bool account) { appearance.Visibility=account ? Visibility.Collapsed : Visibility.Visible; accounts.Visibility=account ? Visibility.Visible : Visibility.Collapsed; appearanceTab.Background=account ? Brush("#18FFFFFF") : new SolidColorBrush(Green); appearanceTab.Foreground=new SolidColorBrush(account ? Ink : DarkInk); accountTab.Background=account ? new SolidColorBrush(Green) : Brush("#18FFFFFF"); accountTab.Foreground=new SolidColorBrush(account ? DarkInk : Ink); reset.Visibility=account ? Visibility.Collapsed : Visibility.Visible; skip.Visibility=account ? Visibility.Visible : Visibility.Collapsed; };
        changeSettingsTab=selectTab;
        appearanceTab=Btn("화면 설정",delegate { selectTab(false); }); accountTab=Btn("계정 연결",delegate { selectTab(true); }); tabs.Children.Add(appearanceTab); tabs.Children.Add(accountTab); selectTab(showAccounts);
        stack.Children.Add(Text("화면",16,true));
        var value=Text("",13,false); value.Margin=new Thickness(0,12,0,4); stack.Children.Add(value);
        var slider=new Slider { Minimum=0,Maximum=95,Value=backgroundTransparency,TickFrequency=5,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,0,6) };
        AutomationProperties.SetName(slider,"배경 투명도");
        slider.ValueChanged += delegate { SetTransparency(slider.Value); value.Text="배경 투명도 "+Math.Round(slider.Value)+"%"; ScheduleSave(); };
        value.Text="배경 투명도 "+Math.Round(backgroundTransparency)+"%"; stack.Children.Add(slider);
        stack.Children.Add(new TextBlock { Text="글자와 일정 색은 선명하게 유지됩니다.",FontSize=12,Margin=new Thickness(0,0,0,16),Foreground=Brush("#C3D3E0") });
        var paneValue=Text("Todo Mate 폭 "+Math.Round(todoWidth)+"px",13,false); stack.Children.Add(paneValue);
        var paneSlider=new Slider { Minimum=260,Maximum=MaxTodoWidth,Value=todoWidth,TickFrequency=10,IsSnapToTickEnabled=true,Margin=new Thickness(0,3,0,4) }; AutomationProperties.SetName(paneSlider,"Todo Mate 폭"); stack.Children.Add(paneSlider);
        paneSlider.ValueChanged+=delegate { SetTodoWidth(paneSlider.Value); paneValue.Text="Todo Mate 폭 "+Math.Round(todoWidth)+"px"; };
        stack.Children.Add(new TextBlock { Text="달력과 할 일 사이 경계를 드래그해도 조절됩니다.",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14),Foreground=Brush("#C3D3E0") });
        var pin=new CheckBox { Content="항상 위에 표시", Foreground=new SolidColorBrush(Ink), IsChecked=Topmost, Margin=new Thickness(0,0,0,16) };
        stack.Children.Add(pin);
        ToggleButton back=null;
        pin.Click+=delegate { SetWindowLayer(pin.IsChecked==true,pin.IsChecked==true ? false : pinBack); back.IsChecked=pinBack; };
        back=TodoPanel.CategorySwitch("가장 뒤로 고정",pinBack,on=> { SetWindowLayer(on ? false : Topmost,on); pin.IsChecked=Topmost; },Theme.Green); back.Margin=new Thickness(0,0,0,6); stack.Children.Add(back);
        settings.Activated+=delegate { pin.IsChecked=Topmost; back.IsChecked=pinBack; };
        stack.Children.Add(new TextBlock { Text="다른 프로그램 뒤에 표시합니다. 상단 토글로도 켜고 끌 수 있습니다.",TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brush("#C3D3E0"),Margin=new Thickness(0,0,0,16) });
        var startup=new CheckBox { Content="Windows 로그인 시 자동 실행",Margin=new Thickness(0,0,0,18) }; stack.Children.Add(startup);
        var startupLoad=LoadStartup(startup,notice);
        startup.Click+=async delegate { bool desired=startup.IsChecked==true; startup.IsEnabled=false; try { await Task.Run(()=>StartupState(desired ? "on" : "off")); } catch(Exception error) { startup.IsChecked=!desired; notice.Text=error.Message; } finally { startup.IsEnabled=true; } };
        stack.Children.Add(Text("동기화",16,true));
        var syncOptions=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,6) }; stack.Children.Add(syncOptions);
        Action updateOptions=delegate { foreach(Button b in syncOptions.Children) { bool active=(int)b.Tag==syncMinutes; b.Background=active ? new SolidColorBrush(Green) : Brush("#18FFFFFF"); b.Foreground=new SolidColorBrush(active ? DarkInk : Ink); } };
        foreach(int interval in new[]{0,15,30,60}) {
            int minutes=interval; var option=Btn(minutes==0 ? "수동만" : minutes+"분",delegate { SetSyncMinutes(minutes); updateOptions(); }); option.Tag=minutes; syncOptions.Children.Add(option);
        }
        updateOptions();
        stack.Children.Add(new TextBlock { Text="화면을 이동할 때는 저장된 데이터를 먼저 보여드립니다.",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20),Foreground=Brush("#C3D3E0") });
        stack.Children.Add(Text("Todo Mate 카테고리",16,true));
        stack.Children.Add(new TextBlock { Text="끔으로 설정한 카테고리는 전체 보기에서도 숨깁니다.",TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brush("#C3D3E0"),Margin=new Thickness(0,6,0,10) });
        var categorySettings=new ContentControl { Content=todoPanel.CategoryPreferences() }; stack.Children.Add(categorySettings);
        reset.Click+=delegate {
            try { ApplyRecommended(); slider.Value=backgroundTransparency; pin.IsChecked=Topmost; back.IsChecked=pinBack; paneSlider.Value=todoWidth; startup.IsChecked=StartupState("status"); value.Text="배경 투명도 "+Math.Round(backgroundTransparency)+"%"; paneValue.Text="Todo Mate 폭 "+Math.Round(todoWidth)+"px"; updateOptions(); categorySettings.Content=todoPanel.CategoryPreferences(); notice.Text="추천 설정을 적용했습니다."; }
            catch(Exception error) { notice.Text=error.Message; }
        };
        settings.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Escape) settings.Close(); };
        settings.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(connectionBusy) { e.Cancel=true; notice.Text="계정 연결 확인을 마친 뒤 닫아 주세요."; } };
        settings.Closed += delegate { settings=null; changeSettingsTab=null; Save(); PlaceBehind(); }; settings.Show();
    }

    static BitmapSource Render(FrameworkElement element) {
        element.Measure(new Size(936,676)); element.Arrange(new Rect(0,0,936,676)); element.UpdateLayout();
        var bitmap=new RenderTargetBitmap(936,676,96,96,PixelFormats.Pbgra32); bitmap.Render(element); return bitmap;
    }
    static BitmapSource LayoutForTest(FrameworkElement content) {
        Render(content);
        foreach(var scroll in Descendants<ScrollViewer>(content).ToList()) scroll.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Render(content);
        foreach(var canvas in Descendants<Canvas>(content).ToList()) canvas.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        return Render(content);
    }
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {
            var child=VisualTreeHelper.GetChild(parent,i); if(child is T) yield return (T)child;
            foreach(var nested in Descendants<T>(child)) yield return nested;
        }
    }
    static void WritePng(BitmapSource image,string path) { var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using(var file=File.Create(path))encoder.Save(file); }
    static void SelfTest(string fixture,string report) {
        Environment.SetEnvironmentVariable("DAYDESK_DATA_DIR",Path.Combine(Path.GetTempPath(),"daydesk-selftest-data-"+Guid.NewGuid().ToString("N")));
        SnapshotStore.WriteFile(AppPaths.TreeAuth,Json.Serialize(new { connected=true,account_key="selftest",email="fixture@example.invalid",password="synthetic-only" }));
        System.Threading.Tasks.Task.Run(()=>ClientSelfTest.Run()).GetAwaiter().GetResult();
        SnapshotStore.Root=Path.Combine(Path.GetTempPath(),"daydesk-cache-selftest-"+Guid.NewGuid().ToString("N"));
        SettingsFile=System.IO.Path.ChangeExtension(report,"settings.json"); File.WriteAllText(SettingsFile,"{\"view\":\"week\"}",Encoding.UTF8);
        var data=Json.Deserialize<D>(File.ReadAllText(fixture,Encoding.UTF8));
        var window=new CalendarWindow("",false); window.anchor=window.selected=new DateTime(2026,11,3); window.SetModel(data);
        Check(S(window.State(),"view")=="month","Legacy weekly preference was not migrated to the month calendar");
        window.todoPanel.ApplyData(new WeekData {
            Goals=new List<Goal> { new Goal { Id="study",Title="연구" },new Goal { Id="life",Title="생활" },new Goal { Id="empty",Title="빈 카테고리" } },
            Todos=new List<Todo> {
                new Todo { Id="sample-a",GoalId="study",Content="논문 읽기",Date=new DateTime(2026,11,3) },
                new Todo { Id="sample-b",GoalId="life",Content="운동하기",Date=new DateTime(2026,11,3),Done=true },
                new Todo { Id="sample-c",GoalId="study",Content="실험 결과 정리",Date=new DateTime(2026,11,4) },
                new Todo { Id="sample-deleted",GoalId="deleted",Content="삭제된 카테고리의 잔여 항목",Date=new DateTime(2026,11,3) }
            }
        },new DateTime(2026,11,1));
        Check(window.todoPanel.VisibleTodos.Count==2,"Todo panel did not display selected-day tasks");
        Check(window.todoPanel.OccupiedCategories.Count()==2,"Empty categories appeared in Todo Mate");
        window.Select(new DateTime(2026,11,4)); Check(window.todoPanel.VisibleTodos.Count==1 && window.todoPanel.VisibleTodos[0].Id=="sample-c","Calendar date did not update Todo Mate");
        Check(window.todoPanel.OccupiedCategories.Count()==1,"Categories from another date remained visible");
        window.Select(new DateTime(2026,11,5)); Check(window.todoPanel.VisibleTodos.Count==0 && window.todoPanel.OccupiedCategories.Count()==0 && window.todoPanel.CategoryControls.Visibility==Visibility.Visible,"An empty date hid the category switches or showed empty list categories");
        window.Select(new DateTime(2026,11,3)); window.todoPanel.SetCategory("study",false);
        Check(window.todoPanel.VisibleTodos.Count==1 && window.todoPanel.VisibleTodos[0].Id=="sample-b","Todo category filtering failed");
        window.todoPanel.SetCategory("study",true);
        Check(((WrapPanel)window.todoPanel.CategoryControls).Children.OfType<ToggleButton>().Count()==1,"Header must contain only one category switch");
        var switches=((StackPanel)window.todoPanel.CategoryPreferences()).Children.OfType<ToggleButton>().ToList();
        Check(switches.Count==3 && switches.Any(t=>Convert.ToString(t.Tag)=="empty" && t.IsChecked==true),"Personal settings do not include empty categories");
        var studySwitch=switches.Single(t=>Object.Equals(t.Tag,"study"));
        studySwitch.IsChecked=false; studySwitch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(window.todoPanel.VisibleTodos.Count==1 && ((Grid)studySwitch.Content).Children.OfType<TextBlock>().Last().Text=="끔","Switch off did not hide its category or update its state");
        studySwitch.IsChecked=true; studySwitch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(window.todoPanel.VisibleTodos.Count==2 && ((Grid)studySwitch.Content).Children.OfType<TextBlock>().Last().Text=="켬","Switch on did not restore its category");
        window.todoPanel.SetAllCategories(true);
        Check(window.todoPanel.ListedCategories.Count()==3,"All-categories mode did not include empty categories");
        window.todoPanel.SetCategory("study",false); window.todoPanel.SetCategory("empty",false);
        Check(window.todoPanel.ListedCategories.Count()==1 && window.todoPanel.ListedCategories.Single().Id=="life","All-categories mode ignored personal exclusions");
        window.todoPanel.SetCategory("study",true); window.todoPanel.SetCategory("empty",true); window.todoPanel.SetAllCategories(false);
        window.calendars.ItemsSource=new[]{"캘린더"}; window.calendars.SelectedIndex=0; window.status.Text="디자인 검증용 샘플 일정";
        var content=(FrameworkElement)window.Content; window.Content=null;
        window.SetTransparency(20); LayoutForTest(content);
        Check(!Descendants<Button>(content).Any(b=>Object.Equals(b.Content,"월간") || Object.Equals(b.Content,"주간")),"View mode buttons are still visible");
        Check(!Descendants<Button>(content).Any(b=>AutomationProperties.GetName(b)=="최소화"),"Minimize button is still visible");
        Check(Descendants<GridSplitter>(content).Count()==1,"Todo pane has no resize divider");
        Check(Descendants<Thumb>(content).Count(t=>Convert.ToString(t.Tag).StartsWith("edge-resize:"))==8,"All edges and corners must resize the widget");
        double savedLeft=window.Left,savedTop=window.Top,savedWidth=window.Width,savedHeight=window.Height;
        window.ResizeEdges(-1,-1,20,20);
        Check(Math.Abs(window.Left+window.Width-savedLeft-savedWidth)<.1 && Math.Abs(window.Top+window.Height-savedTop-savedHeight)<.1,"Top-left resize moved the opposite corner");
        window.ResizeEdges(-1,-1,10000,10000);
        Check(window.Width==window.MinWidth && window.Height==window.MinHeight && Math.Abs(window.Left+window.Width-savedLeft-savedWidth)<.1,"Resize minimum bounds changed the fixed edge");
        window.Left=savedLeft; window.Top=savedTop; window.Width=savedWidth; window.Height=savedHeight;
        var checkbox=Descendants<CheckBox>(content).First(); checkbox.ApplyTemplate();
        var checkFrame=checkbox.Template.FindName("Box",checkbox) as Border;
        Check(checkFrame!=null && checkFrame.CornerRadius.TopLeft==6,"Task checkboxes still use the default theme");
        var scrollbar=Descendants<ScrollBar>(content).First(); scrollbar.ApplyTemplate();
        Check(scrollbar.Template.FindName("Grip",scrollbar) is Thumb,"Scrollbar did not use the themed thumb");
        Check(window.todoPanel.CategoryControls.TransformToAncestor(content).Transform(new Point()).Y < window.todoPanel.TransformToAncestor(content).Transform(new Point()).Y,"Category switches did not move to the former view selector position");
        Check(((SolidColorBrush)window.calendars.Background).Color.A<32,"Calendar selector is not transparent");
        Check(((SolidColorBrush)window.calendars.Foreground).Color.A==255,"Calendar selector text lost opacity");
        var popup=(Popup)window.calendars.Template.FindName("PART_Popup",window.calendars);
        Check(popup!=null && popup.AllowsTransparency && ((SolidColorBrush)((Border)popup.Child).Background).Color.A<255,"Calendar dropdown is not transparent");
        window.calendars.ItemsSource=new[]{"캘린더","보조 캘린더"}; window.calendars.SelectedIndex=1; Render(content);
        Check(Descendants<ContentPresenter>(window.calendars).Any(p=>Object.Equals(p.Content,"보조 캘린더")),"Transparent selector did not display the selected calendar");
        window.calendars.SelectedIndex=0; Render(content);
        Check(Descendants<Button>(window.calendar).Any(b=>b.Tag is D),"Month event was not laid out");
        var button=Descendants<Button>(window.calendar).First(b=>b.Tag is D); var originalColor=((SolidColorBrush)button.Background).Color;
        window.awaitingInitialConnections=true; window.SetTransparency(80);
        Check(((SolidColorBrush)window.plate.Background).Color.A==255 && N(window.State(),"transparency")==80,"First login must keep the background opaque without replacing the saved transparency");
        window.awaitingInitialConnections=false; window.SetTransparency(80); var image=Render(content);
        button=Descendants<Button>(window.calendar).First(b=>b.Tag is D);
        Check(window.Opacity==1,"Window opacity must stay at one"); Check(window.brand.Opacity==1,"Text must stay opaque");
        Check(((SolidColorBrush)window.brand.Foreground).Color.A==255,"Text brush lost opacity");
        Check(((SolidColorBrush)button.Background).Color==originalColor && originalColor.A==255,"Event color lost opacity");
        Check(((SolidColorBrush)window.plate.Background).Color.A==51,"Background alpha did not change independently");
        Check(window.plate.Opacity==1,"Plate must use brush alpha, not subtree opacity");
        var point=button.TransformToAncestor(content).Transform(new Point(10,10));
        point+=VisualTreeHelper.GetOffset(content);
        var pixels=new byte[4]; image.CopyPixels(new Int32Rect((int)point.X,(int)point.Y,1,1),pixels,4,0);
        WritePng(image,System.IO.Path.ChangeExtension(report,"png"));
        Check(pixels[3]==255,"Rendered event pixels must stay opaque: "+String.Join(",",pixels)+" at "+point+" size "+button.ActualWidth+"x"+button.ActualHeight);
        window.SetTransparency(65); image=Render(content); WritePng(image,System.IO.Path.ChangeExtension(report,"png"));
        window.Save(); var restored=new CalendarWindow("",false);
        Check(restored.backgroundTransparency==65 && restored.Opacity==1,"Background setting did not restore independently");
        var allWeeks=A(data,"weeks"); var firstWeek=((IEnumerable)allWeeks[0]).Cast<object>().ToList();
        for(int row=0;row<6;row++) firstWeek.Add(new D { {"first",4},{"last",4},{"row",row},{"event",A(data,"events")[0]} }); allWeeks[0]=firstWeek.ToArray(); data["weeks"]=allWeeks;
        window.SetModel(data); LayoutForTest(content);
        var collapsed=Descendants<Grid>(window.calendar).Single(g=>g.Tag is DateTime && (DateTime)g.Tag==new DateTime(2026,11,1));
        var collapsedEvents=Descendants<Canvas>(collapsed).Single(c=>Object.Equals(c.Tag,"month-events"));
        double dayWidth=collapsedEvents.ActualWidth/7;
        Check(collapsedEvents.Children.OfType<Button>().Count(b=>Math.Abs(Canvas.GetLeft(b)-2*dayWidth)<.01)==2,"A busy Thursday hid a second Tuesday event");
        Check(Descendants<Canvas>(collapsed).Single(c=>Object.Equals(c.Tag,"month-more")).Children.OfType<Button>().Count()==1,"More buttons appeared on days that already fit");
        window.ExpandWeek(new DateTime(2026,11,1)); LayoutForTest(content);
        var expanded=Descendants<Grid>(window.calendar).Single(g=>g.Tag is DateTime && (DateTime)g.Tag==new DateTime(2026,11,1));
        Check(expanded.ActualHeight>=34+6*24,"Expanded week did not grow to fit all rows");
        Check(Descendants<Canvas>(expanded).Single(c=>Object.Equals(c.Tag,"month-events")).Children.OfType<Button>().Count()==firstWeek.Count,"Expanded week still hides events");
        window.ExpandWeek(new DateTime(2026,11,1)); LayoutForTest(content); Check(!window.expandedWeekStart.HasValue,"Week did not collapse");
        var longLabel=new string('가',80); var longPanel=new TodoPanel(new DateTime(2026,11,3),delegate {},false);
        longPanel.ApplyData(new WeekData { Goals=new List<Goal> { new Goal { Id="long",Title=longLabel } },Todos=new List<Todo> { new Todo { Id="long-item",GoalId="long",Content="길이 검증",Date=new DateTime(2026,11,3) } } },new DateTime(2026,11,1));
        var toggle=((StackPanel)longPanel.CategoryPreferences()).Children.OfType<ToggleButton>().Single();
        toggle.Measure(new Size(Double.PositiveInfinity,Double.PositiveInfinity));
        Check(toggle.DesiredSize.Width<=320 && Convert.ToString(toggle.ToolTip).Contains(longLabel) && AutomationProperties.GetName(toggle).Contains(longLabel),"Long categories overflow or lose their full accessible name");
        var today=Today(); longPanel.ReportReadError("오늘 할 일 조회 한도를 넘었습니다. 내일 다시 시도해주세요.");
        Check(longPanel.VisibleTodos.Count==1,"A quota error discarded already loaded tasks");
        double alpha=window.backgroundTransparency; bool pinned=window.Topmost; bool rejected=false;
        try { window.HandleCommand(new D { {"op","settings"},{"transparency",45},{"pin","invalid"} }); } catch(ArgumentException) { rejected=true; }
        Check(rejected && window.backgroundTransparency==alpha && window.Topmost==pinned,"Invalid CLI settings partially applied");
        var refused=WidgetCommands.Execute(window.HandleCommand,new D { {"op","unsupported"} });
        Check(!B(refused,"ok") && S(refused,"error")!="","A rejected CLI command escaped the UI exception boundary");
        refused=WidgetCommands.Execute(window.HandleCommand,new D { {"op","settings"},{"view","week"} });
        Check(!B(refused,"ok") && S(window.State(),"view")=="month","Removed weekly mode is still available through commands");
        var state=window.HandleCommand(new D { {"op","settings"},{"transparency",45},{"pin",false} });
        Check(N(state,"transparency")==45 && !B(state,"pin"),"CLI settings did not apply");
        state=window.HandleCommand(new D { {"op","settings"},{"pin_back",true} });
        Check(B(state,"pin_back") && !B(state,"pin") && window.backSwitch.IsChecked==true,"Back pin did not enable");
        window.Save();
        var restoredLayer=new CalendarWindow("",false);
        Check(restoredLayer.pinBack && !restoredLayer.Topmost,"Back pin did not survive restart");
        restoredLayer.Close();
        state=window.HandleCommand(new D { {"op","settings"},{"pin",true} });
        Check(B(state,"pin") && !B(state,"pin_back") && window.backSwitch.IsChecked==false,"Top pin did not release back pin");
        refused=WidgetCommands.Execute(window.HandleCommand,new D { {"op","settings"},{"pin",true},{"pin_back",true} });
        Check(!B(refused,"ok") && window.Topmost && !window.pinBack,"Conflicting layer settings partially applied");
        window.SetWindowLayer(false,false);
        window.lastToday=window.selected=new DateTime(2026,11,3);
        Check(window.FollowToday(new DateTime(2026,11,4)) && window.selected==new DateTime(2026,11,4),"Midnight did not follow today");
        window.selected=new DateTime(2026,10,20); window.FollowToday(new DateTime(2026,11,5));
        Check(window.selected==new DateTime(2026,10,20),"Midnight overwrote a manually selected past date");
        var goals=new List<Goal> { new Goal { Id="g",Title="연구" } };
        var past=new Todo { Id="past",Content="이전 달 보존",GoalId="g",Date=new DateTime(2026,10,10) };
        var removed=new Todo { Id="removed",Content="삭제될 항목",GoalId="g",Date=new DateTime(2026,11,3) };
        TodoSnapshots.SaveMonth(new WeekData { Goals=goals,Todos=new List<Todo> { past,removed } },new DateTime(2026,10,1),new DateTime(2026,9,27),new DateTime(2026,11,7));
        var changed=new Todo { Id="changed",Content="새 항목",GoalId="g",Date=new DateTime(2026,11,3) };
        TodoSnapshots.SaveMonth(new WeekData { Goals=goals,Todos=new List<Todo> { changed } },new DateTime(2026,11,1),new DateTime(2026,11,1),new DateTime(2026,12,5));
        var octoberTodos=TodoSnapshots.Read(new DateTime(2026,10,10));
        Check(octoberTodos!=null && octoberTodos.Data.Todos.Any(t=>t.Id=="past") && !octoberTodos.Data.Todos.Any(t=>t.Id=="removed") && octoberTodos.Data.Todos.Any(t=>t.Id=="changed"),"Refreshing November lost October data or left deleted overlapping tasks");
        changed.Done=true; TodoSnapshots.Patch(changed);
        Check(TodoSnapshots.Read(changed.Date).Data.Todos.Single(t=>t.Id=="changed").Done,"Local completion did not persist");
        string ownKey=TodoSnapshots.AccountKey;
        TodoSnapshots.AccountKey="different-account";
        Check(TodoSnapshots.Read(changed.Date)==null,"Another account could read the previous user's cache");
        TodoSnapshots.AccountKey=ownKey;
        var moved=new Todo { Id="move-check",Content="이동 검증",GoalId="g",Date=new DateTime(2026,11,3) };
        TodoSnapshots.Patch(moved); moved.Date=new DateTime(2026,10,10); TodoSnapshots.Patch(moved);
        Check(TodoSnapshots.Read(moved.Date).Data.Todos.Count(t=>t.Id==moved.Id)==1 && !TodoSnapshots.Read(new DateTime(2026,11,3)).Data.Todos.Any(t=>t.Id==moved.Id),"Moving a task left stale cache entries");
        TodoSnapshots.Remove(moved.Id);
        Check(!TodoSnapshots.Read(moved.Date).Data.Todos.Any(t=>t.Id==moved.Id),"Deleted task survived in cache");
        var restoredPanel=new TodoPanel(changed.Date,delegate {},false); restoredPanel.ReloadCached();
        Check(restoredPanel.VisibleTodos.Single().Done,"A reopened panel did not restore tasks from disk");
        var sameTodo=restoredPanel.VisibleTodos.Single(); var sameSaved=TodoSnapshots.Read(changed.Date);
        restoredPanel.ApplyData(sameSaved.Data,sameSaved.Start,sameSaved.End);
        Check(Object.ReferenceEquals(sameTodo,restoredPanel.VisibleTodos.Single()),"An unchanged refresh replaced objects still referenced by checkbox handlers");
        sameSaved.Data.Source="mcp"; sameSaved.Data.Notice="Firebase 조회 제한 · MCP 보조 조회";
        TodoSnapshots.SaveMonth(sameSaved.Data,changed.Date,sameSaved.Start,sameSaved.End);
        Check(TodoSnapshots.Read(changed.Date).Data.Source=="mcp","Cached MCP source was lost on restart");
        var october=Json.Deserialize<D>(Json.Serialize(data)); october["period"]="2026년 10월";
        october["days"]=Enumerable.Range(0,35).Select(i=>new DateTime(2026,9,27).AddDays(i).ToString("yyyy-MM-dd")).ToArray();
        window.calendarId="cache-fixture"; window.anchor=window.selected=new DateTime(2026,10,1);
        SnapshotStore.Write("timetree","cache-fixture|2026-10",Json.Serialize(new { model=october,warning="" }));
        int previous=window.generation; var watch=Stopwatch.StartNew(); window.Refresh(); watch.Stop();
        Check(window.backend==null && window.period.Text=="2026년 10월" && watch.ElapsedMilliseconds<1000,"Cached month navigation waited for the backend");
        Check(window.status.Text=="","Cached calendar shows an unnecessary saved-schedule message");
        var originalHeader=window.calendar.Children[0]; window.ApplyView(october);
        Check(Object.ReferenceEquals(originalHeader,window.calendar.Children[0]),"Unchanged snapshots rebuilt the calendar");
        window.Receive(new D { {"kind","view"},{"id",previous},{"model",data} });
        Check(window.period.Text=="2026년 10월","An old November response replaced the cached October view");
        window.calendarId="removed-calendar";
        window.Receive(new D { {"kind","calendars"},{"calendars",new[]{new D { {"id","cache-fixture"},{"name","저장된 캘린더"} }}} });
        Check(window.calendarId=="cache-fixture" && window.period.Text=="2026년 10월" && window.backend==null,"Changing the server calendar list did not select and restore the matching cached calendar");
        window.SetSyncMinutes(0); Check(window.syncMinutes==0,"Manual sync mode did not apply");
        window.SetTodoWidth(400); Render(content);
        Check(Math.Abs(window.todoColumn.ActualWidth-400)<2 && window.calendarColumn.ActualWidth>=420,"Resizing Todo Mate did not preserve calendar space");
        Check(window.Save(),"Pane width could not be saved");
        var reopened=new CalendarWindow("",false); Check(reopened.todoColumn.Width.Value==400,"Pane width was not restored on restart"); reopened.todoPanel.Dispose();
        window.SetTransparency(25); window.Topmost=true; window.todoPanel.SetCategory("life",false); window.todoPanel.SetAllCategories(true);
        window.ApplyRecommended(); Render(content);
        Check(window.backgroundTransparency==65 && !window.Topmost && window.syncMinutes==30 && window.todoWidth==300,"Recommended visual settings were not restored");
        Check(window.todoPanel.PreferenceValues()["ShowAllCategories"].Equals(false) && ((IEnumerable)window.todoPanel.PreferenceValues()["HiddenGoals"]).Cast<object>().Count()==0,"Recommended category preferences were not restored");
        window.awaitingInitialConnections=true;
        Check(window.ShouldPromptForAccounts,"A fresh installation must offer account setup");
        window.connectionBusy=true;
        try { window.DeferAccountSetup(); throw new Exception("Account setup was skipped while connecting"); }
        catch(InvalidOperationException) { Check(!B(window.prefs,"account_setup_deferred"),"A rejected skip was persisted"); }
        finally { window.connectionBusy=false; }
        var savedAuth=SnapshotStore.ReadFile(AppPaths.TreeAuth).Payload;
        window.DeferAccountSetup(); window.SetTransparency(65);
        Check(!window.ShouldPromptForAccounts && ((SolidColorBrush)window.plate.Background).Color.A==255 && window.backgroundTransparency==65,"Skipping account setup changed transparency or still prompts");
        var deferredWindow=new CalendarWindow("",false); deferredWindow.awaitingInitialConnections=true;
        Check(!deferredWindow.ShouldPromptForAccounts && SnapshotStore.ReadFile(AppPaths.TreeAuth).Payload==savedAuth,"Deferred setup did not persist or changed credentials");
        var accountPage=(StackPanel)deferredWindow.AccountSettingsPage();
        var guides=accountPage.Children.OfType<DockPanel>().SelectMany(p=>p.Children.OfType<Button>()).Where(b=>AutomationProperties.GetName(b).EndsWith(" 연결 방법")).ToList();
        Check(guides.Count==3,"Each account provider needs connection instructions");
        var details=accountPage.Children.OfType<Border>().ToList();
        Check(details.Count==3 && details.All(b=>b.Visibility==Visibility.Collapsed),"Connection guides must start compact");
        guides[2].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(details[2].Visibility==Visibility.Visible && ((TextBlock)details[2].Child).Text.Contains("10분"),"PlayMCP instructions did not expand");
        guides[2].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(details[2].Visibility==Visibility.Collapsed,"PlayMCP guide did not collapse");
        deferredWindow.todoPanel.Dispose();
        File.WriteAllText(report,Json.Serialize(new { passed=true, windowOpacity=window.Opacity, textAlpha=255,eventPixelAlpha=pixels[3], backgroundAlphaAt80=51, preferencesRestored=true, viewModeRemoved=true, categorySwitchesInHeader=true, expandedWeekShowsAllEvents=true, twoEventsAlwaysVisible=true, calendarSelectorTransparent=true, todoDateLinked=true, todoCategoriesFilter=true, emptyCategoriesHidden=true, orphanedCategoriesHidden=true, todoAdditionCompletionPayloadsVerified=true, longCategoriesFit=true, cliSettingsValidated=true, midnightFollowsToday=true, pastDateSelectionPreserved=true, monthCacheRestored=true, todoCacheRestored=true, cachedNavigationIgnoresOldResponses=true, unchangedDataKeepsControls=true, cacheTestRoot=SnapshotStore.Root }),Encoding.UTF8);
        // The test does not close or save a production window or settings.
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle,int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr handle);
    [STAThread] public static int Main(string[] args) {
        Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("ko-KR");
        System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
        try {
            if(args.Length==3 && args[0]=="--self-test") { SelfTest(args[1],args[2]); return 0; }
            bool first; using(var mutex=new Mutex(true,"Local\\TimeTreeDesktopWidgetNative"+AppPaths.InstanceSuffix,out first)) {
                if(!first) { var handle=FindWindow(null,"TimeTree · 바탕화면 캘린더"); if(handle!=IntPtr.Zero) { ShowWindow(handle,9); SetForegroundWindow(handle); } return 0; }
                var app=new Application(); app.Run(new CalendarWindow(args.Length>0 ? args[0] : AppPaths.Python,true));
            }
            return 0;
        } catch(Exception e) {
            if(args.Length==3 && args[0]=="--self-test") File.WriteAllText(args[2],e.ToString(),Encoding.UTF8);
            else MessageBox.Show("위젯을 시작하지 못했습니다.\n"+e.Message,"TimeTree");
            return 1;
        }
    }
}
