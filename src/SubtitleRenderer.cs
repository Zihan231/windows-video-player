using System.Globalization;
using System.Text;

namespace XRPlayer;

/// <summary>Draws two independently styled subtitle tracks through mpv's ASS OSD overlay.</summary>
public class SubtitleRenderer
{
    const int W = 1920, H = 1080;
    readonly Mpv mpv;
    readonly AppSettings cfg;
    string t1 = "", t2 = "";
    public bool Preview;   // show sample text (used while the settings window is open)

    public SubtitleRenderer(Mpv m, AppSettings s) { mpv = m; cfg = s; }

    public void SetText(int which, string text)
    {
        if (which == 1) t1 = text ?? ""; else t2 = text ?? "";
        Render();
    }

    static string Clean(string s) =>
        s.Replace("\\", "∖").Replace("{", "(").Replace("}", ")").Replace("\r", "").Replace("\n", "\\N");

    static string Col(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) hex = "FFFFFF";
        return "&H" + hex.Substring(4, 2) + hex.Substring(2, 2) + hex.Substring(0, 2) + "&";
    }
    static string Alpha(int opacityPct) => "&H" + (255 - Math.Clamp(opacityPct, 0, 100) * 255 / 100).ToString("X2") + "&";
    static string N(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Style override tags for one subtitle block.</summary>
    static string Tags(SubStyle s)
    {
        var b = new StringBuilder("{");
        b.Append("\\fn").Append(s.Font);
        b.Append("\\fs").Append(s.Size);
        b.Append("\\b").Append(s.Bold ? 1 : 0).Append("\\i").Append(s.Italic ? 1 : 0).Append("\\u").Append(s.Underline ? 1 : 0);
        b.Append("\\1c").Append(Col(s.Color)).Append("\\1a").Append(Alpha(s.Opacity));
        if (s.UseBox)
        {
            b.Append("\\3c").Append(Col(s.OutlineColor)).Append("\\3a").Append(Alpha(s.BoxOpacity));
            b.Append("\\bord").Append(s.BoxPadding).Append("\\shad0\\blur0");
        }
        else
        {
            b.Append("\\3c").Append(Col(s.OutlineColor)).Append("\\3a&H00&");
            b.Append("\\bord").Append(N(s.OutlineWidth));
            b.Append("\\4c").Append(Col(s.ShadowColor)).Append("\\4a&H40&\\shad").Append(N(s.ShadowDepth));
            b.Append("\\blur").Append(N(s.Blur));
        }
        b.Append("\\fsp").Append(N(s.LetterSpacing));
        b.Append('}');
        return b.ToString();
    }

    static int An(SubStyle s, bool top)
    {
        int col = s.Align == 0 ? 1 : s.Align == 2 ? 3 : 2; // bottom row: 1,2,3
        return top ? col + 6 : col;                         // top row: 7,8,9
    }

    string Sample(int which) => which == 1 ? "Subtitle 1 — sample line" : "Subtitle 2 — নমুনা লাইন";

    public void Render()
    {
        if (mpv == null) return;
        if (cfg.NativeSubRendering) { Clear(0); Clear(1); return; }

        string a = Preview && t1.Length == 0 ? Sample(1) : t1;
        string b = Preview && t2.Length == 0 ? Sample(2) : t2;
        if (!cfg.Sub1.Enabled) a = "";
        if (!cfg.Sub2.Enabled) b = "";

        if (cfg.Layout == "Stacked")
        {
            // One bottom-anchored block: both subtitles share a column; order is configurable.
            var first = (cfg.Sub1, a);    // Subtitle 1 always on top, Subtitle 2 right below it
            var second = (cfg.Sub2, b);
            var sb = new StringBuilder();
            var ref1 = cfg.Sub1;
            int x = ref1.Align == 1 ? (int)(W * ref1.PosX / 100.0) : ref1.Align == 0 ? (int)(W * 0.05) : (int)(W * 0.95);
            int y = (int)(H * cfg.Sub1.PosY / 100.0);
            sb.Append("{\\an").Append(An(ref1, false)).Append("\\pos(").Append(x).Append(',').Append(y).Append(")}");
            bool any = false;
            foreach (var (st, tx) in new[] { first, second })
            {
                if (tx.Length == 0) continue;
                if (any) sb.Append("\\N{\\fs").Append(Math.Max(1, cfg.StackGap)).Append("\\bord0\\shad0} \\N");
                sb.Append(Tags(st)).Append(Clean(tx));
                any = true;
            }
            Send(0, any ? sb.ToString() : "");
            Clear(1);
        }
        else
        {
            bool tb = cfg.Layout == "TopBottom";
            Place(0, cfg.Sub1, a, tb ? 92 : cfg.Sub1.PosY, tb ? false : cfg.Sub1.PosY < 50);
            Place(1, cfg.Sub2, b, tb ? 6 : cfg.Sub2.PosY, tb ? true : cfg.Sub2.PosY < 50);
        }
    }

    void Place(int id, SubStyle s, string text, double yPct, bool top)
    {
        if (text.Length == 0) { Clear(id); return; }
        int x = s.Align == 1 ? (int)(W * s.PosX / 100.0) : s.Align == 0 ? (int)(W * 0.05) : (int)(W * 0.95);
        int y = (int)(H * yPct / 100.0);
        Send(id, "{\\an" + An(s, top) + "\\pos(" + x + "," + y + ")}" + Tags(s) + Clean(text));
    }

    void Send(int id, string data)
    {
        if (data.Length == 0) { Clear(id); return; }
        mpv.Command("osd-overlay", id.ToString(), "ass-events", data, W.ToString(), H.ToString());
    }

    void Clear(int id) => mpv.Command("osd-overlay", id.ToString(), "none", "", "0", "0");
}
