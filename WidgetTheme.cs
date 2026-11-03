using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

static class WidgetTheme {
    internal static void Dialog(Window window,string title,FrameworkElement fields,FrameworkElement footer,double height=540,bool todoMate=false) {
        window.Title=title; window.Width=380; window.SizeToContent=SizeToContent.Height; window.MaxHeight=System.Math.Min(height,System.Math.Max(360,SystemParameters.WorkArea.Height-40));
        window.WindowStyle=WindowStyle.None; window.ResizeMode=ResizeMode.NoResize; window.AllowsTransparency=true;
        window.Background=Brushes.Transparent; window.Foreground=Theme.Brush("#25272B"); window.FontFamily=new FontFamily("Malgun Gothic"); window.FontSize=13;
        window.WindowStartupLocation=WindowStartupLocation.CenterOwner; window.ShowInTaskbar=false; Apply(window.Resources,true,todoMate);
        var dock=new DockPanel();
        window.Content=new Border { Background=Brushes.White,BorderBrush=Theme.Brush("#DDE2E5"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(18),Child=dock };
        var heading=new DockPanel { Margin=new Thickness(0,0,0,16) }; DockPanel.SetDock(heading,Dock.Top); dock.Children.Add(heading);
        var close=Theme.Button("×",window.Close,"닫기"); DockPanel.SetDock(close,Dock.Right); heading.Children.Add(close);
        if(todoMate) { var logo=TodoPanel.CreateLogo(); logo.Margin=new Thickness(0,0,10,0); DockPanel.SetDock(logo,Dock.Left); heading.Children.Add(logo); }
        var headingText=Theme.Text(todoMate ? title : "TimeTree · "+title,20,true); headingText.Effect=null; headingText.Foreground=Theme.Brush(todoMate ? "#25272B" : "#168B56"); heading.Children.Add(headingText);
        heading.MouseLeftButtonDown+=delegate { if(Mouse.LeftButton==MouseButtonState.Pressed) window.DragMove(); };
        footer.Margin=new Thickness(0,12,0,0); DockPanel.SetDock(footer,Dock.Bottom); dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer { Content=fields,MaxHeight=System.Math.Max(180,window.MaxHeight-142),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0) });
        LightContent(dock,todoMate);
        window.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Escape) { if(!CloseDropDown(dock)) window.Close(); e.Handled=true; } };
    }
    static bool CloseDropDown(DependencyObject element) {
        var date=element as DatePicker; if(date!=null && date.IsDropDownOpen) { date.IsDropDownOpen=false; return true; }
        var select=element as ComboBox; if(select!=null && select.IsDropDownOpen) { select.IsDropDownOpen=false; return true; }
        foreach(object child in LogicalTreeHelper.GetChildren(element)) { var dependency=child as DependencyObject; if(dependency!=null && CloseDropDown(dependency)) return true; }
        return false;
    }
    static void LightContent(DependencyObject element,bool todoMate) {
        var text=element as TextBlock;
        if(text!=null) { text.Effect=null; var color=text.Foreground as SolidColorBrush; if(color!=null && (color.Color==((SolidColorBrush)Theme.Ink).Color || color.Color==((SolidColorBrush)Theme.Muted).Color)) text.Foreground=Theme.Brush("#5B6268"); }
        var control=element as Control;
        if(control!=null) {
            var ink=control.Foreground as SolidColorBrush;
            if(ink!=null && (ink.Color==((SolidColorBrush)Theme.Ink).Color || ink.Color==((SolidColorBrush)Theme.Muted).Color)) control.Foreground=Theme.Brush("#25272B");
            var button=control as Button;
            if(button!=null) {
                var fill=button.Background as SolidColorBrush;
                bool primary=fill!=null && fill.Color==((SolidColorBrush)Theme.Green).Color;
                button.Background=primary ? todoMate ? Theme.Brush("#25272B") : Theme.Green : Theme.Brush("#F0F2F3");
                button.Foreground=primary && todoMate ? Brushes.White : Theme.Brush("#25272B");
            }
        }
        foreach(object child in LogicalTreeHelper.GetChildren(element)) { var dependency=child as DependencyObject; if(dependency!=null) LightContent(dependency,todoMate); }
    }
    internal static void Field(Panel panel,string label,Control control) {
        panel.Children.Add(new Label { Content=label,Target=control,Foreground=Theme.Muted,Padding=new Thickness(0),Margin=new Thickness(0,0,0,6) });
        control.Margin=new Thickness(0,0,0,14); control.MinHeight=34;
        System.Windows.Automation.AutomationProperties.SetName(control,label); panel.Children.Add(control);
    }
    internal static DatePicker Date(System.DateTime day) {
        var picker=new DatePicker { SelectedDate=day.Date,DisplayDate=day.Date,SelectedDateFormat=DatePickerFormat.Short,FirstDayOfWeek=System.DayOfWeek.Sunday };
        picker.DateValidationError+=delegate(object sender,DatePickerDateValidationErrorEventArgs e) { e.ThrowException=false; picker.SelectedDate=null; };
        return picker;
    }
    internal static void Apply(ResourceDictionary resources,bool light=false,bool todoMate=false) {
        string xaml=@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style x:Key='DateCell' TargetType='{x:Type Button}'>
  <Setter Property='Foreground' Value='#F4F8FC'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='1'/><Setter Property='MinHeight' Value='30'/><Setter Property='MinWidth' Value='32'/><Setter Property='Margin' Value='1'/><Setter Property='FontSize' Value='13'/><Setter Property='Padding' Value='4'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'>
   <Border CornerRadius='5' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#30FFFFFF'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='BorderBrush' Value='#2ECC87'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.35'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='DateDay' TargetType='{x:Type CalendarDayButton}' BasedOn='{StaticResource DateCell}'>
  <Style.Triggers><Trigger Property='IsInactive' Value='True'><Setter Property='Foreground' Value='#9CACB9'/></Trigger><Trigger Property='IsToday' Value='True'><Setter Property='BorderBrush' Value='#2ECC87'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter Property='Background' Value='#2ECC87'/><Setter Property='Foreground' Value='#17232E'/></Trigger><Trigger Property='IsBlackedOut' Value='True'><Setter Property='Opacity' Value='.3'/></Trigger></Style.Triggers>
 </Style>
 <Style x:Key='DateMonth' TargetType='{x:Type CalendarButton}' BasedOn='{StaticResource DateCell}'/>
 <Style x:Key='DateCalendarItem' TargetType='{x:Type CalendarItem}'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type CalendarItem}'>
   <ControlTemplate.Resources><DataTemplate x:Key='{x:Static CalendarItem.DayTitleTemplateResourceKey}'><TextBlock Text='{Binding}' Foreground='#D5E3E8' FontSize='12' HorizontalAlignment='Center' Margin='0,8'/></DataTemplate></ControlTemplate.Resources>
   <Grid x:Name='PART_Root'>
    <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions><Grid.ColumnDefinitions><ColumnDefinition Width='36'/><ColumnDefinition/><ColumnDefinition Width='36'/></Grid.ColumnDefinitions>
    <Button x:Name='PART_PreviousButton' Style='{StaticResource DateCell}' Content='‹'/><Button x:Name='PART_HeaderButton' Grid.Column='1' Style='{StaticResource DateCell}' FontWeight='SemiBold'/><Button x:Name='PART_NextButton' Grid.Column='2' Style='{StaticResource DateCell}' Content='›'/>
    <Grid x:Name='PART_MonthView' Grid.Row='1' Grid.ColumnSpan='3'>
     <Grid.RowDefinitions><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/></Grid.RowDefinitions>
     <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
    </Grid>
    <Grid x:Name='PART_YearView' Grid.Row='1' Grid.ColumnSpan='3' Visibility='Hidden'>
     <Grid.RowDefinitions><RowDefinition/><RowDefinition/><RowDefinition/></Grid.RowDefinitions><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
    </Grid>
    <Border x:Name='PART_DisabledVisual' Grid.RowSpan='2' Grid.ColumnSpan='3' Visibility='Collapsed'/>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='DateCalendar' TargetType='{x:Type Calendar}'>
  <Setter Property='CalendarItemStyle' Value='{StaticResource DateCalendarItem}'/><Setter Property='CalendarDayButtonStyle' Value='{StaticResource DateDay}'/><Setter Property='CalendarButtonStyle' Value='{StaticResource DateMonth}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Calendar}'>
   <StackPanel x:Name='PART_Root'><Border Background='#102A3E' BorderBrush='#6086A4B7' BorderThickness='1' CornerRadius='8' Padding='8'><CalendarItem x:Name='PART_CalendarItem' Style='{TemplateBinding CalendarItemStyle}'/></Border></StackPanel>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type DatePicker}'>
  <Setter Property='Background' Value='#14FFFFFF'/><Setter Property='Foreground' Value='#F4F8FC'/><Setter Property='BorderBrush' Value='#45C7E3F8'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='CalendarStyle' Value='{StaticResource DateCalendar}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type DatePicker}'>
   <Grid x:Name='PART_Root'>
    <Border x:Name='Frame' CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'>
     <Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='34'/></Grid.ColumnDefinitions>
      <DatePickerTextBox x:Name='PART_TextBox' Foreground='{TemplateBinding Foreground}' CaretBrush='#2ECC87' Background='Transparent' BorderThickness='0' Padding='9,6' VerticalContentAlignment='Center'>
       <DatePickerTextBox.Template><ControlTemplate TargetType='{x:Type DatePickerTextBox}'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/></ControlTemplate></DatePickerTextBox.Template>
      </DatePickerTextBox>
      <Button x:Name='PART_Button' Grid.Column='1' Focusable='False' Background='Transparent' Foreground='#D5E3E8' BorderThickness='0' Content='▦' FontSize='18'>
       <Button.Template><ControlTemplate TargetType='{x:Type Button}'><Border Background='Transparent' CornerRadius='6'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate></Button.Template>
      </Button>
     </Grid>
    </Border>
    <Popup x:Name='PART_Popup' Placement='Bottom' AllowsTransparency='True' StaysOpen='False'/>
   </Grid>
   <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#2ECC87'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type TextBox}'>
  <Setter Property='Background' Value='#14FFFFFF'/><Setter Property='Foreground' Value='#F4F8FC'/><Setter Property='CaretBrush' Value='#2ECC87'/><Setter Property='BorderBrush' Value='#45C7E3F8'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='9,6'/><Setter Property='SelectionBrush' Value='#502ECC87'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type TextBox}'>
   <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6' Padding='{TemplateBinding Padding}'><ScrollViewer x:Name='PART_ContentHost'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#2ECC87'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type PasswordBox}'>
  <Setter Property='Background' Value='#14FFFFFF'/><Setter Property='Foreground' Value='#F4F8FC'/><Setter Property='CaretBrush' Value='#2ECC87'/><Setter Property='BorderBrush' Value='#45C7E3F8'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='9,6'/><Setter Property='SelectionBrush' Value='#502ECC87'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type PasswordBox}'>
   <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6' Padding='{TemplateBinding Padding}'><ScrollViewer x:Name='PART_ContentHost'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#2ECC87'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type CheckBox}'>
  <Setter Property='Foreground' Value='#F4F8FC'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Padding' Value='3'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type CheckBox}'>
   <Border Background='Transparent' Padding='{TemplateBinding Padding}'>
    <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='22'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
     <Border x:Name='Box' Width='20' Height='20' CornerRadius='6' Background='#14FFFFFF' BorderBrush='#80E0EDF5' BorderThickness='1.3' VerticalAlignment='Center'>
      <Path x:Name='Mark' Data='M 4,10 L 8,14 L 16,5' Stroke='#102A3E' StrokeThickness='2.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Visibility='Collapsed'/>
     </Border>
     <ContentPresenter Grid.Column='1' Margin='8,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/>
    </Grid>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='HasContent' Value='False'><Setter Property='Width' Value='28'/></Trigger>
    <Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='#2ECC87'/><Setter TargetName='Box' Property='BorderBrush' Value='#2ECC87'/><Setter TargetName='Mark' Property='Visibility' Value='Visible'/></Trigger>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#7BE3B6'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#FFFFFF'/><Setter TargetName='Box' Property='BorderThickness' Value='2'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='ScrollThumb' TargetType='{x:Type Thumb}'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Thumb}'>
   <Border x:Name='Grip' Background='#70D5E3E8' CornerRadius='3' Margin='3'/>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Grip' Property='Background' Value='#C0F4F8FC'/></Trigger>
    <Trigger Property='IsDragging' Value='True'><Setter TargetName='Grip' Property='Background' Value='#2ECC87'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='ScrollPage' TargetType='{x:Type RepeatButton}'>
  <Setter Property='Focusable' Value='False'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type ScrollBar}'>
  <Setter Property='Width' Value='12'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
   <Grid Background='{TemplateBinding Background}'>
    <Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='{TemplateBinding Orientation}' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' ViewportSize='{TemplateBinding ViewportSize}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
     <Track.DecreaseRepeatButton><RepeatButton x:Name='Before' Style='{StaticResource ScrollPage}' Command='{x:Static ScrollBar.PageUpCommand}'/></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb x:Name='Grip' Style='{StaticResource ScrollThumb}' MinHeight='28'/></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton x:Name='After' Style='{StaticResource ScrollPage}' Command='{x:Static ScrollBar.PageDownCommand}'/></Track.IncreaseRepeatButton>
    </Track>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property='Orientation' Value='Horizontal'>
     <Setter Property='Width' Value='Auto'/><Setter Property='Height' Value='12'/><Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/>
     <Setter TargetName='Grip' Property='MinHeight' Value='0'/><Setter TargetName='Grip' Property='MinWidth' Value='28'/>
     <Setter TargetName='Before' Property='Command' Value='{x:Static ScrollBar.PageLeftCommand}'/><Setter TargetName='After' Property='Command' Value='{x:Static ScrollBar.PageRightCommand}'/>
    </Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.25'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type Slider}'>
  <Setter Property='Height' Value='30'/><Setter Property='Focusable' Value='True'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Slider}'>
   <Grid Background='Transparent' Margin='0,3'>
    <Border Height='4' CornerRadius='2' Background='#30FFFFFF' VerticalAlignment='Center'/>
    <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' IsDirectionReversed='{TemplateBinding IsDirectionReversed}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
     <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource ScrollPage}' Command='{x:Static Slider.DecreaseLarge}'/></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb Width='18' Height='18'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border Background='#2ECC87' BorderBrush='#B0D8FFF0' BorderThickness='1' CornerRadius='9'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource ScrollPage}' Command='{x:Static Slider.IncreaseLarge}'/></Track.IncreaseRepeatButton>
    </Track>
   </Grid>
   <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter Property='Opacity' Value='.8'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>";
        if(light) {
            xaml=xaml.Replace("Stroke='#102A3E'",todoMate ? "Stroke='#FFFFFF'" : "Stroke='#17232E'");
            xaml=xaml.Replace("#102A3E","#FFFFFF").Replace("#F4F8FC","#25272B").Replace("#D5E3E8","#5B6268").Replace("#9CACB9","#747B80")
                .Replace("#14FFFFFF","#F7F8F9").Replace("#30FFFFFF","#EDF1F2").Replace("#45C7E3F8","#DDE2E5").Replace("#6086A4B7","#DDE2E5").Replace("#80E0EDF5","#A7AFB4")
                .Replace("#70D5E3E8","#9CA5AA").Replace("#C0F4F8FC","#65727A").Replace("Value='#FFFFFF'","Value='#25272B'");
            if(todoMate) xaml=xaml.Replace("#2ECC87","#25272B").Replace("#17232E","#FFFFFF");
        }
        resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
    }
}
