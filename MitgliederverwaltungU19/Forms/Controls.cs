using System.ComponentModel;
using System.Drawing.Drawing2D;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Eintrag der dunklen Seitenleiste: Symbol, Text, optional roter Zähler, aktiver Zustand in Orange.</summary>
internal sealed class SideNavButton : Button
{
    private static readonly Color Idle = Color.FromArgb(0xC7, 0xCB, 0xE0);
    private static readonly Color HoverBack = Color.FromArgb(0x26, 0x2C, 0x4D);
    private bool _hover;

    /// <summary>Zeichen aus der Windows-Symbolschrift „Segoe MDL2 Assets“ (z. B. "").</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set; } = "";
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Badge { get; set; }

    public SideNavButton(string text, string glyph)
    {
        Text = text;
        Glyph = glyph;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Height = 38;
        Margin = new Padding(0, 1, 0, 1);
        TextAlign = ContentAlignment.MiddleLeft;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent?.BackColor ?? Theme.Navy);

        var rect = new Rectangle(Theme.Px(10), Theme.Px(2), Width - Theme.Px(20), Height - Theme.Px(4));
        var back = Active ? Theme.Accent : _hover && Enabled ? HoverBack : Color.Empty;
        if (back != Color.Empty)
        {
            using var path = RoundedRect(rect, 9);
            using var brush = new SolidBrush(back);
            g.FillPath(brush, path);
        }

        var fore = !Enabled ? Color.FromArgb(0x5B, 0x62, 0x86)
            : Active ? Color.FromArgb(0x1A, 0x0D, 0x00)
            : _hover ? Color.White : Idle;
        using var textBrush = new SolidBrush(fore);

        using (var iconFont = new Font("Segoe MDL2 Assets", 12f * Theme.Zoom))
        {
            var iconRect = new RectangleF(rect.X + Theme.Px(12), rect.Y, Theme.Px(26), rect.Height);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Glyph, iconFont, textBrush, iconRect, fmt);
        }

        var textFont = Active ? Theme.Bold : Theme.Body;
        var textFmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(Text, textFont, textBrush, new RectangleF(rect.X + Theme.Px(46), rect.Y, rect.Width - Theme.Px(46) - Theme.Px(Badge > 0 ? 36 : 6), rect.Height), textFmt);

        if (Badge > 0)
        {
            var label = Badge > 99 ? "99+" : Badge.ToString();
            var size = g.MeasureString(label, Theme.Bold);
            var w = Math.Max(Theme.Px(24), (int)size.Width + Theme.Px(10));
            var pill = new Rectangle(rect.Right - w - Theme.Px(8), rect.Y + (rect.Height - Theme.Px(20)) / 2, w, Theme.Px(20));
            using var pillPath = RoundedRect(pill, 10);
            using var pillBrush = new SolidBrush(Theme.Danger);
            g.FillPath(pillBrush, pillPath);
            using var white = new SolidBrush(Color.White);
            g.DrawString(label, Theme.Bold, white, pill, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }
    }

    internal static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Kennzahl-Karte mit farbigem Streifen (Zahl groß, Beschriftung klein). Klick löst Click aus.
/// Text und Abstände werden selbst gezeichnet und aus der Schrifthöhe berechnet, damit nichts abgeschnitten wird –
/// unabhängig von Bildschirm, Skalierung und Schriftart.
/// </summary>
internal sealed class StatCard : Panel
{
    private static readonly Font ValueFont = new(Theme.Body.FontFamily, 20f * Theme.Zoom, FontStyle.Bold);
    private string _value = "–";
    private string _caption;
    private Color _accent;

    /// <summary>Höhe, die eine Karte für ihren Inhalt mindestens braucht.</summary>
    public static int PreferredHeight => Theme.Px(14) * 2 + ValueFont.Height + Theme.Px(2) + Theme.Body.Height;

    public StatCard(string caption, Color accent, bool clickable = false)
    {
        Size = new Size(206, 84);
        Margin = new Padding(0, 0, 14, 0);
        BackColor = Color.White;
        DoubleBuffered = true;
        _accent = accent;
        _caption = caption;
        if (clickable) Cursor = Cursors.Hand;
    }

    public void Set(string value, string caption, Color? accent = null)
    {
        _value = value;
        _caption = caption;
        if (accent is { } a) _accent = a;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = SideNavButton.RoundedRect(rect, 12);
        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(Theme.Border);
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        // farbiger Streifen links
        var stripe = new Rectangle(0, Theme.Px(14), Theme.Px(5), Math.Max(4, Height - Theme.Px(28)));
        using var stripePath = SideNavButton.RoundedRect(stripe, 2);
        using var accent = new SolidBrush(_accent);
        g.FillPath(accent, stripePath);

        // Zahl und Beschriftung als Block senkrecht zentriert
        var block = ValueFont.Height + Theme.Px(2) + Theme.Body.Height;
        var top = Math.Max(Theme.Px(6), (Height - block) / 2);
        var x = Theme.Px(20);
        var width = Math.Max(10, Width - x - Theme.Px(8));
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, _value, ValueFont, new Rectangle(x, top, width, ValueFont.Height), Color.FromArgb(0x17, 0x19, 0x23), flags);
        TextRenderer.DrawText(g, _caption, Theme.Body, new Rectangle(x, top + ValueFont.Height + Theme.Px(2), width, Theme.Body.Height), Theme.Muted, flags);
    }
}
