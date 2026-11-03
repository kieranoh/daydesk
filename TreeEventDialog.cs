using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using D=System.Collections.Generic.Dictionary<string,object>;

sealed class TreeEventDialog : Window {
    internal DateTime SavedDate;
    internal string SaveWarning;
    internal bool Attempted;
    internal Action<object> UpdateLabels;
    bool saving;
    static string Text(D d,string key) { object value; return d.TryGetValue(key,out value) && value!=null ? Convert.ToString(value,CultureInfo.InvariantCulture) : ""; }
    internal TreeEventDialog(D info,Func<D,Task<D>> write,bool deleteIntent=false) {
        var form=(D)info["form"]; bool adding=info["event"]==null,recurring=Equals(info["recurring"],true);
        var fields=new StackPanel();
        var title=new TextBox { Text=Text(form,"title"),FontSize=17,MinHeight=38,VerticalContentAlignment=VerticalAlignment.Center,Padding=new Thickness(2,6,2,6),Background=System.Windows.Media.Brushes.White,BorderThickness=new Thickness(0,0,0,1) };
        var titleRow=new DockPanel { Margin=new Thickness(0,0,0,12) }; titleRow.Children.Add(new Label { Content="제목",Target=title,Width=36,Padding=new Thickness(0),Foreground=Theme.Muted,VerticalAlignment=VerticalAlignment.Center }); titleRow.Children.Add(title); fields.Children.Add(titleRow); AutomationProperties.SetName(title,"일정 제목");
        var labels=new ComboBox { Height=34 }; CalendarWindow.StyleCalendarSelector(labels,true);
        int selectedLabel=Convert.ToInt32(form["label_id"]);
        bool fillingLabels=false;
        UpdateLabels=delegate(object values) {
        fillingLabels=true; labels.Items.Clear();
        foreach(D label in ((IEnumerable)values).Cast<D>()) {
            object color; if(!label.TryGetValue("color",out color) || color==null) label.TryGetValue("default_color",out color);
            int id=Convert.ToInt32(label["id"]); string name=string.IsNullOrWhiteSpace(Text(label,"name")) ? "라벨 "+id : Text(label,"name");
            var line=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left };
            line.Children.Add(new Border { Width=10,Height=10,CornerRadius=new CornerRadius(3),Background=Theme.Brush("#"+(Convert.ToInt64(color ?? 0)&0xFFFFFF).ToString("X6")),Margin=new Thickness(0,0,8,0) });
            var nameText=new TextBlock { Text=name,Foreground=Theme.Brush("#25272B"),FontSize=13,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=230 }; line.Children.Add(nameText);
            var item=new ComboBoxItem { Content=line,Tag=id,ToolTip=name,HorizontalContentAlignment=HorizontalAlignment.Left }; AutomationProperties.SetName(item,name); labels.Items.Add(item);
            if(id==selectedLabel) labels.SelectedItem=item;
        }
        if(labels.SelectedIndex<0) { var current=new ComboBoxItem { Content="라벨 "+selectedLabel,Tag=selectedLabel }; labels.Items.Add(current); labels.SelectedItem=current; }
        fillingLabels=false;
        };
        UpdateLabels(info["labels"]);
        labels.SelectionChanged+=delegate { if(!fillingLabels && labels.SelectedItem!=null) selectedLabel=Convert.ToInt32(((ComboBoxItem)labels.SelectedItem).Tag); };
        var labelRow=new DockPanel { Margin=new Thickness(0,0,0,12) }; labelRow.Children.Add(new Label { Content="라벨",Target=labels,Width=36,Padding=new Thickness(0),Foreground=Theme.Muted,VerticalAlignment=VerticalAlignment.Center }); labelRow.Children.Add(labels); fields.Children.Add(labelRow); AutomationProperties.SetName(labels,"캘린더 라벨");
        var dates=new StackPanel(); fields.Children.Add(new Border { Background=Theme.Brush("#F7FAF8"),CornerRadius=new CornerRadius(10),Padding=new Thickness(12),Margin=new Thickness(0,0,0,14),Child=dates });
        var dateHeading=new DockPanel { Margin=new Thickness(0,0,0,8) }; dates.Children.Add(dateHeading);
        var allDay=new CheckBox { Content="종일",IsChecked=Equals(form["all_day"],true),IsEnabled=!recurring,VerticalAlignment=VerticalAlignment.Center };
        if(recurring) allDay.ToolTip="반복 회차의 종일 여부는 원본 TimeTree에서 변경해 주세요.";
        DockPanel.SetDock(allDay,Dock.Right); dateHeading.Children.Add(allDay); dateHeading.Children.Add(Theme.Text("날짜·시간",14,true));
        var start=WidgetTheme.Date(DateTime.ParseExact(Text(form,"start_date"),"yyyy-MM-dd",CultureInfo.InvariantCulture));
        var end=WidgetTheme.Date(DateTime.ParseExact(Text(form,"end_date"),"yyyy-MM-dd",CultureInfo.InvariantCulture));
        var startTime=new TextBox { Text=Text(form,"start_time"),VerticalContentAlignment=VerticalAlignment.Center };
        var endTime=new TextBox { Text=Text(form,"end_time"),VerticalContentAlignment=VerticalAlignment.Center };
        Action<string,DatePicker,TextBox> dateRow=delegate(string name,DatePicker picker,TextBox time) {
            var row=new Grid { Margin=new Thickness(0,3,0,3) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(36) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); time.Width=78;
            row.Children.Add(new Label { Content=name,Target=picker,Foreground=Theme.Muted,Padding=new Thickness(0),VerticalAlignment=VerticalAlignment.Center });
            picker.Height=40; time.Margin=new Thickness(6,0,0,0); time.Padding=new Thickness(6); Grid.SetColumn(picker,1); Grid.SetColumn(time,2); row.Children.Add(picker); row.Children.Add(time); dates.Children.Add(row);
            AutomationProperties.SetName(picker,name+" 날짜"); AutomationProperties.SetName(time,name+" 시간 HH:mm");
        };
        dateRow("시작",start,startTime); dateRow("종료",end,endTime);
        Action showTimes=delegate { startTime.Visibility=endTime.Visibility=allDay.IsChecked==true ? Visibility.Collapsed : Visibility.Visible; }; allDay.Checked+=delegate { showTimes(); }; allDay.Unchecked+=delegate { showTimes(); }; showTimes();
        var details=new StackPanel { Visibility=Visibility.Collapsed };
        string moreLabel=string.IsNullOrEmpty(Text(form,"location")+Text(form,"note")+Text(form,"url")) ? "장소·메모·URL 추가 ▾" : "장소·메모·URL 보기 ▾";
        var more=Theme.Button(moreLabel,delegate { details.Visibility=details.Visibility==Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }); more.HorizontalAlignment=HorizontalAlignment.Left; more.Margin=new Thickness(0,0,0,6); fields.Children.Add(more); fields.Children.Add(details);
        more.Click+=delegate { more.Content=details.Visibility==Visibility.Visible ? "추가 정보 접기 ▴" : moreLabel; };
        var location=new TextBox { Text=Text(form,"location") }; WidgetTheme.Field(details,"장소",location);
        var note=new TextBox { Text=Text(form,"note"),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=72 }; WidgetTheme.Field(details,"메모",note);
        var url=new TextBox { Text=Text(form,"url") }; WidgetTheme.Field(details,"URL",url);
        var footer=new StackPanel(); var message=Theme.Text(recurring ? "반복 일정 · 선택한 회차만 변경합니다." : "시간은 한국 기준이며, 변경 사항은 TimeTree에 저장됩니다.",12); message.TextWrapping=TextWrapping.Wrap; message.Margin=new Thickness(0,0,0,10); footer.Children.Add(message);
        var actions=new DockPanel(); footer.Children.Add(actions);
        var right=new StackPanel { Orientation=Orientation.Horizontal }; DockPanel.SetDock(right,Dock.Right); actions.Children.Add(right);
        var cancel=Theme.Button("취소",Close); right.Children.Add(cancel);
        var save=Theme.Button(adding ? "추가" : "저장",delegate {}); save.Background=Theme.Green; save.Foreground=Theme.Brush("#17232E"); save.MinWidth=70; save.ToolTip="Ctrl+Enter로 저장"; right.Children.Add(save);
        var delete=Theme.Button("삭제",delegate {}); delete.Foreground=Theme.Brush("#B23B49"); delete.HorizontalAlignment=HorizontalAlignment.Left; delete.Visibility=adding ? Visibility.Collapsed : Visibility.Visible; actions.Children.Add(delete);
        WidgetTheme.Dialog(this,adding ? "일정 추가" : "일정 편집",fields,footer,540);
        more.Background=System.Windows.Media.Brushes.Transparent; more.Foreground=Theme.Brush("#168B56"); more.Padding=new Thickness(0,8,0,8);
        delete.Background=System.Windows.Media.Brushes.Transparent; delete.Foreground=Theme.Brush("#B23B49");
        Loaded+=delegate { title.Focus(); title.CaretIndex=title.Text.Length; };
        Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(saving) e.Cancel=true; };
        PreviewKeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Enter && Keyboard.Modifiers==ModifierKeys.Control) { save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled=true; } };
        Func<bool,Task> submit=async delegate(bool removing) {
            if(saving || !save.IsEnabled) return;
            TimeSpan a,b;
            if(!removing && (string.IsNullOrWhiteSpace(title.Text) || !start.SelectedDate.HasValue || !end.SelectedDate.HasValue || end.SelectedDate<start.SelectedDate ||
                (allDay.IsChecked!=true && (!TimeSpan.TryParseExact(startTime.Text,"hh\\:mm",CultureInfo.InvariantCulture,out a) || !TimeSpan.TryParseExact(endTime.Text,"hh\\:mm",CultureInfo.InvariantCulture,out b) || a.TotalHours>=24 || b.TotalHours>=24 || end.SelectedDate.Value.Add(b)<=start.SelectedDate.Value.Add(a))))) {
                message.Foreground=Theme.Brush("#B23B49"); message.Text="제목과 시작·종료 날짜를 확인해 주세요. 시간은 HH:mm 형식입니다."; return;
            }
            var request=new D { {"action",removing ? "event-delete" : adding ? "event-add" : "event-update"},{"event",info["event"]},{"version",info["version"]},{"confirm_delete",removing},
                {"form",new D { {"title",title.Text},{"all_day",allDay.IsChecked==true},{"start_date",start.SelectedDate.HasValue ? start.SelectedDate.Value.ToString("yyyy-MM-dd") : ""},{"end_date",end.SelectedDate.HasValue ? end.SelectedDate.Value.ToString("yyyy-MM-dd") : ""},
                    {"start_time",startTime.Text},{"end_time",endTime.Text},{"label_id",selectedLabel},{"location",location.Text},{"note",note.Text},{"url",url.Text} }} };
            if(info.ContainsKey("original_form")) request["original_form"]=info["original_form"];
            saving=true; Attempted=true; fields.IsEnabled=save.IsEnabled=cancel.IsEnabled=delete.IsEnabled=false; message.Text=removing ? "삭제 중…" : "저장 중…";
            try {
                var result=await write(request); SavedDate=DateTime.ParseExact(Text(result,"date"),"yyyy-MM-dd",CultureInfo.InvariantCulture); SaveWarning=Text(result,"cache_warning");
                saving=false; DialogResult=true;
            } catch(Exception error) {
                message.Foreground=Theme.Brush("#B23B49"); message.Text=error.Message; cancel.Content="닫기"; cancel.IsEnabled=true;
            } finally { saving=false; }
        };
        DateTime confirmUntil=DateTime.MinValue;
        Action confirm=delegate { confirmUntil=DateTime.UtcNow.AddSeconds(10); delete.Content="삭제 확인"; message.Foreground=Theme.Brush("#B23B49"); message.Text=(recurring ? "선택한 반복 회차" : "이 일정")+"를 TimeTree에서도 삭제하시겠습니까? 한 번 더 누르면 삭제됩니다."; };
        delete.Click+=async delegate { if(DateTime.UtcNow>confirmUntil) { confirm(); return; } await submit(true); };
        save.Click+=async delegate { await submit(false); };
        if(deleteIntent && !adding) confirm();
    }
}

