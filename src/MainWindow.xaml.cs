using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace XRPlayer;

public class TrackItem
{
    public string Id { get; set; }
    public string Text { get; set; }
    public string Lang { get; set; }
    public override string ToString() => Text;
}

public partial class MainWindow : Window
{
    public Mpv Player;
    public AppSettings Cfg = AppSettings.Load();
    public SubtitleRenderer Subs;
    readonly ObservableCollection<string> playlist = new();
    int index = -1;
    bool dragging, loadingTracks, fullscreen, ready;
    double duration;
    string currentPath;
    WindowState prevState; WindowStyle prevStyle; ResizeMode prevResize;
    readonly DispatcherTimer hideTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    SettingsWindow settingsWin;

    static readonly string[] MediaExt = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".3gp", ".ogv", ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".opus", ".wma" };
    static readonly string[] SubExt = { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".sup" };

    public MainWindow()
    {
        InitializeComponent();
        List.ItemsSource = playlist;
        foreach (var s in new[] { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 3.0, 4.0 }) SpeedBox.Items.Add(s.ToString("0.##") + "x");
        SpeedBox.SelectedIndex = 3;
        Vol.Value = Cfg.Volume;
        Topmost = Cfg.AlwaysOnTop;
        SelectLayoutItem();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Drop += OnDrop;
        PreviewKeyDown += OnKey;
        hideTimer.Tick += (_, _) => { hideTimer.Stop(); if (fullscreen) SetBarVisible(false); };
    }

    void OnLoaded(object s, RoutedEventArgs e)
    {
        try { Player = new Mpv(Host.Handle); }
        catch (Exception ex) { MessageBox.Show("Could not start the playback engine (libmpv-2.dll):\n" + ex.Message, "WinPlayer"); Close(); return; }

        Player.Option("hwdec", Cfg.Hwdec);
        Player.Set("hwdec", Cfg.Hwdec);
        Player.Set("volume", Cfg.Volume);
        Player.Set("deinterlace", Cfg.Deinterlace);
        Player.Set("scale", Cfg.Scale);
        Player.Set("loop-file", Cfg.Loop ? "inf" : "no");
        Player.Set("sub-visibility", Cfg.NativeSubRendering);
        Player.Set("secondary-sub-visibility", Cfg.NativeSubRendering);
        if (!string.IsNullOrEmpty(Cfg.AudioLang)) Player.Set("alang", Cfg.AudioLang);
        Subs = new SubtitleRenderer(Player, Cfg);

        foreach (var p in new[] { "time-pos", "duration", "pause", "idle-active", "sub-text", "secondary-sub-text", "media-title", "eof-reached", "volume", "mute" }) Player.Observe(p);
        Player.PropertyChanged += (n, v) => Dispatcher.BeginInvoke(() => OnProp(n, v));
        Player.FileLoaded += () => Dispatcher.BeginInvoke(OnFileLoaded);
        Player.Message += a => Dispatcher.BeginInvoke(() => OnMpvMessage(a));

        // Keys and mouse handled inside the video surface are forwarded here through mpv key bindings.
        foreach (var (k, a) in MpvKeys) Player.Command("keybind", k, "script-message xr " + a);
        Player.Command("keybind", "MBTN_LEFT", "script-message xr pause");
        Player.Command("keybind", "MBTN_LEFT_DBL", "script-message xr fullscreen");
        Player.Command("keybind", "WHEEL_UP", "script-message xr volup");
        Player.Command("keybind", "WHEEL_DOWN", "script-message xr voldown");
        Player.Command("keybind", "MOUSE_MOVE", "script-message xr mouse");
        ready = true;

        var args = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToList();
        if (args.Count > 0) { playlist.Clear(); foreach (var a in args) playlist.Add(a); PlayIndex(0); }
    }

    // ---------- keys ----------
    static readonly (string, string)[] MpvKeys =
    {
        ("SPACE","pause"),("LEFT","left"),("RIGHT","right"),("Shift+LEFT","bigleft"),("Shift+RIGHT","bigright"),
        ("UP","volup"),("DOWN","voldown"),("f","fullscreen"),("ESC","esc"),("m","mute"),("v","subs"),("l","list"),
        ("s","shot"),("n","next"),("p","prev"),("]","faster"),("[","slower"),("+","faster"),("=","faster"),("-","slower"),("KP_ADD","faster"),("KP_SUBTRACT","slower"),("BS","speed1"),(".","framestep"),(",","frameback"),
        ("z","d1minus"),("x","d1plus"),("c","d2minus"),("b","d2plus"),("Ctrl+o","open"),("Ctrl+,","settings"),
    };
    static readonly Dictionary<Key, string> WpfKeys = new()
    {
        [Key.Space]="pause",[Key.Left]="left",[Key.Right]="right",[Key.Up]="volup",[Key.Down]="voldown",[Key.F]="fullscreen",[Key.Escape]="esc",
        [Key.M]="mute",[Key.V]="subs",[Key.L]="list",[Key.S]="shot",[Key.N]="next",[Key.P]="prev",[Key.OemCloseBrackets]="faster",
        [Key.OemOpenBrackets]="slower",[Key.OemPlus]="faster",[Key.Add]="faster",[Key.OemMinus]="slower",[Key.Subtract]="slower",[Key.Back]="speed1",[Key.OemPeriod]="framestep",[Key.OemComma]="frameback",
        [Key.Z]="d1minus",[Key.X]="d1plus",[Key.C]="d2minus",[Key.B]="d2plus",
    };

    void OnKey(object s, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        string a = null;
        if (ctrl && e.Key == Key.O) a = "open";
        else if (ctrl && e.Key == Key.OemComma) a = "settings";
        else if (shift && e.Key == Key.Left) a = "bigleft";
        else if (shift && e.Key == Key.Right) a = "bigright";
        else if (!ctrl && WpfKeys.TryGetValue(e.Key, out var x)) a = x;
        if (a != null) { Do(a); e.Handled = true; }
    }

    void OnMpvMessage(string[] a)
    {
        if (a.Length >= 2 && a[0] == "xr") Do(a[1]);
    }

    void Do(string a)
    {
        switch (a)
        {
            case "pause": TogglePause(); break;
            case "left": SeekBy(-Cfg.SeekSmall); break;
            case "right": SeekBy(Cfg.SeekSmall); break;
            case "bigleft": SeekBy(-Cfg.SeekBig); break;
            case "bigright": SeekBy(Cfg.SeekBig); break;
            case "volup": SetVol(Vol.Value + 5); break;
            case "voldown": SetVol(Vol.Value - 5); break;
            case "fullscreen": ToggleFullscreen(); break;
            case "esc": if (fullscreen) ToggleFullscreen(); break;
            case "mute": Mute_Click(null, null); break;
            case "subs": SubPop.IsOpen = !SubPop.IsOpen; if (SubPop.IsOpen) RefreshSubPopup(); break;
            case "list": ListToggle_Click(null, null); break;
            case "shot": Shot_Click(null, null); break;
            case "next": Next_Click(null, null); break;
            case "prev": Prev_Click(null, null); break;
            case "faster": SetSpeed(Math.Round(Player.GetD("speed", 1) + 0.25, 2)); break;
            case "slower": SetSpeed(Math.Round(Player.GetD("speed", 1) - 0.25, 2)); break;
            case "speed1": SetSpeed(1); break;
            case "framestep": Player.Command("frame-step"); break;
            case "frameback": Player.Command("frame-back-step"); break;
            case "d1minus": AdjDelay(1, -0.1); break;
            case "d1plus": AdjDelay(1, 0.1); break;
            case "d2minus": AdjDelay(2, -0.1); break;
            case "d2plus": AdjDelay(2, 0.1); break;
            case "open": Open_Click(null, null); break;
            case "settings": Settings_Click(null, null); break;
            case "mouse": OnMouseMoved(); break;
        }
    }

    // ---------- mpv events ----------
    void OnProp(string n, string v)
    {
        switch (n)
        {
            case "time-pos":
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                {
                    TimeL.Text = Fmt(t);
                    if (!dragging && duration > 0) Seek.Value = t / duration * 1000;
                }
                break;
            case "duration":
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out duration)) TimeR.Text = Fmt(duration);
                break;
            case "idle-active": SetIdleUi(v == "yes"); break;
            case "pause": PlayBtn.Content = v == "yes" ? "" : ""; break;
            case "mute": MuteBtn.Content = v == "yes" ? "" : ""; break;
            case "volume":
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var vol) && Math.Abs(vol - Vol.Value) > 0.5) { Vol.Value = vol; }
                break;
            case "sub-text": Subs?.SetText(1, v); break;
            case "secondary-sub-text": Subs?.SetText(2, v); break;
            case "media-title": if (!string.IsNullOrEmpty(v)) { NowPlaying.Text = v; this.Title = v + " — WinPlayer"; } break;
            case "eof-reached":
                if (v == "yes" && Cfg.AutoPlayNext && !Cfg.Loop && index + 1 < playlist.Count) PlayIndex(index + 1);
                break;
        }
    }

    static string Fmt(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, s));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    // ---------- playback ----------
    void PlayIndex(int i)
    {
        if (i < 0 || i >= playlist.Count) return;
        SaveResume();
        index = i; currentPath = playlist[i];
        List.SelectedIndex = i;
        Seek.Value = 0;
        Player.Command("loadfile", currentPath, "replace");
        Player.Set("pause", false);
        Subs?.SetText(1, ""); Subs?.SetText(2, "");
    }

    void SaveResume()
    {
        if (currentPath == null || !Cfg.ResumePlayback || Player == null) return;
        double pos = Player.GetD("time-pos"), dur = Player.GetD("duration");
        if (dur > 0 && pos > 10 && pos < dur - 15) Cfg.ResumePositions[currentPath] = pos; else Cfg.ResumePositions.Remove(currentPath);
    }

    void OnFileLoaded()
    {
        RefreshTracks();
        if (Cfg.ResumePlayback && currentPath != null && Cfg.ResumePositions.TryGetValue(currentPath, out var p))
            Player.Command("seek", p.ToString(CultureInfo.InvariantCulture), "absolute");
        ApplyDelays();
    }

    void TogglePause() { if (Player.Get("idle-active") == "yes" && playlist.Count > 0) PlayIndex(Math.Max(0, index)); else Player.Command("cycle", "pause"); }
    void SeekBy(double s) => Player.Command("seek", s.ToString(CultureInfo.InvariantCulture), "relative");

    void SetVol(double v) { v = Math.Clamp(v, 0, 130); Vol.Value = v; Player.Command("show-text", $"Volume {v:0}%", "800"); }
    void SetSpeed(double sp)
    {
        sp = Math.Clamp(sp, 0.1, 8); Player.Set("speed", sp); Player.Command("show-text", $"Speed {sp:0.##}x", "800");
    }

    // ---------- tracks ----------
    void RefreshTracks()
    {
        loadingTracks = true;
        var audio = new List<TrackItem>(); var subs = new List<TrackItem> { new() { Id = "no", Text = "Off" } };
        int n = (int)Player.GetD("track-list/count");
        for (int i = 0; i < n; i++)
        {
            string type = Player.Get($"track-list/{i}/type"), id = Player.Get($"track-list/{i}/id");
            string lang = Player.Get($"track-list/{i}/lang") ?? "", title = Player.Get($"track-list/{i}/title") ?? "";
            bool ext = Player.Get($"track-list/{i}/external") == "yes";
            string text = $"#{id}  {(lang != "" ? lang.ToUpper() : "und")}{(title != "" ? " · " + title : "")}{(ext ? " (file)" : "")}";
            var it = new TrackItem { Id = id, Text = text, Lang = lang };
            if (type == "audio") audio.Add(it); else if (type == "sub") subs.Add(it);
        }
        AudioBox.ItemsSource = audio;
        Sid1.ItemsSource = subs.ToList(); Sid2.ItemsSource = subs.ToList();
        SelectById(AudioBox, Player.Get("aid"));

        // preferred languages -> pick automatically
        var s1 = PickSub(subs, Cfg.Sub1.PreferredLang, null);
        var s2 = PickSub(subs, Cfg.Sub2.PreferredLang, s1?.Id);
        string cur1 = Player.Get("sid"), cur2 = Player.Get("secondary-sid");
        if (s1 != null) { Player.Set("sid", s1.Id); cur1 = s1.Id; }
        if (s2 != null) { Player.Set("secondary-sid", s2.Id); cur2 = s2.Id; }
        SelectById(Sid1, cur1 ?? "no"); SelectById(Sid2, cur2 ?? "no");
        loadingTracks = false;
    }

    static TrackItem PickSub(List<TrackItem> subs, string lang, string exceptId)
    {
        if (string.IsNullOrWhiteSpace(lang)) return null;
        return subs.FirstOrDefault(t => t.Id != "no" && t.Id != exceptId && t.Lang.StartsWith(lang.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    static void SelectById(ComboBox c, string id)
    {
        if (c.ItemsSource is IEnumerable<TrackItem> l) c.SelectedItem = l.FirstOrDefault(t => t.Id == id) ?? l.FirstOrDefault();
    }

    void Audio_Changed(object s, SelectionChangedEventArgs e) { if (!loadingTracks && AudioBox.SelectedItem is TrackItem t) Player.Set("aid", t.Id); }
    void Sid1_Changed(object s, SelectionChangedEventArgs e) { if (!loadingTracks && Sid1.SelectedItem is TrackItem t) { Player.Set("sid", t.Id); if (t.Id == "no") Subs.SetText(1, ""); } }
    void Sid2_Changed(object s, SelectionChangedEventArgs e) { if (!loadingTracks && Sid2.SelectedItem is TrackItem t) { Player.Set("secondary-sid", t.Id); if (t.Id == "no") Subs.SetText(2, ""); } }

    void AddSub_Click(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.sup|All files|*.*", Multiselect = false };
        if (d.ShowDialog() == true) AddSubtitle(d.FileName, (string)((Button)s).Tag == "2");
    }

    void AddTwoSubs_Click(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.sup|All files|*.*", Multiselect = true, Title = "Select 1 or 2 subtitle files (first = Subtitle 1, second = Subtitle 2)" };
        if (d.ShowDialog() != true) return;
        var files = d.FileNames.Take(2).ToArray();
        static bool Empty(string id) => string.IsNullOrEmpty(id) || id == "no";
        bool s1Empty = Empty(Player.Get("sid")), s2Empty = Empty(Player.Get("secondary-sid"));
        if (files.Length == 2 || s1Empty)
        {
            // two files -> Sub 1 + Sub 2; one file and Sub 1 is free -> Sub 1
            AddSubtitle(files[0], false);
            if (files.Length > 1) AddSubtitle(files[1], true);
        }
        else AddSubtitle(files[0], true);   // Sub 1 already has one -> this goes to Sub 2 (replaces it if both are taken)
    }

    void AddSubtitle(string path, bool second)
    {
        // mpv auto-selects a newly added track as the primary one; remember what was active so we can restore it.
        string prevSid = Player.Get("sid"), prevSec = Player.Get("secondary-sid");
        Player.Command("sub-add", path, "cached");
        RefreshTracks();
        int n = (int)Player.GetD("track-list/count"); string last = null;
        string want = System.IO.Path.GetFullPath(path);
        for (int i = 0; i < n; i++)
        {
            if (Player.Get($"track-list/{i}/type") != "sub") continue;
            var f = Player.Get($"track-list/{i}/external-filename");
            if (f != null && string.Equals(System.IO.Path.GetFullPath(f), want, StringComparison.OrdinalIgnoreCase)) last = Player.Get($"track-list/{i}/id");
        }
        if (last == null) return;
        if (second)
        {
            // secondary-sid is ignored by mpv while it equals sid, so put the primary back first
            if (prevSid != null && prevSid != last) Player.Set("sid", prevSid);
            Player.Set("secondary-sid", last);
        }
        else
        {
            if (prevSec == last) Player.Set("secondary-sid", "no");
            Player.Set("sid", last);
        }
        loadingTracks = true;
        SelectById(Sid1, Player.Get("sid") ?? "no"); SelectById(Sid2, Player.Get("secondary-sid") ?? "no");
        loadingTracks = false;
    }

    void RefreshSubPopup()
    {
        Delay1.Text = Cfg.Sub1.DelaySec.ToString("0.##", CultureInfo.InvariantCulture);
        Delay2.Text = Cfg.Sub2.DelaySec.ToString("0.##", CultureInfo.InvariantCulture);
        SelectLayoutItem();
    }

    void SelectLayoutItem()
    {
        foreach (ComboBoxItem it in LayoutBox.Items) if ((string)it.Tag == Cfg.Layout) { LayoutBox.SelectedItem = it; break; }
    }

    void Layout_Changed(object s, SelectionChangedEventArgs e)
    {
        if (LayoutBox.SelectedItem is ComboBoxItem it && Subs != null) { Cfg.Layout = (string)it.Tag; Subs.Render(); settingsWin?.SyncFromConfig(); }
    }

    public void ApplyDelays()
    {
        Player.Set("sub-delay", Cfg.Sub1.DelaySec);
        Player.Set("secondary-sub-delay", Cfg.Sub2.DelaySec);
    }

    void AdjDelay(int which, double d)
    {
        var st = which == 1 ? Cfg.Sub1 : Cfg.Sub2;
        st.DelaySec = Math.Round(st.DelaySec + d, 2);
        ApplyDelays(); RefreshSubPopup();
        Player.Command("show-text", $"Subtitle {which} delay {st.DelaySec:0.##}s", "800");
    }

    void Delay_Changed(object s, RoutedEventArgs e)
    {
        if (double.TryParse(Delay1.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) Cfg.Sub1.DelaySec = a;
        if (double.TryParse(Delay2.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) Cfg.Sub2.DelaySec = b;
        ApplyDelays();
    }
    void Delay_Key(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { Delay_Changed(s, null); Keyboard.ClearFocus(); } }

    void OpenSubSettings_Click(object s, RoutedEventArgs e) { SubPop.IsOpen = false; OpenSettings(1); }

    // ---------- UI handlers ----------
    void Play_Click(object s, RoutedEventArgs e) => TogglePause();
    void Stop_Click(object s, RoutedEventArgs e) { SaveResume(); Player.Command("stop"); Seek.Value = 0; TimeL.Text = "0:00"; }
    void Next_Click(object s, RoutedEventArgs e) { if (index + 1 < playlist.Count) PlayIndex(index + 1); }
    void Prev_Click(object s, RoutedEventArgs e) { if (Player.GetD("time-pos") > 5) Player.Command("seek", "0", "absolute"); else if (index > 0) PlayIndex(index - 1); }
    void Mute_Click(object s, RoutedEventArgs e) => Player.Command("cycle", "mute");
    void Vol_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (ready) { Player.Set("volume", e.NewValue); Cfg.Volume = (int)e.NewValue; } }
    void Speed_Changed(object s, SelectionChangedEventArgs e)
    {
        if (!ready || SpeedBox.SelectedItem is not string t) return;
        Player.Set("speed", double.Parse(t.TrimEnd('x'), CultureInfo.InvariantCulture));
    }
    void Sub_Click(object s, RoutedEventArgs e) { RefreshSubPopup(); SubPop.IsOpen = true; }
    void ListToggle_Click(object s, RoutedEventArgs e) => ListPanel.Visibility = ListPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    void Full_Click(object s, RoutedEventArgs e) => ToggleFullscreen();
    void Settings_Click(object s, RoutedEventArgs e) => OpenSettings(0);

    void Shot_Click(object s, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(Cfg.ScreenshotDir); Player.Set("screenshot-directory", Cfg.ScreenshotDir); } catch { }
        Player.Command("screenshot"); Player.Command("show-text", "Screenshot saved", "1000");
    }

    void Open_Click(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Multiselect = true, Filter = "All media|*.*" };
        if (d.ShowDialog() != true) return;
        playlist.Clear(); foreach (var f in d.FileNames) playlist.Add(f);
        PlayIndex(0);
    }
    void AddFiles_Click(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Multiselect = true, Filter = "All media|*.*" };
        if (d.ShowDialog() != true) return;
        bool wasEmpty = playlist.Count == 0;
        foreach (var f in d.FileNames) playlist.Add(f);
        if (wasEmpty) PlayIndex(0);
    }
    void ClearList_Click(object s, RoutedEventArgs e) { playlist.Clear(); index = -1; }
    void List_DoubleClick(object s, MouseButtonEventArgs e) { if (List.SelectedIndex >= 0) PlayIndex(List.SelectedIndex); }

    double SeekRatio(MouseEventArgs e)
    {
        double w = Math.Max(1, Seek.ActualWidth - 10);
        return Math.Clamp((e.GetPosition(Seek).X - 5) / w, 0, 1);
    }

    void Seek_Down(object s, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Primitives.Thumb || duration <= 0) { dragging = duration > 0; return; }
        // click on the track: jump straight there and keep dragging from that point
        dragging = true; Seek.CaptureMouse();
        Seek.Value = SeekRatio(e) * Seek.Maximum;
        e.Handled = true;
    }

    // show the time under the thumb while scrubbing
    void Seek_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (dragging && duration > 0) TimeL.Text = Fmt(e.NewValue / Seek.Maximum * duration);
    }

    void Seek_Move(object s, MouseEventArgs e)
    {
        if (dragging && Seek.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Seek.Value = SeekRatio(e) * Seek.Maximum;
    }

    void Seek_Up(object s, MouseButtonEventArgs e)
    {
        if (Seek.IsMouseCaptured) Seek.ReleaseMouseCapture();
        if (!dragging) return;
        double target = Seek.Value;
        Dispatcher.BeginInvoke(() =>
        {
            if (duration > 0) Player.Command("seek", (target / Seek.Maximum * duration).ToString(CultureInfo.InvariantCulture), "absolute");
            dragging = false;
        }, DispatcherPriority.Input);
    }

    void DropZone_Click(object s, MouseButtonEventArgs e) => Open_Click(null, null);

    void SetIdleUi(bool idle)
    {
        DropZone.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        Host.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        if (idle) { Seek.Value = 0; TimeL.Text = "0:00"; TimeR.Text = "0:00"; duration = 0; }
    }

    void OnDrop(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = ((string[])e.Data.GetData(DataFormats.FileDrop)).ToList();
        var subs = files.Where(f => SubExt.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
        var media = files.Except(subs).ToList();
        if (media.Count > 0) { playlist.Clear(); foreach (var f in media) playlist.Add(f); PlayIndex(0); }
        bool second = false;
        foreach (var sf in subs) { AddSubtitle(sf, second); second = true; }
    }

    // ---------- fullscreen ----------
    void ToggleFullscreen()
    {
        if (!fullscreen)
        {
            prevState = WindowState; prevStyle = WindowStyle; prevResize = ResizeMode;
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
            fullscreen = true; SetBarVisible(false);
        }
        else
        {
            fullscreen = false; WindowStyle = prevStyle; ResizeMode = prevResize; WindowState = prevState; SetBarVisible(true);
        }
    }

    void SetBarVisible(bool v)
    {
        Bar.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
        if (!v) ListPanel.Visibility = Visibility.Collapsed;
    }

    void OnMouseMoved()
    {
        if (!fullscreen) return;
        var p = System.Windows.Forms.Cursor.Position;
        var screenH = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds.Height;
        if (p.Y > screenH - 110) { SetBarVisible(true); hideTimer.Stop(); hideTimer.Start(); }
        else if (Bar.Visibility == Visibility.Visible && !Bar.IsMouseOver) { hideTimer.Stop(); hideTimer.Start(); }
    }

    // ---------- settings ----------
    void OpenSettings(int tab)
    {
        if (settingsWin == null || !settingsWin.IsLoaded)
        {
            settingsWin = new SettingsWindow(this) { Owner = this };
            settingsWin.Closed += (_, _) => { Subs.Preview = false; Subs.Render(); Cfg.Save(); };
            settingsWin.Show();
        }
        settingsWin.SelectTab(tab);
        settingsWin.Activate();
    }

    void OnClosing(object s, System.ComponentModel.CancelEventArgs e)
    {
        SaveResume();
        Cfg.Save();
        Player?.Dispose();
    }
}
