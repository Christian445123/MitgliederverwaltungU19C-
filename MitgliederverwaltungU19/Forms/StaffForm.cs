using System.Text.Json.Nodes;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Staff (Coaches/Betreuer): Liste mit Suche sowie Anlegen, Bearbeiten und Löschen.</summary>
public sealed class StaffForm : Form
{
    /// <summary>Felder des Staffs (Schlüssel wie in der API, siehe includes/staff.php).</summary>
    internal static readonly (string Key, string Label, bool Date, bool Multiline, bool Required)[] StaffFields =
    {
        ("nachname", "Nachname", false, false, true),
        ("vorname", "Vorname", false, false, true),
        ("position", "Position (z. B. HC, OC, DC, TM)", false, false, false),
        ("nada", "Nada", false, false, false),
        ("nada_gueltig_bis", "Nada gültig bis", true, false, false),
        ("geburtsdatum", "Geburtsdatum", true, false, false),
        ("telefon", "Telefon", false, false, false),
        ("email", "Mail", false, false, false),
        ("telefon_angehoeriger", "Telefon Angehöriger", false, false, false),
        ("reisepass_nr", "Reisepass Nr", false, false, false),
        ("reisepass_ausgestellt_am", "Reisepass ausgestellt am", true, false, false),
        ("reisepass_gueltig_bis", "Reisepass gültig bis", true, false, false),
        ("geburtsland", "Geburtsland", false, false, false),
        ("ausstellungsbehoerde", "Ausstellungsbehörde", false, false, false),
        ("plz", "PLZ", false, false, false),
        ("ort", "Ort", false, false, false),
        ("strasse", "Straße", false, false, false),
        ("essen", "Essen", false, true, false),
        ("tshirt_polo_groesse", "T-Shirt / Polo Größe", false, false, false),
        ("hoodie_groesse", "Hoodie Größe", false, false, false),
        ("jacken_groesse", "Jacken Größe", false, false, false),
        ("short_groesse", "Short Größe", false, false, false),
        ("shorts_anzahl", "Wie viele Shorts besitzt du?", false, false, false),
        ("coaching_hosen_lang_groesse", "Coaching Hosen (lang) Größe", false, false, false),
    };

