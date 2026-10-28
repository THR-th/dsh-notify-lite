using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Reflection;

[assembly: AssemblyTitle("DSH 小助手")]
[assembly: AssemblyDescription("DSH task notifications. Local UI only, no shell commands or network.")]
[assembly: AssemblyVersion("1.2.0.0")]

namespace DshNotifyPet {
  sealed class Notice {
    public string Key, Kind;
    public bool Sound;
    public int Seconds;
    public int Priority { get { return Kind == "approval" || Kind == "question" ? 2 : 1; } }
  }
  static class Program {
    internal static Application App;
    internal static PetWindow Pet;
    [STAThread] static void Main() {
      try {
        App = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Pet = new PetWindow();
        var reader = new Thread(ReadInput) { IsBackground = true, Name = "DSH notification input" };
        reader.Start();
        App.Run();
      } catch { Environment.ExitCode = 1; }
    }
    static void ReadInput() {
      var json = new JavaScriptSerializer { MaxJsonLength = 8192, RecursionLimit = 8 };
      try {
        using (var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true))) {
          for (;;) {
            string line = ReadBoundedLine(input);
            if (line == null) break;
            if (line.Length == 0) continue;
            try {
              var data = json.Deserialize<Dictionary<string, object>>(line);
              if (data == null) continue;
              string op = Value(data, "op", 16);
              if (op == "quit") break;
              string key = Value(data, "key", 1024);
              if (key.Length == 0) continue;
              if (op == "cancel") {
                App.Dispatcher.BeginInvoke(new Action(delegate { Pet.Cancel(key); }));
              } else if (op == "show") {
                string kind = Value(data, "kind", 24);
                if (!new[] {"completed","approval","question","error","aborted","max-tokens","test","unknown"}.Contains(kind)) kind = "unknown";
                object s; bool sound = data.TryGetValue("sound", out s) && s is bool && (bool)s;
                int seconds = kind == "approval" || kind == "question" ? 20 : 12;
                var item = new Notice { Key = key, Kind = kind, Sound = sound, Seconds = seconds };
                App.Dispatcher.BeginInvoke(new Action(delegate { Pet.Enqueue(item); }));
              }
            } catch { /* Invalid input never becomes executable text or UI markup. */ }
          }
        }
      } catch { }
      try { App.Dispatcher.BeginInvoke(new Action(delegate { Pet.Stop(); App.Shutdown(); })); } catch { }
    }
    static string Value(Dictionary<string, object> d, string k, int max) {
      object v; if (!d.TryGetValue(k, out v) || !(v is string)) return "";
      string s = (string)v; return s.Length <= max ? s : "";
    }
    static string ReadBoundedLine(TextReader input) {
      var text = new StringBuilder(); bool overflow = false;
      for (;;) {
        int c = input.Read();
        if (c < 0) return text.Length == 0 || overflow ? null : text.ToString();
        if (c == '\n') return overflow ? "" : text.ToString().TrimEnd('\r');
        if (text.Length < 8192) text.Append((char)c); else overflow = true;
      }
    }
  }
  sealed partial class PetWindow : Window {
    const string DshExe = @"D:\DSH\DeepSeek Harness.exe";
    const string PositionFile = @"D:\DSH-data\notify-lite\pet-position.json";
    readonly List<Notice> queue = new List<Notice>();
    readonly DispatcherTimer hideTimer;
    readonly TextBlock headline, body, mood;
    readonly Border bubble, badge;
    readonly Image avatar;
    readonly TranslateTransform floatMotion = new TranslateTransform();
    readonly RotateTransform tilt = new RotateTransform();
    Notice active;
    bool positioned, dragged;
    Point mouseStart;
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

    public PetWindow() {
      Title = "DSH 小助手";
      Width = 442; Height = 244;
      WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
      // Microsoft WPF: non-rectangular modeless windows, without first-show activation.
      // https://learn.microsoft.com/dotnet/api/system.windows.window.allowstransparency
      // https://learn.microsoft.com/dotnet/api/system.windows.window.showactivated
      AllowsTransparency = true; Background = Brushes.Transparent;
      ShowActivated = false; ShowInTaskbar = false; Topmost = true;
      WindowStartupLocation = WindowStartupLocation.Manual;
      var canvas = new Canvas(); Content = canvas;

      bubble = new Border {
        Width = 260, Background = Brush("#FFFFFF"), BorderBrush = Brush("#53639C"),
        BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(19), Padding = new Thickness(17,12,14,13),
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 13, ShadowDepth = 2, Opacity = .14, Color = Colors.MidnightBlue }
      };
      Canvas.SetLeft(bubble, 10); Canvas.SetTop(bubble, 14); canvas.Children.Add(bubble);
      var stack = new StackPanel(); bubble.Child = stack;
      var top = new DockPanel { Margin = new Thickness(0,0,0,5) };
      var close = new Button { Content = "×", Width = 22, Height = 22, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brush("#7781A4"), FontSize = 17, Cursor = Cursors.Hand, Padding = new Thickness(0) };
      AutomationProperties.SetName(close, "关闭本次提醒");
      close.Click += delegate { Finish(); };
      DockPanel.SetDock(close, Dock.Right); top.Children.Add(close);
      var gear = new Button { Content = "⚙", Width = 24, Height = 22, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brush("#7781A4"), FontSize = 16, Cursor = Cursors.Hand, Padding = new Thickness(0) };
      AutomationProperties.SetName(gear, "小助手设置"); gear.ToolTip = "小助手设置"; gear.Click += delegate { ShowPreferences(); };
      DockPanel.SetDock(gear, Dock.Right); top.Children.Add(gear);
      top.Children.Add(new TextBlock { Text = "DSH 小助手", Foreground = Brush("#7D85A2"), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center });
      stack.Children.Add(top);
      headline = new TextBlock { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brush("#354578"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,7) };
      stack.Children.Add(headline);
      body = new TextBlock { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 12, Foreground = Brush("#67718A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12), LineHeight = 19 };
      stack.Children.Add(body);
      var buttons = new StackPanel { Orientation = Orientation.Horizontal };
      var open = MakeButton("去 DSH 看看", true); AutomationProperties.SetName(open, "打开 DSH");
      open.Click += delegate { OpenDsh(); Finish(); };
      var dismiss = MakeButton("收起", false); AutomationProperties.SetName(dismiss, "收起提醒");
      dismiss.Click += delegate { Finish(); };
      buttons.Children.Add(open); buttons.Children.Add(dismiss); stack.Children.Add(buttons);

      var tail = new Polygon { Points = new PointCollection(new[] {new Point(249,119),new Point(281,155),new Point(266,111)}), Fill = Brushes.White, Stroke = Brush("#53639C"), StrokeThickness = 2, IsHitTestVisible = false };
      Panel.SetZIndex(tail, -1); canvas.Children.Add(tail);
      avatar = new Image { Width = 155, Height = 199, Stretch = Stretch.Uniform, Cursor = Cursors.Hand, ToolTip = "点击打开设置，拖动调整位置" };
      var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
      image.UriSource = new Uri(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","assets","maid.png")));
      image.EndInit(); image.Freeze(); avatar.Source = image;
      AutomationProperties.SetName(avatar, "蓝发小助手");
      var transforms = new TransformGroup(); transforms.Children.Add(tilt); transforms.Children.Add(floatMotion);
      avatar.RenderTransform = transforms; avatar.RenderTransformOrigin = new Point(.5,.85);
      Canvas.SetLeft(avatar, 275); Canvas.SetTop(avatar, 43); canvas.Children.Add(avatar);
      avatar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { mouseStart = e.GetPosition(this); dragged = false; avatar.CaptureMouse(); e.Handled = true; };
      avatar.MouseMove += delegate(object sender, MouseEventArgs e) {
        if (!avatar.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed || dragged) return;
        if ((e.GetPosition(this) - mouseStart).Length < 5) return;
        dragged = true; avatar.ReleaseMouseCapture();
        // https://learn.microsoft.com/dotnet/api/system.windows.window.dragmove
        try { DragMove(); ClampPosition(); SavePosition(); } catch (InvalidOperationException) { }
      };
      avatar.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { avatar.ReleaseMouseCapture(); if (!dragged) { ShowPreferences(); } e.Handled = true; };

      mood = new TextBlock { Text = "✓", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
      badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Brush("#7995CA"), Child = mood, IsHitTestVisible = false };
      Canvas.SetLeft(badge, 398); Canvas.SetTop(badge, 53); canvas.Children.Add(badge);
      hideTimer = new DispatcherTimer(); hideTimer.Tick += delegate { Finish(); };
      avatar.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e) { ShowPreferences(); e.Handled = true; };
      Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!stopping) { e.Cancel = true; Finish(); } };
      MouseEnter += delegate { if (active != null) hideTimer.Stop(); };
      MouseLeave += delegate { if (active != null && preferencesWindow == null) { hideTimer.Interval = TimeSpan.FromSeconds(5); hideTimer.Start(); } };
    }
    static SolidColorBrush Brush(string value) { return (SolidColorBrush)new BrushConverter().ConvertFromString(value); }
    static Button MakeButton(string label, bool primary) {
      return new Button { Content = label, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 11, Padding = new Thickness(12,5,12,5), Margin = new Thickness(0,0,8,0), Background = primary ? Brush("#586BA4") : Brush("#F2F4FA"), Foreground = primary ? Brushes.White : Brush("#687393"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
    }
    public void Enqueue(Notice item) {
      if ((active != null && active.Key == item.Key) || queue.Any(x => x.Key == item.Key)) return;
      if (active != null && item.Priority > active.Priority) {
        hideTimer.Stop(); active = null; Hide();
      }
      if (queue.Count >= 10) { if (item.Priority < queue[queue.Count - 1].Priority) return; queue.RemoveAt(queue.Count - 1); }
      queue.Add(item); queue.Sort(delegate(Notice a, Notice b) { return b.Priority.CompareTo(a.Priority); });
      Next();
    }
    public void Cancel(string key) {
      queue.RemoveAll(x => x.Key == key);
      if (active != null && active.Key == key) Finish();
    }
    void Next() {
      if (active != null || queue.Count == 0) return;
      active = queue[0]; queue.RemoveAt(0);
      string title, text;
      switch (active.Kind) {
        case "completed": title = "做好啦！"; text = "这次任务完成了，\n回来看看结果吧。"; break;
        case "approval": title = "需要你点个头"; text = "DSH 正在等待授权，\n处理后我会停止提醒。"; break;
        case "question": title = "等你回答一下～"; text = "DSH 有个问题需要你决定，\n回来看看吧。"; break;
        case "error": title = "遇到一点问题"; text = "任务因错误停止了，\n回 DSH 查看具体原因吧。"; break;
        case "aborted": title = "任务已停下"; text = "这次任务已中止，\n当前进度在 DSH 里。"; break;
        case "max-tokens": title = "本轮到上限啦"; text = "任务已停下，\n回 DSH 决定是否继续。"; break;
        case "test": title = "你好，我是小助手！"; text = "完成任务或需要你时，\n我会这样出来提醒你。"; break;
        default: title = "DSH 有新进展"; text = "本轮任务已结束，\n回来查看当前状态吧。"; break;
      }
      headline.Text = title; body.Text = text;
      bool attention = active.Priority == 2;
      mood.Text = attention ? "!" : active.Kind == "error" ? "…" : "✓";
      badge.Background = attention ? Brush("#D8AC68") : Brush("#7995CA");
      ApplyAppearance(); if (!positioned) { LoadPosition(); positioned = true; }
      ClampPosition(); ShowActivated = false; Show();
      UpdateAnimations();
      hideTimer.Interval = TimeSpan.FromSeconds(attention ? preferences.WaitingSeconds : preferences.CompletedSeconds); if (preferencesWindow == null) hideTimer.Start();
      if (active.Sound && !preferences.Muted) reminderSound.TryPlay(preferences.Volume, attention);
    }
    void Finish() {
      hideTimer.Stop(); reminderSound.Stop(); active = null;
      floatMotion.BeginAnimation(TranslateTransform.YProperty, null); tilt.BeginAnimation(RotateTransform.AngleProperty, null);
      Hide(); Next();
    }
    public void Stop() { stopping = true; if (preferencesWindow != null) preferencesWindow.Close(); reminderSound.Dispose(); hideTimer.Stop(); queue.Clear(); active = null; Close(); }
    void LoadPosition() {
      var area = SystemParameters.WorkArea; Left = area.Right - Width - 15; Top = area.Bottom - Height - 12;
      try {
        if (!File.Exists(PositionFile) || new FileInfo(PositionFile).Length > 1024) return;
        var p = new JavaScriptSerializer().Deserialize<Dictionary<string,double>>(File.ReadAllText(PositionFile));
        if (p != null && p.ContainsKey("left") && p.ContainsKey("top") && !Double.IsNaN(p["left"]) && !Double.IsInfinity(p["left"]) && !Double.IsNaN(p["top"]) && !Double.IsInfinity(p["top"])) { Left=p["left"];Top=p["top"]; }
      } catch { }
    }
    void ClampPosition() {
      var area = SystemParameters.WorkArea;
      Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
      Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
    }
    void SavePosition() {
      try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PositionFile)); File.WriteAllText(PositionFile,new JavaScriptSerializer().Serialize(new Dictionary<string,double>{{"left",Left},{"top",Top}}),new UTF8Encoding(false)); } catch { }
    }
    void OpenDsh() {
      // Only the fixed, installed desktop application can be opened, after a click.
      foreach (var p in Process.GetProcessesByName("DeepSeek Harness")) {
        try { if (String.Equals(p.MainModule.FileName,DshExe,StringComparison.OrdinalIgnoreCase) && p.MainWindowHandle != IntPtr.Zero) { ShowWindow(p.MainWindowHandle,9);SetForegroundWindow(p.MainWindowHandle);return; } } catch { }
      }
      if (File.Exists(DshExe)) { try { Process.Start(new ProcessStartInfo(DshExe) { UseShellExecute=false, WorkingDirectory=System.IO.Path.GetDirectoryName(DshExe) }); } catch { }
      }
    }
  }
}

