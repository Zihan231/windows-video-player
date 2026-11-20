using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XRPlayer;

/// <summary>All player settings. Built in code; changes apply live to the playing video.</summary>
public class SettingsWindow : Window
{
    readonly MainWindow main;
    readonly AppSettings cfg;
    readonly Mpv mpv;
    readonly TabControl tabs = new();
    ComboBox layoutBox; CheckBox sub2Top; bool syncing;

    static readonly Brush Fg = (Brush)Application.Current.FindResource("Fg");
    static readonly Brush Dim = (Brush)Application.Current.FindResource("Dim");
    static readonly Brush PanelBg = (Brush)Application.Current.FindResource("Panel");

    public SettingsWindow(MainWindow m)
    {
        main = m; cfg = m.Cfg; mpv = m.Player;
        Title = "Settings — XR Player"; Width = 860; Height = 720; MinWidth = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");
        Foreground = Fg;

        tabs.Background = Brushes.Transparent; tabs.BorderThickness = new Thickness(0); tabs.Margin = new Thickness(8);
        tabs.Items.Add(Tab("General", BuildGeneral()));
        tabs.Items.Add(Tab("Video", BuildVideo()));
        tabs.Items.Add(Tab("Audio", BuildAudio()));
        tabs.Items.Add(Tab("Subtitles", BuildSubtitles()));
        Content = tabs;
        Loaded += (_, _) => { main.Subs.Preview = tabs.SelectedIndex == 3; main.Subs.Render(); };
        tabs.SelectionChanged += (_, _) => { main.Subs.Preview = tabs.SelectedIndex == 3; main.Subs.Render(); };
    }

    public void SelectTab(int i) => tabs.SelectedIndex = i;

    public void SyncFromConfig()
    {
        syncing = true;
        foreach (ComboBoxItem it in layoutBox.Items) if ((string)it.Tag == cfg.Layout) layoutBox.SelectedItem = it;
        sub2Top.IsChecked = cfg.Sub2OnTop;
        syncing = false;
    }

