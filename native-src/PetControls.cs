using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DshNotifyPet {
  sealed partial class PetWindow {
    const string PreferencesFile = @"D:\DSH-data\notify-lite\pet-settings.json";
    readonly PetPreferences preferences = PetPreferences.Load(PreferencesFile);
    readonly ReminderSound reminderSound = new ReminderSound();
    PetSettingsWindow preferencesWindow;
    bool stopping;

    void ShowPreferences() {
      if (preferencesWindow != null) { preferencesWindow.Activate(); return; }
      hideTimer.Stop();
      preferencesWindow = new PetSettingsWindow(preferences,
        delegate { if (preferences.Muted || preferences.Volume == 0) reminderSound.Stop(); ApplyAppearance(); SavePosition(); return preferences.Save(PreferencesFile); },
        delegate { return preferences.Muted || preferences.Volume == 0 || reminderSound.TryPlay(preferences.Volume, false); });
      preferencesWindow.Loaded += delegate {
        var area = SystemParameters.WorkArea;
        preferencesWindow.Left = Math.Max(area.Left + 8, Math.Min(Left + Width - preferencesWindow.ActualWidth, area.Right - preferencesWindow.ActualWidth - 8));
        preferencesWindow.Top = Math.Max(area.Top + 8, Math.Min(Top - 20, area.Bottom - preferencesWindow.ActualHeight - 8));
      };
      preferencesWindow.Closed += delegate {
        preferencesWindow = null;
        if (!stopping && active != null) { hideTimer.Interval = TimeSpan.FromSeconds(5); hideTimer.Start(); }
      };
      preferencesWindow.Show();
    }
    void ApplyAppearance() {
      double right = Left + Width, bottom = Top + Height;
      double scale = preferences.Scale;
      Width = 442 + (scale - 1) * 155; Height = Math.Max(220, 244 + (scale - 1) * 199);
      avatar.Width = 155 * scale; avatar.Height = 199 * scale;
      Canvas.SetLeft(badge, 398 + (scale - 1) * 155);
      if (positioned && !Double.IsNaN(right) && !Double.IsNaN(bottom)) { Left = right - Width; Top = bottom - Height; ClampPosition(); }
      UpdateAnimations();
    }
    void UpdateAnimations() {
      if (preferences.Animations && IsVisible) {
        floatMotion.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0,-5,TimeSpan.FromSeconds(1.25)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-1.7,1.7,TimeSpan.FromSeconds(1.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
      } else {
        floatMotion.BeginAnimation(TranslateTransform.YProperty, null); tilt.BeginAnimation(RotateTransform.AngleProperty, null);
      }
    }
  }

  sealed class PetSettingsWindow : Window {
    readonly PetPreferences settings;
    readonly Func<bool> changed, testSound;
    readonly Slider volume, scale;
    readonly CheckBox muted, animations;
    readonly ComboBox completed, waiting;
    readonly TextBlock volumeValue, scaleValue, status;
    readonly Button test;
    bool updating;

    public PetSettingsWindow(PetPreferences values, Func<bool> save, Func<bool> play) {
      settings = values; changed = save; testSound = play;
      Title = "小助手设置"; Width = 354; SizeToContent = SizeToContent.Height;
      WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
      AllowsTransparency = true; Background = Brushes.Transparent;
      ShowInTaskbar = false; Topmost = true;
      FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 12;
      var frame = new Border { Background = Color("#FFFFFF"), BorderBrush = Color("#A3AED0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Padding = new Thickness(20,16,20,18), Margin = new Thickness(9), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 13, Opacity = .15, ShadowDepth = 2, Color = Colors.MidnightBlue } };
      Content = frame;
      var stack = new StackPanel(); frame.Child = stack;
      var header = new DockPanel { Margin = new Thickness(0,0,0,18) };
      var close = Button("×", false); close.FontSize = 17; close.Padding = new Thickness(6,0,6,0);
      AutomationProperties.SetName(close,"关闭设置"); close.Click += delegate { Close(); };
      DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
      var title = new TextBlock { Text = "小助手设置", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Color("#354578"), VerticalAlignment = VerticalAlignment.Center };
      header.Children.Add(title); header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.OriginalSource == title) { try { DragMove(); } catch (InvalidOperationException) { } } };
      stack.Children.Add(header);

      volumeValue = new TextBlock(); stack.Children.Add(LabelRow("提醒音量", volumeValue));
      volume = Slider(0,100,5); AutomationProperties.SetName(volume,"提醒音量"); volume.Value = settings.Volume; stack.Children.Add(volume);
      var audio = new DockPanel { Margin = new Thickness(0,3,0,16) };
      test = Button("试音",true); AutomationProperties.SetName(test,"试音"); DockPanel.SetDock(test,Dock.Right); audio.Children.Add(test);
      muted = new CheckBox { Content = "静音", IsChecked = settings.Muted, VerticalAlignment = VerticalAlignment.Center, Foreground = Color("#566183") };
      AutomationProperties.SetName(muted,"静音"); audio.Children.Add(muted); stack.Children.Add(audio);
      stack.Children.Add(new Border { Height = 1, Background = Color("#E7EAF4"), Margin = new Thickness(0,0,0,15) });

      scaleValue = new TextBlock(); stack.Children.Add(LabelRow("小人大小",scaleValue));
      scale = Slider(75,140,5); scale.Value = settings.Scale * 100; AutomationProperties.SetName(scale,"小人大小"); stack.Children.Add(scale);
      animations = new CheckBox { Content = "小人浮动动画", IsChecked = settings.Animations, Margin = new Thickness(0,4,0,17), Foreground = Color("#566183") };
      AutomationProperties.SetName(animations,"小人浮动动画"); stack.Children.Add(animations);
      completed = Durations(new[]{5,8,12,20,30,60},settings.CompletedSeconds); AutomationProperties.SetName(completed,"完成提醒时长"); stack.Children.Add(ChoiceRow("完成提醒停留",completed));
      waiting = Durations(new[]{5,10,15,20,30,60},settings.WaitingSeconds); AutomationProperties.SetName(waiting,"等待提醒时长"); stack.Children.Add(ChoiceRow("等待处理停留",waiting));
      stack.Children.Add(new TextBlock { Text = "未处理的授权或问题会继续周期提醒。", FontSize = 10.5, Foreground = Color("#8A92AB"), Margin = new Thickness(0,4,0,14) });
      status = new TextBlock { Text = "调整后自动保存", FontSize = 10.5, Foreground = Color("#8A92AB"), Margin = new Thickness(0,0,0,12) };
      stack.Children.Add(status);
      var bottom = new DockPanel();
      var done = Button("完成",true); AutomationProperties.SetName(done,"完成设置"); done.Click += delegate { Close(); }; DockPanel.SetDock(done,Dock.Right); bottom.Children.Add(done);
      var reset = Button("恢复默认",false); AutomationProperties.SetName(reset,"恢复默认"); reset.HorizontalAlignment = HorizontalAlignment.Left; reset.Click += delegate { Reset(); }; bottom.Children.Add(reset); stack.Children.Add(bottom);
      stack.Children.Add(new TextBlock { Text = "音量只影响这个小助手", FontSize = 10.5, Foreground = Color("#969DB2"), Margin = new Thickness(0,13,0,0), HorizontalAlignment = HorizontalAlignment.Center });

      volume.ValueChanged += delegate { if (updating) return; settings.Volume = (int)Math.Round(volume.Value); RefreshReadouts(); Save(); };
      scale.ValueChanged += delegate { if (updating) return; settings.Scale = scale.Value / 100; RefreshReadouts(); Save(); };
      muted.Checked += MutedChanged; muted.Unchecked += MutedChanged;
      animations.Checked += AnimationChanged; animations.Unchecked += AnimationChanged;
      completed.SelectionChanged += delegate { if (!updating && completed.SelectedItem != null) { settings.CompletedSeconds=(int)((ComboBoxItem)completed.SelectedItem).Tag; Save(); } };
      waiting.SelectionChanged += delegate { if (!updating && waiting.SelectedItem != null) { settings.WaitingSeconds=(int)((ComboBoxItem)waiting.SelectedItem).Tag; Save(); } };
      test.Click += delegate { if(!testSound()) { status.Text="播放失败，请检查声音输出设备";status.Foreground=Color("#A1574F"); } else { status.Text="已试音";status.Foreground=Color("#657797"); } };
      RefreshReadouts();
    }
    void MutedChanged(object sender,RoutedEventArgs e) { if (updating) return; settings.Muted=muted.IsChecked==true;RefreshReadouts();Save(); }
    void AnimationChanged(object sender,RoutedEventArgs e) { if (updating) return; settings.Animations=animations.IsChecked==true;Save(); }
    void Save() { settings.Sanitize();bool ok=changed();status.Text=ok?"已保存":"保存失败，请检查磁盘是否可写";status.Foreground=Color(ok?"#657797":"#A1574F"); }
    void RefreshReadouts() { volumeValue.Text=settings.Volume+"%";scaleValue.Text=Math.Round(settings.Scale*100)+"%";test.IsEnabled=!settings.Muted&&settings.Volume>0; }
    void Reset() {
      updating=true;settings.Reset();volume.Value=settings.Volume;scale.Value=settings.Scale*100;muted.IsChecked=settings.Muted;animations.IsChecked=settings.Animations;
      Select(completed,settings.CompletedSeconds);Select(waiting,settings.WaitingSeconds);updating=false;RefreshReadouts();Save();
    }
    static SolidColorBrush Color(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }
    static Button Button(string label,bool primary) { return new Button { Content=label,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=12,Padding=new Thickness(13,5,13,5),Background=Color(primary?"#586BA4":"#F0F2F8"),Foreground=primary?Brushes.White:Color("#627097"),BorderThickness=new Thickness(0),Cursor=Cursors.Hand }; }
    static Slider Slider(double min,double max,double tick) { return new Slider { Minimum=min,Maximum=max,TickFrequency=tick,IsSnapToTickEnabled=true,Margin=new Thickness(0,5,0,7),Height=22,Foreground=Color("#586BA4") }; }
    static DockPanel LabelRow(string label,TextBlock value) {
      var row=new DockPanel();value.Foreground=Color("#627097");value.FontSize=11;value.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(value,Dock.Right);row.Children.Add(value);row.Children.Add(new TextBlock{Text=label,Foreground=Color("#46557E")});return row;
    }
    static DockPanel ChoiceRow(string text,ComboBox combo) { var row=new DockPanel{Margin=new Thickness(0,0,0,9)};DockPanel.SetDock(combo,Dock.Right);row.Children.Add(combo);row.Children.Add(new TextBlock{Text=text,Foreground=Color("#566183"),VerticalAlignment=VerticalAlignment.Center});return row; }
    static ComboBox Durations(int[] values,int selected) { var c=new ComboBox{Width=88,Padding=new Thickness(6,2,6,2)};foreach(int n in values)c.Items.Add(new ComboBoxItem{Content=n+" 秒",Tag=n});if(Array.IndexOf(values,selected)<0)c.Items.Add(new ComboBoxItem{Content=selected+" 秒",Tag=selected});Select(c,selected);return c; }
    static void Select(ComboBox combo,int selected) { foreach(ComboBoxItem item in combo.Items)if((int)item.Tag==selected){combo.SelectedItem=item;break;} }
  }
}
