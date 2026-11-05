using System.Drawing.Drawing2D;
namespace Drex;

public static class Theme
{
    // Base: cooler, cleaner near-black — "system hygiene"
    public static readonly Color Bg = ColorTranslator.FromHtml("#0A0E14");
    public static readonly Color Surf = ColorTranslator.FromHtml("#111822");
    public static readonly Color Surf2 = ColorTranslator.FromHtml("#1A2230");
    public static readonly Color Txt = ColorTranslator.FromHtml("#F0F4F8");
    public static readonly Color Silver = ColorTranslator.FromHtml("#B8C0CC");
    public static readonly Color Muted = ColorTranslator.FromHtml("#8491A3");

    // Accent: Teal-cyan — "clean", "fresh", "system" (not purple SaaS)
    public static readonly Color Accent = ColorTranslator.FromHtml("#00D4A0");
    public static readonly Color AccentHi = ColorTranslator.FromHtml("#33E0B8");
    public static readonly Color AccentLo = ColorTranslator.FromHtml("#00A880");
    public static readonly Color AccentMuted = ColorTranslator.FromHtml("#00D4A033"); // 20% opacity

    // Semantic (kept — they work well)
    public static readonly Color Good = ColorTranslator.FromHtml("#22C55E");
    public static readonly Color Warn = ColorTranslator.FromHtml("#FBBF24");
    public static readonly Color Bad = ColorTranslator.FromHtml("#EF4444");
    public static readonly Color Alt = ColorTranslator.FromHtml("#0E1520");
    public static readonly Color Sel = ColorTranslator.FromHtml("#0D3B2E"); // teal-tinted selection

    // Backward compat aliases (for any missed refs)
    public static readonly Color Violet = Accent;
    public static readonly Color VioletHi = AccentHi;
    public static readonly Color VioletLo = AccentLo;

    static readonly Dictionary<string, Font> _fonts = new();
    public static Font F(float size, bool bold = false) => Get(bold ? "Segoe UI Semibold" : "Segoe UI", size);
    public static Font Icons(float size) => Get("Segoe MDL2 Assets", size);
    static Font Get(string name, float size)
    {
        var k = name + "|" + size;
        if (!_fonts.TryGetValue(k, out var f)) { f = new Font(name, size, FontStyle.Regular, GraphicsUnit.Point); _fonts[k] = f; }
        return f;
    }

    public static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        if (d <= 0 || r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Color Interpolate(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}

public static class AppIcon
{
    // The logo is embedded in the EXE so it works in single-file builds and the installed copy.
    public static Icon Get(int size)
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("drex.ico");
            if (s != null) return new Icon(s, new Size(size, size));
        }
        catch { }
        return SystemIcons.Application;
    }
}
