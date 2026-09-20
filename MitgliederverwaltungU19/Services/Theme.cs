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
    public static readonly Color GreenLight = Color.FromArgb(0x34, 0xD3, 0x99);
    public static readonly Color AccentLight = Color.FromArgb(0xFD, 0xBA, 0x74);
    public static readonly Color DangerLight = Color.FromArgb(0xF8, 0x71, 0x71);
    public static readonly Color SidebarText = Color.FromArgb(0xC7, 0xCB, 0xE0);

    /// <summary>
    /// Anpassung an den Bildschirm: Auf großen Bildschirmen mit kleiner Windows-Skalierung (z. B. 4K bei 100 %)
    /// werden Schrift und Fenster vergrößert (1,0 bis 2,0). Bei normaler Skalierung bleibt es 1,0.
    /// </summary>
    public static readonly float Zoom = ComputeZoom();

    /// <summary>Pixelwert an den Zoom anpassen (für selbst gezeichnete Elemente).</summary>
    public static int Px(int value) => (int)Math.Round(value * Zoom);

    private static float ComputeZoom()
    {
        try
        {
            // Manuell einstellbar (z. B. U19_ZOOM=1.5), sonst automatisch nach Bildschirmgröße
            if (float.TryParse(Environment.GetEnvironmentVariable("U19_ZOOM"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var manual) && manual is >= 0.75f and <= 3f)
            {
                return manual;
            }
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            var dpiScale = Math.Max(1f, g.DpiY / 96f);
            var zoom = area.Height / 1040f / dpiScale;
            return zoom < 1.2f ? 1f : Math.Min(2f, (float)Math.Round(zoom * 4) / 4f);
        }
        catch
        {
            return 1f;
        }
    }

    public static readonly Font Body = new(FontFamily(), 10f * Zoom);
    public static readonly Font Bold = new(FontFamily(), 10f * Zoom, FontStyle.Bold);
    public static readonly Font Title = new(FontFamily(), 15f * Zoom, FontStyle.Bold);

    /// <summary>Programm-Icon (aus der .exe), damit alle Fenster in Titelleiste und Taskleiste es zeigen.</summary>
    public static readonly Icon? AppIcon = LoadAppIcon();

    private static Icon? LoadAppIcon()
    {
        try
        {
            return Environment.ProcessPath is { } path ? Icon.ExtractAssociatedIcon(path) : null;
        }
        catch
        {
            return null;
        }
    }

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


    /// <summary>
    /// Einheitliche Vorbereitung jedes Fensters: Schrift, DPI-Skalierung und Anpassung an die Bildschirmgröße
    /// (vergrößert bei Bedarf und begrenzt das Fenster auf den sichtbaren Bereich).
    /// </summary>
    public static void Prepare(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96f, 96f);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Font = Body;
        form.Load += (_, _) =>
        {
            if (Zoom > 1f && form.WindowState == FormWindowState.Normal)
            {
                form.Scale(new SizeF(Zoom, Zoom));
            }
            var workArea = Screen.FromControl(form.Owner ?? form).WorkingArea;
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, (int)(workArea.Width * 0.96)), Math.Min(form.MinimumSize.Height, (int)(workArea.Height * 0.96)));
            FitContent(form);
            KeepFitted(form);
            FitToScreen(form);
        };
    }

    /// <summary>
    /// Passt feste Dialoge an ihren Inhalt an: Das Fenster wird so groß, dass alles ohne Scrollen sichtbar ist
    /// (höchstens so groß wie der Bildschirm). Ist der Bildschirm zu klein, bleibt die Bildlaufleiste als Rückfall.
    /// </summary>
    [ThreadStatic] private static bool _fitting;

    private static void FitContent(Form form, bool growOnly = false)
    {
        _fitting = true; // die eigene Anpassung löst keine weitere aus
        try
        {
            FitContentCore(form, growOnly);
        }
        finally
        {
            _fitting = false;
        }
    }

    private static void FitContentCore(Form form, bool growOnly)
    {
        if (form.FormBorderStyle is not (FormBorderStyle.FixedDialog or FormBorderStyle.FixedSingle or FormBorderStyle.FixedToolWindow)) return;
        if (form.WindowState != FormWindowState.Normal) return;

        form.PerformLayout();
        var right = 0;
        var bottom = 0;
        void Walk(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                if (c.Controls.Count > 0 && c is not ComboBox && c is not TextBoxBase)
                {
                    Walk(c);
                    continue;
                }
                var r = form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
                bottom = Math.Max(bottom, r.Bottom + c.Margin.Bottom);
                right = Math.Max(right, r.Right + c.Margin.Right);
            }
        }
        Walk(form);
        if (bottom == 0) return;

        var area = Screen.FromControl(form.Owner ?? form).WorkingArea;
        var frameW = form.Width - form.ClientSize.Width;
        var frameH = form.Height - form.ClientSize.Height;
        var pad = Px(20);
        var width = Math.Min(Math.Max(form.ClientSize.Width, right + pad), (int)(area.Width * 0.96) - frameW);
        var height = Math.Min(bottom + pad, (int)(area.Height * 0.96) - frameH);
        height = Math.Max(height, Px(160));
        if (growOnly) height = Math.Max(height, form.ClientSize.Height);
        var target = new Size(width, height);
        if (target != form.ClientSize) form.ClientSize = target;
    }

    /// <summary>Feste Dialoge wachsen mit, wenn später Inhalt erscheint (Fortschritt, Fehlermeldung, Ergebnisliste).</summary>
    private static void KeepFitted(Form form)
    {
        if (form.FormBorderStyle is not (FormBorderStyle.FixedDialog or FormBorderStyle.FixedSingle or FormBorderStyle.FixedToolWindow)) return;
        var pending = false;
        var refits = 0; // Schutz gegen Schwingen: höchstens 6 nachträgliche Anpassungen pro Dialog
        form.Layout += (_, _) =>
        {
            if (_fitting || pending || refits >= 6 || !form.IsHandleCreated) return;
            pending = true;
            form.BeginInvoke(() =>
            {
                pending = false;
                if (form.IsDisposed) return;
                var before = form.ClientSize;
                FitContent(form, growOnly: true);
                if (form.ClientSize != before)
                {
                    refits++;
                    FitToScreen(form);
                }
            });
        };
    }

    private static void FitToScreen(Form form)
    {
        if (form.WindowState != FormWindowState.Normal) return;
        var area = Screen.FromControl(form.Owner ?? form).WorkingArea;
        var w = Math.Min(form.Width, (int)(area.Width * 0.96));
        var h = Math.Min(form.Height, (int)(area.Height * 0.96));
        if (w != form.Width || h != form.Height) form.Size = new Size(w, h);
        if (form.StartPosition is FormStartPosition.CenterScreen or FormStartPosition.CenterParent)
        {
            var bounds = form.Owner is { WindowState: FormWindowState.Normal } owner ? owner.Bounds : area;
            var x = bounds.Left + (bounds.Width - form.Width) / 2;
            var y = bounds.Top + (bounds.Height - form.Height) / 2;
            form.Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - form.Width)), Math.Max(area.Top, Math.Min(y, area.Bottom - form.Height)));
        }
        else
        {
            var x = Math.Max(area.Left, Math.Min(form.Left, area.Right - form.Width));
            var y = Math.Max(area.Top, Math.Min(form.Top, area.Bottom - form.Height));
            form.Location = new Point(x, y);
        }
    }


    /// <summary>Tabellenspalte ganz hinten: pro Zeile ein Button „Link senden“ (öffnet den persönlichen Link mit E-Mail-Versand).</summary>
    public static DataGridViewButtonColumn LinkColumn()
    {
        var col = new DataGridViewButtonColumn
        {
            Name = "link",
            HeaderText = "",
            Text = "Link senden",
            UseColumnTextForButtonValue = true,
            FlatStyle = FlatStyle.Flat,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = Px(120),
            MinimumWidth = Px(120),
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        col.DefaultCellStyle.BackColor = AccentSoft;
        col.DefaultCellStyle.ForeColor = AccentDark;
        col.DefaultCellStyle.SelectionBackColor = AccentSoft;
        col.DefaultCellStyle.SelectionForeColor = AccentDark;
        col.DefaultCellStyle.Font = Bold;
        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        return col;
    }

    public static void ShowError(IWin32Window owner, Exception ex) =>
        MessageBox.Show(owner, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
