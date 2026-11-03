using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D=System.Collections.Generic.Dictionary<string,object>;

static class DialogPreview {
    static IEnumerable<T> Children<T>(DependencyObject item) where T:DependencyObject {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++) { var child=VisualTreeHelper.GetChild(item,i); if(child is T) yield return (T)child; foreach(var nested in Children<T>(child)) yield return nested; }
    }
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    static void CheckDate(DatePicker date) {
        date.UpdateLayout(); var input=(DatePickerTextBox)date.Template.FindName("PART_TextBox",date);
        Check(input.ViewportWidth>=100 && input.ExtentWidth<=input.ViewportWidth+1,"Date text was clipped");
    }
    static void CheckTitle(Window window) {
        var input=Children<TextBox>(window).Single(b=>AutomationProperties.GetName(b)=="일정 제목");
        input.Text="예시 일정: 한글 Agpq 123"; input.CaretIndex=0; window.UpdateLayout();
        var host=(ScrollViewer)input.Template.FindName("PART_ContentHost",input);
        Check(host.ExtentHeight<=host.ViewportHeight+.5,"Title text was clipped vertically");
        var first=input.GetRectFromCharacterIndex(0);
        Check(first.Top>=0 && first.Bottom<=input.ActualHeight+.5,"Title glyph was outside its input");
    }
    [STAThread] static void Main(string[] args) {
        try { Run(args); }
        catch(Exception error) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dialog-error.txt"),error.ToString()); Environment.ExitCode=1; }
    }
    static void Run(string[] args) {
        Environment.SetEnvironmentVariable("DAYDESK_DATA_DIR",Path.Combine(Path.GetTempPath(),"daydesk-dialog-preview"));
        var assembly=Assembly.LoadFrom(args[0]);
        var goalType=assembly.GetType("Goal"); var goal=Activator.CreateInstance(goalType,true);
        goalType.GetField("Id").SetValue(goal,"fixture-goal"); goalType.GetField("Title").SetValue(goal,"연구");
        var goals=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(goalType)); goals.Add(goal);
        var app=new Application();
        var todo=(Window)Activator.CreateInstance(assembly.GetType("TodoDialog"),new object[] { null,goal,new DateTime(2026,11,3),null,goals });
        todo.Title="디자인 검증 · 할 일 추가"; todo.ShowInTaskbar=true; todo.Left=100; todo.Top=100; todo.WindowStartupLocation=WindowStartupLocation.Manual;
        var form=new D { {"title",""},{"all_day",true},{"start_date","2026-11-03"},{"end_date","2026-11-03"},{"start_time","09:00"},{"end_time","10:00"},{"label_id",2},{"note",""},{"location",""},{"url",""} };
        var info=new D { {"form",form},{"event",null},{"version",""},{"recurring",false},{"labels",new object[] { new D { {"id",2},{"name","연구"},{"color",0x2ecc87} } }} };
        Func<D,Task<D>> noWrite=request=> { throw new InvalidOperationException("이 창은 디자인 검증용이며 원격 저장을 하지 않습니다."); };
        var tree=(Window)Activator.CreateInstance(assembly.GetType("TreeEventDialog"),BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[] { info,noWrite,false },null);
        tree.Title="디자인 검증 · 일정 추가"; tree.ShowInTaskbar=true; tree.Left=550; tree.Top=100; tree.WindowStartupLocation=WindowStartupLocation.Manual;
        todo.Show(); tree.Show();
        if(args.Length>1 && args[1]=="--check") {
            todo.UpdateLayout(); tree.UpdateLayout();
            CheckTitle(tree);
            var content=Children<TextBox>(todo).Single(b=>AutomationProperties.GetName(b)=="할 일 내용");
            Check(content.MinLines==1 && content.MaxLines==3 && content.ActualHeight<44,"Task editor is not compact");
            Check(Children<CheckBox>(todo).Count()==1 && Children<CheckBox>(todo).All(b=>Convert.ToString(b.Content)=="알림 설정"),"Completion checkbox remained in composer");
            Check(todo.ActualHeight<400 && tree.ActualHeight<490,"Default dialogs are too large");
            foreach(var date in Children<DatePicker>(todo).Where(d=>d.IsVisible)) CheckDate(date);
            foreach(var date in Children<DatePicker>(tree)) CheckDate(date);
            var picker=Children<DatePicker>(tree).First(); picker.IsDropDownOpen=true; tree.UpdateLayout();
            var popup=(Popup)picker.Template.FindName("PART_Popup",picker);
            var calendar=popup.Child as Calendar; calendar.UpdateLayout();
            var days=Children<CalendarDayButton>(calendar).ToList();
            Check(days.Count==42 && days.All(b=>((SolidColorBrush)b.Foreground).Color.R<180),"Calendar dates are unreadable");
            var weekdays=Children<TextBlock>(calendar).Where(b=>new[]{"일","월","화","수","목","금","토","Su","Mo","Tu","We","Th","Fr","Sa"}.Contains(b.Text)).Count();
            Check(weekdays==7,"Weekday labels are missing");
            picker.IsDropDownOpen=false;
            Children<CheckBox>(tree).Single().IsChecked=false; tree.UpdateLayout();
            foreach(var date in Children<DatePicker>(tree)) CheckDate(date);
            foreach(var time in Children<TextBox>(tree).Where(b=>AutomationProperties.GetName(b).Contains("HH:mm"))) Check(time.ExtentWidth<=time.ViewportWidth+1,"Time text was clipped");
            var title=Children<TextBox>(tree).Single(b=>AutomationProperties.GetName(b)=="일정 제목");
            foreach(double fontSize in new[]{17.0,21.0,25.0}) { title.FontSize=fontSize; CheckTitle(tree); }
            title.FontSize=17; tree.UpdateLayout();
            var image=new RenderTargetBitmap((int)tree.ActualWidth,(int)tree.ActualHeight,96,96,PixelFormats.Pbgra32); image.Render(tree);
            var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tree-dialog-preview.png"))) png.Save(file);
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dialog-layout-result.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { passed=true,todoHeight=todo.ActualHeight,treeHeight=tree.ActualHeight,oneLineComposer=true,noCompletionCheckbox=true,dateAndTimeNotClipped=true,weekdayLabels=weekdays }));
            todo.Close(); tree.Close(); return;
        }
        app.Run();
    }
}