public partial class CalendarWindow {
    bool calendarEditing;
    async void EditTree(D item=null,bool deleteIntent=false) {
        if(calendarEditing || connectionBusy || todoPanel.HasPendingWrite) return;
        if(string.IsNullOrEmpty(calendarId)) { status.Text="설정에서 TimeTree 계정과 캘린더를 연결해 주세요."; return; }
        calendarEditing=true; TreeEventDialog dialog=null; string target=calendarId;
        try {
            status.Text="일정 편집 정보를 불러오는 중…";
            var info=await RunTreeRequest(new D { {"action","event-form"},{"calendar_id",target},{"event",item},{"date",selected.ToString("yyyy-MM-dd")},{"anchor",anchor.ToString("yyyy-MM-dd")} });
            dialog=new TreeEventDialog(info,request=> { request["calendar_id"]=target; return RunTreeRequest(request); },deleteIntent) { Owner=this };
            if(info.ContainsKey("labels_cached") && !B(info,"labels_cached")) dialog.Loaded+=async delegate {
                try { var labels=await RunTreeRequest(new D { {"action","event-labels"},{"calendar_id",target} }); if(dialog.IsVisible) dialog.UpdateLabels(labels["labels"]); }
                catch { if(dialog.IsVisible) status.Text="라벨을 불러오지 못했습니다. 현재 라벨은 유지됩니다."; }
            };
            bool saved=dialog.ShowDialog()==true;
            calendarEditing=false;
            if(saved) { selected=anchor=dialog.SavedDate; expandedWeekStart=null; Refresh(!string.IsNullOrEmpty(dialog.SaveWarning)); if(!string.IsNullOrEmpty(dialog.SaveWarning)) status.Text=dialog.SaveWarning; }
            else if(dialog.Attempted) Refresh(true);
            else status.Text="";
        } catch(Exception error) { status.Text=error.Message; }
        finally { calendarEditing=false; PlaceBehind(); }
    }
}
