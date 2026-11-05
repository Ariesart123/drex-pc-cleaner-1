using System.Drawing.Drawing2D;
using System.Globalization;
namespace Drex;

public class Card : Panel
{
    public bool Accent;
    // Hierarchical radii: 16 (page), 12 (section), 8 (inline/nested)
    public int Radius = 12;
    float _opacity = 1f;
    public Card() : this(12) { }
    public Card(int radius)
    {
        Radius = radius;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surf;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using var br = new SolidBrush(Color.FromArgb((int)(255 * _opacity), Theme.Surf));
        g.FillPath(br, path);
        using var pen = new Pen(Accent ? Color.FromArgb((int)(110 * _opacity), Theme.Accent) : Color.FromArgb((int)(255 * _opacity), Theme.Surf2), 1f);
        g.DrawPath(pen, path);
    }

    /// <summary>
    /// Fades in the card over the specified duration.
    /// </summary>
    public void FadeIn(int durationMs = 300)
    {
        _opacity = 0f;
        Anim.Float(this, 0f, 1f, durationMs, v => { _opacity = v; Invalidate(); });
    }
}

public class GButton : Control, IButtonControl
{
    public bool Primary;
    float _hoverProgress = 0f;
    float _pressProgress = 0f;
    public DialogResult DialogResult { get; set; }
    public GButton(string text, bool primary = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text; Primary = primary; Font = Theme.F(9.5f, true); Cursor = Cursors.Hand;
        Size = new Size(Math.Max(104, TextRenderer.MeasureText(text, Font).Width + 44), 44);
        Margin = new Padding(0, 0, 6, 0);
    }

