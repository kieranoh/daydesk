using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

sealed class TodoDialog : Window {
    public DateTime SavedDate { get; private set; }
    public string SaveWarning { get; private set; }
    internal bool Attempted;
    bool saving;
    public TodoDialog(Todo todo,Goal goal,DateTime selected,MateClient client,List<Goal> goals) {
        bool adding=todo==null; SavedDate=selected;
        var fields=new StackPanel();
        var category=new ComboBox { ItemsSource=goals.Select(g=>new Category { Goal=g }).ToList() };
        CalendarWindow.StyleCalendarSelector(category,true); category.SelectedIndex=goals.FindIndex(g=>g.Id==goal.Id);
        AutomationProperties.SetName(category,"할 일 카테고리");
        var categoryRow=new DockPanel { Margin=new Thickness(0,0,0,10) };
        categoryRow.Children.Add(new Label { Content="카테고리",Target=category,Foreground=Theme.Muted,VerticalAlignment=VerticalAlignment.Center,Padding=new Thickness(0,0,12,0) });
        category.Background=System.Windows.Media.Brushes.White; category.BorderThickness=new Thickness(0); category.Height=34; category.FontWeight=FontWeights.SemiBold; categoryRow.Children.Add(category); fields.Children.Add(categoryRow);
        var content=new TextBox { AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinLines=1,MaxLines=3,FontSize=15,Padding=new Thickness(2,4,2,4),Background=System.Windows.Media.Brushes.White,BorderThickness=new Thickness(0,0,0,1),Text=adding ? "" : todo.Content };
        fields.Children.Add(new Label { Content="할 일",Target=content,Foreground=Theme.Muted,Padding=new Thickness(0),Margin=new Thickness(0,0,0,4) });
        content.Margin=new Thickness(0,0,0,12); fields.Children.Add(content); AutomationProperties.SetName(content,"할 일 내용");
        var date=WidgetTheme.Date(selected); var dateRow=new DockPanel { Margin=new Thickness(0,0,0,10) };
        dateRow.Children.Add(new Label { Content="날짜",Target=date,Foreground=Theme.Muted,VerticalAlignment=VerticalAlignment.Center,Padding=new Thickness(0,0,22,0) }); date.Height=40; dateRow.Children.Add(date); fields.Children.Add(dateRow); AutomationProperties.SetName(date,"할 일 날짜");
        var reminder=new CheckBox { Content="알림 설정",IsChecked=!adding && !string.IsNullOrWhiteSpace(todo.Reminder),Margin=new Thickness(0,0,0,8) }; fields.Children.Add(reminder);
        DateTime when=selected.AddHours(9);
        if(!adding && !string.IsNullOrWhiteSpace(todo.Reminder)) DateTime.TryParse(todo.Reminder,out when);
        if(when.Year<1900) when=selected.AddHours(9);
        var reminderRow=new Grid(); reminderRow.ColumnDefinitions.Add(new ColumnDefinition()); reminderRow.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(94) });
        var reminderDate=WidgetTheme.Date(when); var reminderTime=new TextBox { Text=when.ToString("HH:mm"),Margin=new Thickness(8,0,0,0),VerticalContentAlignment=VerticalAlignment.Center };
        AutomationProperties.SetName(reminderDate,"알림 날짜"); AutomationProperties.SetName(reminderTime,"알림 시간 HH:mm");
        Grid.SetColumn(reminderTime,1); reminderRow.Children.Add(reminderDate); reminderRow.Children.Add(reminderTime); reminderRow.Visibility=reminder.IsChecked==true ? Visibility.Visible : Visibility.Collapsed; fields.Children.Add(reminderRow);
        bool reminderChanged=false;
        reminder.Checked+=delegate { reminderChanged=true; reminderRow.Visibility=Visibility.Visible; }; reminder.Unchecked+=delegate { reminderChanged=true; reminderRow.Visibility=Visibility.Collapsed; };
        reminderDate.SelectedDateChanged+=delegate { reminderChanged=true; }; reminderTime.TextChanged+=delegate { reminderChanged=true; };
        var footer=new StackPanel(); var message=Theme.Text("변경 사항은 Todo Mate 계정에 바로 저장됩니다.",12); message.TextWrapping=TextWrapping.Wrap; message.Margin=new Thickness(0,0,0,10); footer.Children.Add(message);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right }; footer.Children.Add(actions);
        var cancel=Theme.Button("취소",Close); actions.Children.Add(cancel);
        var save=Theme.Button(adding ? "추가" : "저장",delegate {}); save.Background=Theme.Green; save.Foreground=Theme.Brush("#17232E"); save.MinWidth=76; actions.Children.Add(save);
        WidgetTheme.Dialog(this,adding ? "할 일 추가" : "할 일 수정",fields,footer,460,true);
        Loaded+=delegate { content.Focus(); content.CaretIndex=content.Text.Length; };
        Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(saving) e.Cancel=true; };
        PreviewKeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Enter && Keyboard.Modifiers==ModifierKeys.Control) { save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled=true; } };
        save.ToolTip="Ctrl+Enter로 저장";
        save.Click+=async delegate {
            if(saving || !save.IsEnabled) return;
            TimeSpan time;
            if(string.IsNullOrWhiteSpace(content.Text) || category.SelectedItem==null || !date.SelectedDate.HasValue) { message.Foreground=Theme.Brush("#B23B49"); message.Text="내용·카테고리·날짜를 확인해 주세요."; return; }
            if(reminder.IsChecked==true && (!reminderDate.SelectedDate.HasValue || !TimeSpan.TryParseExact(reminderTime.Text,"hh\\:mm",CultureInfo.InvariantCulture,out time) || time.TotalHours>=24)) { message.Foreground=Theme.Brush("#B23B49"); message.Text="알림 날짜와 시간을 확인해 주세요. 시간은 HH:mm 형식입니다."; return; }
            DateTime? remindAt=reminder.IsChecked==true ? (DateTime?)reminderDate.SelectedDate.Value.Add(TimeSpan.ParseExact(reminderTime.Text,"hh\\:mm",CultureInfo.InvariantCulture)) : null;
            saving=true; Attempted=true; save.IsEnabled=cancel.IsEnabled=fields.IsEnabled=false; message.Text="저장 중…"; bool acknowledged=false;
            try {
                using(await TodoSnapshots.Enter()) {
                    var chosen=((Category)category.SelectedItem).Goal;
                    Todo updated=adding ? await client.Add(chosen.Id,content.Text,date.SelectedDate.Value.Date,false,remindAt) :
                        await client.Update(todo.Id,content.Text,date.SelectedDate.Value.Date,chosen.Id,null,reminderChanged,remindAt);
                    acknowledged=true; SavedDate=updated.Date;
                    try { TodoSnapshots.Patch(updated); if(TodoSnapshots.Read(updated.Date)==null) TodoSnapshots.SaveDay(await client.LoadRange(updated.Date,updated.Date),updated.Date); }
                    catch { SaveWarning="서버 저장은 완료되었습니다. 캐시 갱신을 위해 새로고침해 주세요."; }
                }
                saving=false; DialogResult=true;
            } catch(Exception error) {
                SaveWarning=message.Text=(acknowledged ? "서버 저장은 완료되었습니다. " : "새로고침으로 저장 여부를 확인해 주세요. ")+error.Message;
                message.Foreground=Theme.Brush("#B23B49"); cancel.Content="닫기"; cancel.IsEnabled=true;
                // A timed-out write may have committed; close and refresh before another submission.
            } finally { saving=false; }
        };
    }
    sealed class Category { internal Goal Goal; public override string ToString() { return string.IsNullOrWhiteSpace(Goal.Title) ? "기본" : Goal.Title; } }
}
