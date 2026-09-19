namespace MitgliederverwaltungU19.Services;

/// <summary>Farben/Stile passend zum Web-Design (dunkles Navy + orangener Akzent).</summary>
public static class Theme
{
    public static readonly Color Navy = Color.FromArgb(0x11, 0x15, 0x2A);
    public static readonly Color NavyLight = Color.FromArgb(0x1A, 0x20, 0x40);
    public static readonly Color Accent = Color.FromArgb(0xF9, 0x73, 0x16);
    public static readonly Color AccentDark = Color.FromArgb(0xEA, 0x58, 0x0C);
    public static readonly Color AccentSoft = Color.FromArgb(0xFF, 0xF2, 0xE6);
    public static readonly Color Background = Color.FromArgb(0xF3, 0xF4, 0xF8);
    public static readonly Color Border = Color.FromArgb(0xE3, 0xE5, 0xEC);
    public static readonly Color Muted = Color.FromArgb(0x6B, 0x72, 0x80);
    public static readonly Color Green = Color.FromArgb(0x06, 0x5F, 0x46);
    public static readonly Color Danger = Color.FromArgb(0xDC, 0x26, 0x26);

    public static readonly Font Body = new(FontFamily(), 10f);
    public static readonly Font Bold = new(FontFamily(), 10f, FontStyle.Bold);
    public static readonly Font Title = new(FontFamily(), 15f, FontStyle.Bold);

    /// <summary>Moderne Windows-Schrift, falls vorhanden (Windows 11), sonst Segoe UI.</summary>
    private static string FontFamily()
    {
        using var f = new Font("Segoe UI Variable Text", 10f);
        return f.Name == "Segoe UI Variable Text" ? f.Name : "Segoe UI";
    }

    private static readonly Color Ink = Color.FromArgb(0x17, 0x19, 0x23);
    private static readonly Color RowAlt = Color.FromArgb(0xFA, 0xFB, 0xFD);

    /// <summary>Abgerundeter Umriss für Buttons.</summary>
    private static void Round(Control c, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var r = Math.Min(radius * 2, Math.Min(c.Width, c.Height));
        if (r <= 0) return;
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(c.Width - r, 0, r, r, 270, 90);
        path.AddArc(c.Width - r, c.Height - r, r, r, 0, 90);
        path.AddArc(0, c.Height - r, r, r, 90, 90);
        path.CloseFigure();
        c.Region = new Region(path);
    }

    public static Button MakeButton(string text, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 34),
            Padding = new Padding(14, 4, 14, 4),
            Margin = new Padding(0, 0, 8, 6),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = primary ? Bold : Body,
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.FromArgb(0x1A, 0x0D, 0x00) : Ink,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentDark : AccentSoft;
        b.FlatAppearance.MouseDownBackColor = primary ? AccentDark : Color.FromArgb(0xFF, 0xE4, 0xCC);
        b.SizeChanged += (_, _) => Round(b, 8);
        b.EnabledChanged += (_, _) => b.ForeColor = b.Enabled ? (primary ? Color.FromArgb(0x1A, 0x0D, 0x00) : Ink) : Color.FromArgb(0x9C, 0xA3, 0xAF);
        return b;
    }

    /// <summary>Einheitlich moderne Tabelle: helle Kopfzeile, luftige Zeilen, zarte Trennlinien.</summary>
    public static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Color.White;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.GridColor = Color.FromArgb(0xEE, 0xF0, 0xF5);
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 40;
        g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(0xF3, 0xF4, 0xF8);
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(0x4B, 0x52, 0x63);
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(0xF3, 0xF4, 0xF8);
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(0x4B, 0x52, 0x63);
        g.ColumnHeadersDefaultCellStyle.Font = Bold;
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 4, 0);
        g.DefaultCellStyle.BackColor = Color.White;
        g.DefaultCellStyle.ForeColor = Ink;
        g.DefaultCellStyle.Font = Body;
        g.DefaultCellStyle.Padding = new Padding(8, 0, 4, 0);
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xFF, 0xEC, 0xD9);
        g.DefaultCellStyle.SelectionForeColor = Ink;
        g.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;
        g.AlternatingRowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(0xFF, 0xEC, 0xD9);
        g.AlternatingRowsDefaultCellStyle.SelectionForeColor = Ink;
        g.RowTemplate.Height = 38;
        g.RowHeadersVisible = false;
        g.AllowUserToResizeRows = false;
    }

    public static void ShowError(IWin32Window owner, Exception ex) =>
        MessageBox.Show(owner, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