    public void NotifyDefault(bool value) { }
    public void PerformClick() { OnClick(EventArgs.Empty); }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnMouseEnter(EventArgs e) { Anim.Float(this, _hoverProgress, 1f, 150, v => { _hoverProgress = v; Invalidate(); }); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Anim.Float(this, _hoverProgress, 0f, 150, v => { _hoverProgress = v; Invalidate(); }); Anim.Float(this, _pressProgress, 0f, 100, v => { _pressProgress = v; Invalidate(); }); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Anim.Float(this, _pressProgress, 1f, 50, v => { _pressProgress = v; Invalidate(); }); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Anim.Float(this, _pressProgress, 0f, 100, v => { _pressProgress = v; Invalidate(); }); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(4, 4, Width - 9, Height - 9);
        using var path = Theme.Round(r, 10);
        Color fore;
        if (Primary && Enabled)
        {
            // Single subtle glow on hover, no multi-layer glow
            if (_hoverProgress > 0f)
            {
                using var glowPath = Theme.Round(Rectangle.Inflate(r, 2, 2), 12);
                using var glowPen = new Pen(Color.FromArgb((int)(30 * _hoverProgress), Theme.Accent), 1.5f);
                g.DrawPath(glowPen, glowPath);
            }
            var startCol = _hoverProgress > 0.5f ? Theme.AccentHi : Theme.Accent;
            using var lg = new LinearGradientBrush(r, startCol, Theme.AccentLo, 90f);
            g.FillPath(lg, path);
            fore = Color.White;
        }
        else
        {
            var fill = !Enabled ? Theme.Surf :
                _pressProgress > 0.5f ? Color.FromArgb(0x1A, 0x2A, 0x3A) :
                _hoverProgress > 0.5f ? Color.FromArgb(0x16, 0x22, 0x30) : Theme.Surf2;
            using var br = new SolidBrush(fill);
            g.FillPath(br, path);
            using var bp = new Pen(Color.FromArgb(0x2D, 0x34, 0x46));
            g.DrawPath(bp, path);
            fore = Enabled ? Theme.Txt : Theme.Muted;
        }
        TextRenderer.DrawText(g, Text, Font, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

public class Toggle : Control
{
    bool _on;
    float _thumbPos;
    public event EventHandler? CheckedChanged;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _on;
        set { if (_on == value) return; _on = value; AnimateThumb(); Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public Toggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(48, 28); Cursor = Cursors.Hand;
        _thumbPos = 0f;
    }
    void AnimateThumb()
    {
        var target = _on ? 1f : 0f;
        Anim.Float(this, _thumbPos, target, 200, v => { _thumbPos = v; Invalidate(); });
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = Theme.Round(r, r.Height / 2);
        // Interpolate track color
        var trackColor = Theme.Interpolate(Color.FromArgb(0x1E, 0x26, 0x34), Theme.Accent, _thumbPos);
        using var br = new SolidBrush(trackColor);
        g.FillPath(br, path);
        int d = r.Height - 8;
        int startX = r.X + 4;
        int endX = r.Right - d - 4;
        int x = startX + (int)((endX - startX) * _thumbPos);
        using var kb = new SolidBrush(_thumbPos > 0.5f ? Color.White : Theme.Silver);
        g.FillEllipse(kb, x, r.Y + 4, d, d);
    }
}

public class DarkBar : Control
{
    int _v;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value { get => _v; set { var n = Math.Clamp(value, 0, 100); if (n != _v) { _v = n; Invalidate(); } } }
    public DarkBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 8;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var tr = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var tp = Theme.Round(tr, Height / 2)) using (var tb = new SolidBrush(Theme.Surf2)) g.FillPath(tb, tp);
        int w = (int)((Width - 1) * _v / 100.0);
        if (w >= Height)
        {
            var fr = new Rectangle(0, 0, w, Height - 1);
            Color a = _v >= 90 ? Theme.Bad : _v >= 75 ? Theme.Warn : Theme.AccentLo;
            Color b = _v >= 90 ? Theme.Bad : _v >= 75 ? Theme.Warn : Theme.AccentHi;
            using var fp = Theme.Round(fr, Height / 2);
            using var lg = new LinearGradientBrush(fr, a, b, 0f);
            g.FillPath(lg, fp);
        }
    }
}

public class Ring : Control
{
    float _v, _animV; string _big = "", _small = "";
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float Value { get => _v; set { if (Math.Abs(_v - value) > 0.01f) { _v = value; AnimateValue(); } } }
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Big { get => _big; set { if (_big != value) { _big = value; Invalidate(); } } }
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Small { get => _small; set { if (_small != value) { _small = value; Invalidate(); } } }
    public Ring()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(120, 120);
    }
    void AnimateValue()
    {
        Anim.Float(this, _animV, _v, 800, v => { _animV = v; Invalidate(); });
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(10, 10, Width - 20, Height - 20);

        // Track: subtle, darker than surface
        using (var track = new Pen(Color.FromArgb(0x1A, 0x22, 0x30), 10f)) g.DrawEllipse(track, r);

        float sweep = 360f * Math.Clamp(_animV, 0f, 100f) / 100f;
        if (sweep > 1f)
        {
            // Accent color based on value - teal for normal, semantic for warnings
            Color col = _animV >= 90 ? Theme.Bad : _animV >= 75 ? Theme.Warn : Theme.Accent;
            using var pen = new Pen(col, 10f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, r, -90f, sweep);
        }
        TextRenderer.DrawText(g, _big, Theme.F(Width > 140 ? 24f : 18f, true), new Rectangle(0, Height / 2 - 28, Width, 36), Theme.Txt, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _small, Theme.F(9f), new Rectangle(0, Height / 2 + 8, Width, 20), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

public class NavItem : Control
{
    bool _sel;
    float _selProgress = 0f;
    float _hoverProgress = 0f;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _sel; set { if (_sel != value) { _sel = value; Anim.Float(this, _selProgress, value ? 1f : 0f, 250, v => { _selProgress = v; Invalidate(); }); } } }
    public NavItem(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text; Cursor = Cursors.Hand; Font = Theme.F(10f);
        Margin = Padding.Empty; BackColor = Theme.Surf;
        // Auto-size: text width + padding, min height 40
        var textSize = TextRenderer.MeasureText(text, Font);
        Height = Math.Max(40, textSize.Height + 16);
        // Fixed width matching sidebar (212) so FlowLayoutPanel lays out correctly
        Width = 212;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnMouseEnter(EventArgs e) { Anim.Float(this, _hoverProgress, 1f, 150, v => { _hoverProgress = v; Invalidate(); }); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Anim.Float(this, _hoverProgress, 0f, 150, v => { _hoverProgress = v; Invalidate(); }); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surf);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width, Height);

        // Selected state with animation
        if (_selProgress > 0f)
        {
            var selAlpha = (int)(0x0D * _selProgress);
            using var bar = new SolidBrush(Color.FromArgb((int)(255 * _selProgress), Theme.Accent));
            g.FillRectangle(bar, 0, 0, 3, Height);
            using var bg = new SolidBrush(Color.FromArgb(selAlpha, 0x3B, 0x2E));
            g.FillRectangle(bg, r);
        }
        // Hover state with animation (only when not selected)
        else if (_hoverProgress > 0f)
        {
            var hoverAlpha = (int)(0x16 * _hoverProgress);
            using var bg = new SolidBrush(Color.FromArgb(hoverAlpha, 0x22, 0x30));
            g.FillRectangle(bg, r);
        }

        // Text: left-aligned with generous padding, no glyph column
        var textRect = new Rectangle(16, 0, Width - 20, Height);
        // Interpolate text color
        var textColor = _selProgress > 0f ? Theme.Interpolate(Theme.Silver, Theme.Txt, _selProgress) : Theme.Silver;
        TextRenderer.DrawText(g, Text, Font, textRect, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

public class DarkList : ListView
{
    // Cached drawing objects to avoid allocation on every draw call
    private static readonly SolidBrush s_bgSel = new SolidBrush(Theme.Sel);
    private static readonly SolidBrush s_bgSurf = new SolidBrush(Theme.Surf);
    private static readonly Pen s_headerLine = new Pen(Theme.Surf2);
    private static readonly SolidBrush s_headerBg = new SolidBrush(Theme.Bg);
    private static readonly Pen s_rowDivider = new Pen(Color.FromArgb(0x1A, 0x22, 0x30)); // subtle divider

    public int BarColumn = -1;
    public DarkList(params (string name, int width)[] cols)
    {
        View = View.Details; FullRowSelect = true; OwnerDraw = true; BorderStyle = BorderStyle.None;
        BackColor = Theme.Surf; ForeColor = Theme.Silver; HideSelection = false; MultiSelect = true; Font = Theme.F(9.5f);
        DoubleBuffered = true; HeaderStyle = ColumnHeaderStyle.Nonclickable;
        SmallImageList = new ImageList { ImageSize = new Size(1, 34) };
        foreach (var (n, w) in cols) Columns.Add(n, w);
        Native.DarkScroll(this);
        HandleCreated += (_, _) => FitLast();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            s_bgSel?.Dispose();
            s_bgSurf?.Dispose();
            s_headerLine?.Dispose();
            s_headerBg?.Dispose();
            s_rowDivider?.Dispose();
        }
        base.Dispose(disposing);
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); FitLast(); }
    public void FitLast()
    {
        if (Columns.Count == 0 || !IsHandleCreated) return;
        int used = 0;
        for (int i = 0; i < Columns.Count - 1; i++) used += Columns[i].Width;
        int w = ClientSize.Width - used;
        if (w > 60) Columns[Columns.Count - 1].Width = w;
    }
    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        e.Graphics.FillRectangle(s_headerBg, e.Bounds);
        e.Graphics.DrawLine(s_headerLine, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        var r = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(10, e.Bounds.Width - 12), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Theme.F(8.5f, true), r, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    protected override void OnDrawItem(DrawListViewItemEventArgs e) { }
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        var g = e.Graphics; var it = e.Item; if (it == null) return;
        var r = e.Bounds;
        if (e.ColumnIndex == 0) r = new Rectangle(r.X, r.Y, Columns[0].Width, r.Height);

        // Background: selected = teal tint, otherwise uniform surface (no zebra)
        var bgBrush = it.Selected ? s_bgSel : s_bgSurf;
        g.FillRectangle(bgBrush, r);

        // Subtle row divider (except last row)
        if (e.ItemIndex < Items.Count - 1)
        {
            g.DrawLine(s_rowDivider, r.Left + 10, r.Bottom - 1, r.Right - 10, r.Bottom - 1);
        }

        string text = e.SubItem?.Text ?? "";
        if (e.ColumnIndex == BarColumn)
        {
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var tr = new Rectangle(r.X + 10, r.Y + r.Height / 2 - 4, Math.Max(20, r.Width - 74), 8);

            // Progress bar background: subtle surface
            using (var tp = Theme.Round(tr, 4))
            using (var bg = new SolidBrush(Theme.Surf2))
                g.FillPath(bg, tp);

            int w = (int)(tr.Width * Math.Clamp(pct, 0, 100) / 100.0);
            if (w >= 8)
            {
                var fr = new Rectangle(tr.X, tr.Y, w, tr.Height);
                using var fp = Theme.Round(fr, 4);
                // Use Accent gradient (teal) instead of Violet
                using var lg = new LinearGradientBrush(fr, Theme.AccentLo, Theme.AccentHi, 0f);
                g.FillPath(lg, fp);
            }
            TextRenderer.DrawText(g, pct.ToString("0.#", CultureInfo.InvariantCulture) + "%", Theme.F(8.5f), new Rectangle(tr.Right + 4, r.Y, 56, r.Height), Theme.Silver, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            return;
        }
        Color raw = it.UseItemStyleForSubItems ? it.ForeColor : (e.SubItem?.ForeColor ?? ForeColor);
        Color c = raw.ToArgb() != ForeColor.ToArgb() ? raw : (e.ColumnIndex == 0 ? Theme.Txt : Theme.Silver);
        TextRenderer.DrawText(g, text, Theme.F(9.5f), new Rectangle(r.X + 10, r.Y, Math.Max(10, r.Width - 14), r.Height), c, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