    // ---------- tiny UI toolkit ----------
    static TabItem Tab(string header, UIElement body) => new()
    {
        Header = header,
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body, Padding = new Thickness(6) }
    };

    static StackPanel Page() => new() { Margin = new Thickness(14) };

    static TextBlock Heading(string t) => new() { Text = t, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6), Foreground = (Brush)Application.Current.FindResource("Accent") };

    static FrameworkElement Row(string label, FrameworkElement ctl, string hint = null)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Dim });
        Grid.SetColumn(ctl, 1); g.Children.Add(ctl);
        if (hint != null) g.ToolTip = hint;
        return g;
    }

    static CheckBox Check(string text, bool v, Action<bool> on)
    {
        var c = new CheckBox { Content = text, IsChecked = v, Foreground = Fg };
        c.Checked += (_, _) => on(true); c.Unchecked += (_, _) => on(false);
        return c;
    }

    static FrameworkElement Slide(double min, double max, double v, Action<double> on, double step = 1, string fmt = "0.##")
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = v, SmallChange = step, LargeChange = step * 5, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        if (step >= 1) { s.TickFrequency = step; s.IsSnapToTickEnabled = true; }
        var t = new TextBlock { Text = v.ToString(fmt, CultureInfo.InvariantCulture), Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, e) => { t.Text = e.NewValue.ToString(fmt, CultureInfo.InvariantCulture); on(e.NewValue); };
        var d = new DockPanel(); DockPanel.SetDock(t, Dock.Right); d.Children.Add(t); d.Children.Add(s);
        return d;
    }

    static ComboBox Combo(IEnumerable<string> items, string sel, Action<string> on, bool editable = false)
    {
        var c = new ComboBox { IsEditable = editable, ItemsSource = items.ToList(), Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        c.Text = sel; if (!editable) c.SelectedItem = sel;
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is string s) on(s); };
        if (editable) c.LostFocus += (_, _) => on(c.Text);
        return c;
    }

    static Button ColorBtn(string hex, Action<string> on)
    {
        var b = new Button { Width = 90, Height = 26, HorizontalAlignment = HorizontalAlignment.Left, Content = hex, BorderThickness = new Thickness(1) };
        void Paint(string h)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(h);
                b.Background = new SolidColorBrush(c); b.Content = h;
                b.Foreground = (c.R * 0.3 + c.G * 0.59 + c.B * 0.11) > 128 ? Brushes.Black : Brushes.White;
            }
            catch { }
        }
        Paint(hex);
        b.Click += (_, _) =>
        {
            var c = (Color)ColorConverter.ConvertFromString((string)b.Content);
            var d = new System.Windows.Forms.ColorDialog { Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B), FullOpen = true };
            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var h = $"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}"; Paint(h); on(h);
            }
        };
        return b;
    }

    static TextBox Text(string v, Action<string> on, double w = 240)
    {
        var t = new TextBox { Text = v, Width = w, HorizontalAlignment = HorizontalAlignment.Left, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        t.LostFocus += (_, _) => on(t.Text);
        return t;
    }

    // ---------- General ----------
    UIElement BuildGeneral()
    {
        var p = Page();
        p.Children.Add(Heading("Playback"));
        p.Children.Add(Check("Resume where I left off", cfg.ResumePlayback, v => cfg.ResumePlayback = v));
        p.Children.Add(Check("Play next item in playlist automatically", cfg.AutoPlayNext, v => cfg.AutoPlayNext = v));
        p.Children.Add(Check("Loop current file", cfg.Loop, v => { cfg.Loop = v; mpv.Set("loop-file", v ? "inf" : "no"); }));
        p.Children.Add(Check("Always on top", cfg.AlwaysOnTop, v => { cfg.AlwaysOnTop = v; main.Topmost = v; }));
        p.Children.Add(Row("Small seek (seconds)", Slide(1, 60, cfg.SeekSmall, v => cfg.SeekSmall = (int)v)));
        p.Children.Add(Row("Big seek (seconds)", Slide(5, 300, cfg.SeekBig, v => cfg.SeekBig = (int)v, 5)));
        p.Children.Add(Row("Playback speed", Slide(0.25, 4, mpv.GetD("speed", 1), v => mpv.Set("speed", v), 0.05)));
        p.Children.Add(Heading("Performance"));
        p.Children.Add(Row("Hardware decoding", Combo(new[] { "no", "auto-safe", "auto", "d3d11va", "nvdec", "dxva2" }, cfg.Hwdec, v => { cfg.Hwdec = v; mpv.Set("hwdec", v); }),
            "auto-safe is the safest GPU option. Use 'no' if a file shows green/corrupt frames."));
        p.Children.Add(Heading("Screenshots"));
        p.Children.Add(Row("Save folder", Text(cfg.ScreenshotDir, v => cfg.ScreenshotDir = v, 380)));
        var reset = new Button { Content = "Reset all settings to default", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 0) };
        reset.Click += (_, _) =>
        {
            var n = new AppSettings();
            cfg.Sub1 = n.Sub1; cfg.Sub2 = n.Sub2; cfg.Layout = n.Layout; cfg.Sub2OnTop = n.Sub2OnTop; cfg.StackGap = n.StackGap;
            cfg.Save(); MessageBox.Show(this, "Subtitle settings reset. Reopen this window to see them.", "XR Player"); main.Subs.Render(); Close();
        };
        p.Children.Add(reset);
        return p;
    }

    // ---------- Video ----------
    UIElement BuildVideo()
    {
        var p = Page();
        p.Children.Add(Heading("Picture"));
        foreach (var (label, prop) in new[] { ("Brightness", "brightness"), ("Contrast", "contrast"), ("Saturation", "saturation"), ("Gamma", "gamma"), ("Hue", "hue") })
            p.Children.Add(Row(label, Slide(-100, 100, mpv.GetD(prop), v => mpv.Set(prop, (int)v))));
        p.Children.Add(Heading("Geometry"));
        p.Children.Add(Row("Aspect ratio", Combo(new[] { "Default", "16:9", "4:3", "21:9", "2.35:1", "1:1", "16:10" }, "Default", v => mpv.Set("video-aspect-override", v == "Default" ? "-1" : v))));
        p.Children.Add(Row("Rotate", Combo(new[] { "0", "90", "180", "270" }, "0", v => mpv.Set("video-rotate", v))));
        p.Children.Add(Row("Zoom", Slide(-2, 2, mpv.GetD("video-zoom"), v => mpv.Set("video-zoom", v), 0.05)));
        p.Children.Add(Heading("Quality"));
        p.Children.Add(Row("Deinterlace", Combo(new[] { "no", "yes", "auto" }, cfg.Deinterlace, v => { cfg.Deinterlace = v; mpv.Set("deinterlace", v); })));
        p.Children.Add(Row("Scaling algorithm", Combo(new[] { "bilinear", "bicubic_fast", "spline36", "lanczos", "ewa_lanczossharp" }, cfg.Scale, v => { cfg.Scale = v; mpv.Set("scale", v); })));
        var reset = new Button { Content = "Reset picture", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        reset.Click += (_, _) => { foreach (var k in new[] { "brightness", "contrast", "saturation", "gamma", "hue" }) mpv.Set(k, 0); tabs.Items[1] = Tab("Video", BuildVideo()); };
        p.Children.Add(reset);
        return p;
    }

    // ---------- Audio ----------
    readonly double[] eq = new double[6];
    static readonly (string, int)[] Bands = { ("60 Hz", 60), ("170 Hz", 170), ("500 Hz", 500), ("1.5 kHz", 1500), ("5 kHz", 5000), ("12 kHz", 12000) };

    void ApplyEq()
    {
        if (eq.All(x => x == 0)) { mpv.Set("af", "no"); return; }
        var parts = Bands.Select((b, i) => $"equalizer=f={b.Item2}:t=o:w=1.2:g={eq[i].ToString(CultureInfo.InvariantCulture)}");
        mpv.Set("af", "lavfi=[" + string.Join(",", parts) + "]");
    }

    UIElement BuildAudio()
    {
        var p = Page();
        p.Children.Add(Heading("Output"));
        p.Children.Add(Row("Volume (max 130%)", Slide(0, 130, cfg.Volume, v => mpv.Set("volume", (int)v))));
        p.Children.Add(Row("Audio delay (seconds)", Slide(-5, 5, mpv.GetD("audio-delay"), v => mpv.Set("audio-delay", Math.Round(v, 2)), 0.05)));
        p.Children.Add(Row("Channels", Combo(new[] { "auto", "stereo", "mono", "5.1", "7.1" }, "auto", v => mpv.Set("audio-channels", v))));
        p.Children.Add(Row("Preferred audio language", Text(cfg.AudioLang, v => { cfg.AudioLang = v; mpv.Set("alang", v); }, 120), "ISO code, e.g. eng, jpn, ben. Applies to the next file."));
        p.Children.Add(Check("Normalize volume when downmixing", false, v => mpv.Set("audio-normalize-downmix", v)));
        p.Children.Add(Heading("Equalizer"));
        for (int i = 0; i < Bands.Length; i++)
        {
            int k = i;
            p.Children.Add(Row(Bands[i].Item1 + " (dB)", Slide(-12, 12, 0, v => { eq[k] = v; ApplyEq(); })));
        }
        var reset = new Button { Content = "Reset equalizer", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        reset.Click += (_, _) => { Array.Clear(eq); ApplyEq(); tabs.Items[2] = Tab("Audio", BuildAudio()); };
        p.Children.Add(reset);
        return p;
    }

    // ---------- Subtitles ----------
    UIElement BuildSubtitles()
    {
        var p = Page();
        p.Children.Add(new TextBlock
        {
            Text = "Pick two subtitle tracks from the ⌨ button in the player bar (or here). Changes show live on the video behind this window.",
            Foreground = Dim, TextWrapping = TextWrapping.Wrap
        });
        p.Children.Add(Heading("Two-subtitle layout"));

        layoutBox = new ComboBox { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (tag, text) in new[] { ("Stacked", "Stacked together (one block, both at the bottom)"), ("TopBottom", "Sub 1 at bottom, Sub 2 at top"), ("Custom", "Custom — position each one yourself") })
            layoutBox.Items.Add(new ComboBoxItem { Tag = tag, Content = text, IsSelected = tag == cfg.Layout });
        layoutBox.SelectionChanged += (_, _) =>
        {
            if (syncing || layoutBox.SelectedItem is not ComboBoxItem it) return;
            cfg.Layout = (string)it.Tag; main.Subs.Render();
        };
        p.Children.Add(Row("Layout", layoutBox));
        sub2Top = Check("Stacked: show Subtitle 2 above Subtitle 1", cfg.Sub2OnTop, v => { if (!syncing) { cfg.Sub2OnTop = v; main.Subs.Render(); } });
        p.Children.Add(sub2Top);
        p.Children.Add(Row("Gap between the two (px)", Slide(0, 60, cfg.StackGap, v => { cfg.StackGap = (int)v; main.Subs.Render(); })));
        p.Children.Add(Check("Use native rendering (keeps fancy ASS styling / picture subtitles; disables the custom styles below)", cfg.NativeSubRendering, v =>
        {
            cfg.NativeSubRendering = v; mpv.Set("sub-visibility", v); mpv.Set("secondary-sub-visibility", v); main.Subs.Render();
        }));

        var cols = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        cols.ColumnDefinitions.Add(new ColumnDefinition()); cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); cols.ColumnDefinitions.Add(new ColumnDefinition());
        var a = StyleEditor("Subtitle 1", cfg.Sub1, 1); var b = StyleEditor("Subtitle 2", cfg.Sub2, 2);
        Grid.SetColumn(b, 2); cols.Children.Add(a); cols.Children.Add(b);
        p.Children.Add(cols);
        return p;
    }

    UIElement StyleEditor(string title, SubStyle s, int which)
    {
        void Changed() { main.Subs.Render(); }
        var p = new StackPanel();
        var box = new Border { Background = PanelBg, CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Child = p };
        p.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold });
        p.Children.Add(Check("Show this subtitle", s.Enabled, v => { s.Enabled = v; Changed(); }));

        p.Children.Add(Heading("Font"));
        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(x => x).ToList();
        var fc = new ComboBox { IsEditable = true, ItemsSource = fonts, Text = s.Font, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        fc.SelectionChanged += (_, _) => { if (fc.SelectedItem is string f) { s.Font = f; Changed(); } };
        fc.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(fc.Text)) { s.Font = fc.Text; Changed(); } };
        p.Children.Add(Stack(fc));
        p.Children.Add(Stack(Slide(16, 140, s.Size, v => { s.Size = (int)v; Changed(); }), "Size"));
        var style = new StackPanel { Orientation = Orientation.Horizontal };
        style.Children.Add(Check("Bold", s.Bold, v => { s.Bold = v; Changed(); }));
        style.Children.Add(Check("Italic", s.Italic, v => { s.Italic = v; Changed(); })); ((CheckBox)style.Children[1]).Margin = new Thickness(14, 4, 0, 4);
        style.Children.Add(Check("Underline", s.Underline, v => { s.Underline = v; Changed(); })); ((CheckBox)style.Children[2]).Margin = new Thickness(14, 4, 0, 4);
        p.Children.Add(style);
        p.Children.Add(Stack(ColorBtn(s.Color, v => { s.Color = v; Changed(); }), "Text color"));
        p.Children.Add(Stack(Slide(10, 100, s.Opacity, v => { s.Opacity = (int)v; Changed(); }), "Text opacity %"));
        p.Children.Add(Stack(Slide(-5, 20, s.LetterSpacing, v => { s.LetterSpacing = v; Changed(); }, 0.5), "Letter spacing"));

        p.Children.Add(Heading("Outline, shadow & background"));
        p.Children.Add(Check("Use background box instead of outline", s.UseBox, v => { s.UseBox = v; Changed(); }));
        p.Children.Add(Stack(ColorBtn(s.OutlineColor, v => { s.OutlineColor = v; Changed(); }), "Outline / box color"));
        p.Children.Add(Stack(Slide(0, 10, s.OutlineWidth, v => { s.OutlineWidth = v; Changed(); }, 0.5), "Outline width"));
        p.Children.Add(Stack(Slide(0, 30, s.BoxPadding, v => { s.BoxPadding = (int)v; Changed(); }), "Box padding"));
        p.Children.Add(Stack(Slide(0, 100, s.BoxOpacity, v => { s.BoxOpacity = (int)v; Changed(); }), "Box opacity %"));
        p.Children.Add(Stack(Slide(0, 10, s.ShadowDepth, v => { s.ShadowDepth = v; Changed(); }, 0.5), "Shadow depth"));
        p.Children.Add(Stack(ColorBtn(s.ShadowColor, v => { s.ShadowColor = v; Changed(); }), "Shadow color"));
        p.Children.Add(Stack(Slide(0, 5, s.Blur, v => { s.Blur = v; Changed(); }, 0.25), "Edge blur"));

        p.Children.Add(Heading("Position & timing"));
        var al = new ComboBox { Width = 140, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new[] { "Left", "Center", "Right" }, SelectedIndex = s.Align };
        al.SelectionChanged += (_, _) => { s.Align = al.SelectedIndex; Changed(); };
        p.Children.Add(Stack(al, "Alignment"));
        p.Children.Add(Stack(Slide(0, 100, s.PosY, v => { s.PosY = v; Changed(); }, 1), which == 1 ? "Vertical position % (stacked: block position)" : "Vertical position % (custom layout)"));
        p.Children.Add(Stack(Slide(0, 100, s.PosX, v => { s.PosX = v; Changed(); }, 1), "Horizontal position % (centered text)"));
        p.Children.Add(Stack(Slide(-30, 30, s.DelaySec, v => { s.DelaySec = Math.Round(v, 2); main.ApplyDelays(); }, 0.1), "Delay (seconds)"));
        p.Children.Add(Stack(Text(s.PreferredLang, v => s.PreferredLang = v, 100), "Preferred language (e.g. eng, ben)"));
        return box;
    }

    static StackPanel Stack(FrameworkElement ctl, string label = null)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
        if (label != null) sp.Children.Add(new TextBlock { Text = label, Foreground = Dim, FontSize = 12 });
        sp.Children.Add(ctl);
        return sp;
    }
}
