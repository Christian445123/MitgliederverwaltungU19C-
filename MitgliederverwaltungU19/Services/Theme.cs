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

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);

    public static Button MakeButton(string text, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 3, 10, 3),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = primary ? Bold : Body,
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.FromArgb(0x1A, 0x0D, 0x00) : Color.FromArgb(0x17, 0x19, 0x23),
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentDark : AccentSoft;
        return b;
    }

    public static void ShowError(IWin32Window owner, Exception ex) =>
        MessageBox.Show(owner, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