    private readonly ApiClient _api;
    private readonly bool _canWrite;
    private readonly TextBox _search = new() { Width = 260, PlaceholderText = "Suche: Name, Position, E-Mail" };
    private readonly DataGridView _grid = new();
    private readonly Label _info = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 8, 0, 0) };
    private List<Dictionary<string, string?>> _all = new();

    public StaffForm(ApiClient api, bool canWrite)
    {
        _api = api;
        _canWrite = canWrite;
        Text = "Staff";
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 600);
        MinimumSize = new Size(760, 420);

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 10, 12, 0), BackColor = Color.White };
        var refresh = Theme.MakeButton("Aktualisieren");
        var add = Theme.MakeButton("+ Neue Person", primary: true);
        var edit = Theme.MakeButton("Bearbeiten");
        var delete = Theme.MakeButton("Auswahl löschen");
        var export = Theme.MakeButton("Export CSV");
        export.Click += async (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "CSV (Excel)|*.csv", FileName = $"staff-{DateTime.Now:yyyy-MM-dd}.csv" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                await File.WriteAllBytesAsync(dialog.FileName, await _api.DownloadCsvAsync(null, template: false, staff: true));
            }
            catch (Exception ex)
            {
                Theme.ShowError(this, ex);
            }
        };
        _search.Margin = new Padding(0, 2, 8, 0);
        tools.Controls.AddRange(new Control[] { _search, refresh, add, edit, delete, export, _info });
        if (!canWrite)
        {
            add.Enabled = edit.Enabled = delete.Enabled = false;
        }

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Theme.StyleGrid(_grid);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        _grid.AutoGenerateColumns = false;
        foreach (var (key, label) in new[] { ("nachname", "Nachname"), ("vorname", "Vorname"), ("position", "Position"), ("nada", "Nada"), ("nada_gueltig_bis", "Nada gültig bis"), ("geburtsdatum", "Geburtsdatum"), ("telefon", "Telefon"), ("email", "Mail"), ("reisepass_gueltig_bis", "Reisepass gültig bis"), ("status", "Status") })
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = key, HeaderText = label });
        }

        Controls.Add(_grid);
        Controls.Add(tools);

        refresh.Click += async (_, _) => await ReloadAsync();
        add.Click += async (_, _) => await OpenEditorAsync(null);
        edit.Click += async (_, _) => { if (Selected().FirstOrDefault() is { } row) await OpenEditorAsync(row); };
        delete.Click += async (_, _) => await DeleteAsync();
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (canWrite && e.RowIndex >= 0 && Selected().FirstOrDefault() is { } row) await OpenEditorAsync(row);
        };
        _search.TextChanged += (_, _) => ApplyFilter();
        Shown += async (_, _) => await ReloadAsync();
    }

    private IEnumerable<Dictionary<string, string?>> Selected() =>
        _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (Dictionary<string, string?>)r.Tag!);

    private async Task ReloadAsync()
    {
        try
        {
            _info.Text = "Lade …";
            _all = await _api.ListStaffAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _info.Text = "";
            Theme.ShowError(this, ex);
        }
    }

    private static string Val(Dictionary<string, string?> row, string key) => row.TryGetValue(key, out var v) ? v ?? "" : "";

    /// <summary>Gleiche Farben wie bei den Spielern: gelb / blau / orange / rot.</summary>
    private static void MarkExpiry(DataGridViewCell cell, (ExpiryState State, int Days, DateTime? Date) e)
    {
        if (e.State == ExpiryState.Ok || e.Date is null) return;
        var (back, fore) = Expiry.Colors(e.State);
        cell.Style.BackColor = back;
        cell.Style.ForeColor = fore;
        cell.ToolTipText = new ExpiryItem(new Models.Member(), "", e.Date.Value, e.State, e.Days).Describe();
    }

    private void ApplyFilter()
    {
        var q = _search.Text.Trim();
        var rows = _all.Where(r => q.Length == 0
            || new[] { "nachname", "vorname", "position", "email", "telefon" }.Any(k => Val(r, k).Contains(q, StringComparison.OrdinalIgnoreCase)));
        _grid.Rows.Clear();
        var count = 0;
        foreach (var r in rows)
        {
            var i = _grid.Rows.Add(_grid.Columns.Cast<DataGridViewColumn>().Select(c => (object)Val(r, c.Name)).ToArray());
            _grid.Rows[i].Tag = r;
            MarkExpiry(_grid.Rows[i].Cells["nada_gueltig_bis"], Expiry.Check(Val(r, "nada_gueltig_bis"), DateTime.Today.AddMonths(Expiry.NadaWarnMonths), DateTime.Today, Expiry.NadaUrgentDays, redOnDay: true));
            MarkExpiry(_grid.Rows[i].Cells["reisepass_gueltig_bis"], Expiry.Check(Val(r, "reisepass_gueltig_bis"), DateTime.Today.AddMonths(Expiry.PassWarnMonths), DateTime.Today));
            count++;
        }
        _info.Text = $"{count} von {_all.Count} Personen";
    }

    private async Task OpenEditorAsync(Dictionary<string, string?>? row)
    {
        using var dialog = new StaffEditDialog(_api, row);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            int? id = row is not null && int.TryParse(Val(row, "id"), out var n) ? n : null;
            var savedId = await _api.SaveStaffAsync(id, dialog.Payload);
            if (dialog.RemoveRechte && id is not null)
            {
                await _api.DeleteDocumentAsync(savedId, "rechte", kind: "staff");
            }
            if (dialog.RechteFile is { } file)
            {
                await _api.UploadDocumentAsync(savedId, "rechte", file, kind: "staff");
            }
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private async Task DeleteAsync()
    {
        var rows = Selected().ToList();
        if (rows.Count == 0) return;
        if (MessageBox.Show(this, $"{rows.Count} Person(en) wirklich löschen?", "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            foreach (var r in rows)
            {
                if (int.TryParse(Val(r, "id"), out var id)) await _api.DeleteStaffAsync(id);
            }
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }
}

/// <summary>Formular zum Anlegen/Bearbeiten einer Person im Staff.</summary>
internal sealed class StaffEditDialog : Form
{
    private readonly ApiClient _api;
    private readonly int? _id;
    private readonly Dictionary<string, Control> _inputs = new();
    private readonly ComboBox _status = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly Label _rechteStatus = new() { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
    private readonly bool _rechteVorhanden;

    public JsonObject Payload { get; private set; } = new();

    /// <summary>Neu gewählte Datei für "Rechte und Pflichten" (wird nach dem Speichern hochgeladen).</summary>
    public string? RechteFile { get; private set; }

    /// <summary>Vorhandenes Dokument beim Speichern entfernen.</summary>
    public bool RemoveRechte { get; private set; }

    public StaffEditDialog(ApiClient api, Dictionary<string, string?>? row)
    {
        _api = api;
        _id = row is not null && int.TryParse(row.TryGetValue("id", out var idText) ? idText : null, out var idValue) ? idValue : null;
        _rechteVorhanden = row is not null && row.TryGetValue("dokument_rechte", out var dr) && dr == "true";

        Text = row is null ? "Neue Person im Staff" : "Staff bearbeiten";
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(600, 680);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(16) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        foreach (var (key, label, isDate, multiline, required) in StaffForm.StaffFields)
        {
            var current = row is not null && row.TryGetValue(key, out var v) ? v ?? "" : "";
            table.Controls.Add(new Label { Text = label + (required ? " *" : ""), AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            Control input;
            if (isDate)
            {
                var picker = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Width = 160 };
                if (DateTime.TryParse(current, out var d)) picker.Value = d; else picker.Checked = false;
                input = picker;
            }
            else
            {
                input = new TextBox { Text = current, Width = 300, Multiline = multiline, Height = multiline ? 60 : 24, MaxLength = 500 };
            }
            _inputs[key] = input;
            table.Controls.Add(input);
        }

        _status.Items.AddRange(new object[] { "aktiv", "inaktiv" });
        _status.SelectedItem = row is not null && row.TryGetValue("status", out var st) && st == "inaktiv" ? "inaktiv" : "aktiv";
        table.Controls.Add(new Label { Text = "Status", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        table.Controls.Add(_status);

        // Rechte und Pflichten (unterschriebenes Dokument)
        var open = Theme.MakeButton("Öffnen");
        var upload = Theme.MakeButton("Hochladen …");
        var remove = Theme.MakeButton("Entfernen");
        var docBox = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 360, Margin = new Padding(0, 6, 0, 0) };
        docBox.Controls.Add(_rechteStatus);
        docBox.SetFlowBreak(_rechteStatus, true);
        docBox.Controls.AddRange(new Control[] { open, upload, remove });
        table.Controls.Add(new Label { Text = "Rechte & Pflichten (unterschrieben)", AutoSize = true, MaximumSize = new Size(190, 0), Margin = new Padding(0, 14, 8, 0) });
        table.Controls.Add(docBox);

        void RefreshDoc()
        {
            var present = _rechteVorhanden && !RemoveRechte;
            _rechteStatus.Text = RechteFile is not null ? "Neue Datei: " + Path.GetFileName(RechteFile) + " (wird beim Speichern hochgeladen)"
                : RemoveRechte ? "wird beim Speichern entfernt"
                : present ? "✓ vorhanden" : "fehlt";
            _rechteStatus.ForeColor = present || RechteFile is not null ? Theme.Green : Theme.Muted;
            open.Enabled = present && _id is not null && RechteFile is null;
            remove.Enabled = present && RechteFile is null;
        }

        open.Click += async (_, _) =>
        {
            if (_id is null) return;
            try
            {
                UseWaitCursor = true;
                var (data, ext) = await _api.DownloadDocumentAsync(_id.Value, "rechte", kind: "staff");
                var dir = Path.Combine(Path.GetTempPath(), "U19Dokumente");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"staff-rechte-{_id}{ext}");
                await File.WriteAllBytesAsync(path, data);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Theme.ShowError(this, ex);
            }
            finally
            {
                UseWaitCursor = false;
            }
        };
        upload.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Dokumente|*.pdf;*.jpg;*.jpeg;*.png|Alle Dateien|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            RechteFile = dialog.FileName;
            RemoveRechte = false;
            RefreshDoc();
        };
        remove.Click += (_, _) =>
        {
            RemoveRechte = true;
            RechteFile = null;
            RefreshDoc();
        };
        RefreshDoc();

        var ok = Theme.MakeButton("Speichern", primary: true);
        var cancel = Theme.MakeButton("Abbrechen");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 0) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        Controls.Add(table);
        Controls.Add(bar);
        AcceptButton = null;
        CancelButton = cancel;
        cancel.DialogResult = DialogResult.Cancel;

        ok.Click += (_, _) =>
        {
            var payload = new JsonObject();
            foreach (var (key, _, isDate, _, required) in StaffForm.StaffFields)
            {
                string? text = _inputs[key] switch
                {
                    DateTimePicker p => p.Checked ? p.Value.ToString("yyyy-MM-dd") : null,
                    TextBox t => string.IsNullOrWhiteSpace(t.Text) ? null : t.Text.Trim(),
                    _ => null,
                };
                if (required && text is null)
                {
                    MessageBox.Show(this, "Bitte alle mit * markierten Felder ausfüllen.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                payload[key] = text;
            }
            payload["status"] = (string?)_status.SelectedItem ?? "aktiv";
            Payload = payload;
            DialogResult = DialogResult.OK;
        };
    }
}
