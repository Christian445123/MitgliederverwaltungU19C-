using System.Text.Json.Nodes;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>
/// Formular zum Anlegen/Bearbeiten einer Person im Staff, wie bei den Spielern in Register unterteilt:
/// Stammdaten, Kontakt &amp; Adresse, Sozialversicherung (mit E-Card), Reisepass (mit Foto), Dokumente, Ausrüstung &amp; Essen.
/// </summary>
internal sealed class StaffEditDialog : Form
{
    private readonly ApiClient _api;
    private readonly int? _id;
    private readonly Dictionary<string, Control> _inputs = new();
    private readonly ComboBox _status = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };

    public JsonObject Payload { get; private set; } = new();

    /// <summary>Register: Titel, Felder (Schlüssel) und freiwillige Dokumente (Typ, Beschriftung).</summary>
    private static readonly (string Title, string[] Keys, (string Type, string Caption)[] Docs)[] Tabs =
    {
        ("Stammdaten", new[] { "nachname", "vorname", "position", "nada", "geburtsdatum" }, Array.Empty<(string, string)>()),
        ("Kontakt & Adresse", new[] { "telefon", "email", "telefon_angehoeriger", "plz", "ort", "strasse" }, Array.Empty<(string, string)>()),
        ("Sozialversicherung", new[] { "sozialversicherungsnummer" }, new[] { ("ecard", "E-Card (freiwillig)") }),
        ("Reisepass", new[] { "reisepass_nr", "reisepass_ausgestellt_am", "reisepass_gueltig_bis", "geburtsland", "ausstellungsbehoerde" }, new[] { ("pass", "Reisepass (Foto, freiwillig)") }),
        ("Dokumente", Array.Empty<string>(), new[] { ("rechte", "Rechte & Pflichten (unterschrieben, freiwillig)") }),
        ("Ausrüstung & Essen", new[] { "essen", "tshirt_polo_groesse", "hoodie_groesse", "jacken_groesse", "short_groesse", "shorts_anzahl", "coaching_hosen_lang_groesse" }, Array.Empty<(string, string)>()),
    };

    /// <summary>Neu gewählte Dateien je Dokumenttyp (werden nach dem Speichern hochgeladen).</summary>
    public Dictionary<string, string> DocFiles { get; } = new();

    /// <summary>Dokumenttypen, deren vorhandene Datei beim Speichern entfernt wird.</summary>
    public HashSet<string> DocRemove { get; } = new();

    public StaffEditDialog(ApiClient api, Dictionary<string, string?>? row)
    {
        _api = api;
        _id = row is not null && int.TryParse(row.TryGetValue("id", out var idText) ? idText : null, out var idValue) ? idValue : null;

        Text = row is null ? "Neue Person im Staff" : "Staff bearbeiten";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        ClientSize = new Size(720, 560);
        MinimumSize = new Size(700, 480);

        var fields = StaffPanel.StaffFields.ToDictionary(f => f.Key);
        var tabControl = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };

        foreach (var (title, keys, docs) in Tabs)
        {
            var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(14) };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            foreach (var key in keys)
            {
                var (_, label, isDate, multiline, required) = fields[key];
                AddField(table, row, key, label, isDate, multiline, required);
            }

            if (title == "Stammdaten")
            {
                _status.Items.AddRange(new object[] { "aktiv", "inaktiv" });
                _status.SelectedItem = row is not null && row.TryGetValue("status", out var st) && st == "inaktiv" ? "inaktiv" : "aktiv";
                table.Controls.Add(new Label { UseMnemonic = false, Text = "Status", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
                table.Controls.Add(_status);
            }

            foreach (var (type, caption) in docs)
            {
                AddDocumentRow(table, row, type, caption);
            }

            if (title == "Dokumente")
            {
                table.Controls.Add(new Label
                {
                    UseMnemonic = false,
                    Text = "Alle Dokumente beim Staff sind freiwillig (PDF, JPG oder PNG). Wer sie hat, kann sie hier hochladen.",
                    AutoSize = true,
                    MaximumSize = new Size(500, 0),
                    ForeColor = Theme.Muted,
                    Margin = new Padding(0, 16, 0, 0),
                });
                table.SetColumnSpan(table.Controls[^1], 2);
            }

            page.Controls.Add(table);
            tabControl.TabPages.Add(page);
        }

        var ok = Theme.MakeButton("Speichern", primary: true);
        var cancel = Theme.MakeButton("Abbrechen");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 0) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        Controls.Add(tabControl);
        Controls.Add(bar);
        AcceptButton = null;
        CancelButton = cancel;
        cancel.DialogResult = DialogResult.Cancel;

        ok.Click += (_, _) =>
        {
            var payload = new JsonObject();
            foreach (var (key, _, _, _, required) in StaffPanel.StaffFields)
            {
                string? text = _inputs[key] switch
                {
                    DateTimePicker p => p.Checked ? p.Value.ToString("yyyy-MM-dd") : null,
                    ComboBox c when key == "nada" => c.SelectedIndex == 1 ? "1" : "0",
                    TextBox t => string.IsNullOrWhiteSpace(t.Text) ? null : t.Text.Trim(),
                    _ => null,
                };
                if (required && text is null)
                {
                    tabControl.SelectedIndex = 0;
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

    private void AddField(TableLayoutPanel table, Dictionary<string, string?>? row, string key, string label, bool isDate, bool multiline, bool required)
    {
        var current = row is not null && row.TryGetValue(key, out var v) ? v ?? "" : "";
        table.Controls.Add(new Label { UseMnemonic = false, Text = label + (required ? " *" : ""), AutoSize = true, MaximumSize = new Size(190, 0), Margin = new Padding(0, 8, 8, 0) });
        Control input;
        if (key == "nada")
        {
            var yesNo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            yesNo.Items.AddRange(new object[] { "Nein", "Ja" });
            yesNo.SelectedIndex = current.Equals("true", StringComparison.OrdinalIgnoreCase) || current is "1" || current.Equals("ja", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            input = yesNo;
        }
        else if (isDate)
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

        if (key == "vorname")
        {
            // Automatisch gebildet: zuerst Nachname, dann Vorname (z. B. "Schubert Hans")
            var auto = new TextBox { ReadOnly = true, Width = 300, TabStop = false, BackColor = Color.FromArgb(0xF3, 0xF4, 0xF8) };
            var lastName = (TextBox)_inputs["nachname"];
            var firstName = (TextBox)input;
            void UpdateName() => auto.Text = $"{lastName.Text.Trim()} {firstName.Text.Trim()}".Trim();
            lastName.TextChanged += (_, _) => UpdateName();
            firstName.TextChanged += (_, _) => UpdateName();
            UpdateName();
            table.Controls.Add(new Label { UseMnemonic = false, Text = "Name & Vorname (automatisch)", AutoSize = true, MaximumSize = new Size(190, 0), Margin = new Padding(0, 8, 8, 0) });
            table.Controls.Add(auto);
        }
    }

    /// <summary>Zeile für ein freiwilliges Dokument: Status, Öffnen, Hochladen, Entfernen.</summary>
    private void AddDocumentRow(TableLayoutPanel table, Dictionary<string, string?>? row, string docType, string caption)
    {
        var present0 = row is not null && row.TryGetValue("dokument_" + docType, out var flag) && flag == "true";
        var status = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var open = Theme.MakeButton("Öffnen");
        var upload = Theme.MakeButton("Hochladen …");
        var remove = Theme.MakeButton("Entfernen");
        var docBox = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 360, Margin = new Padding(0, 6, 0, 0) };
        docBox.Controls.Add(status);
        docBox.SetFlowBreak(status, true);
        docBox.Controls.AddRange(new Control[] { open, upload, remove });
        table.Controls.Add(new Label { UseMnemonic = false, Text = caption, AutoSize = true, MaximumSize = new Size(190, 0), Margin = new Padding(0, 14, 8, 0) });
        table.Controls.Add(docBox);

        void Refresh()
        {
            var removed = DocRemove.Contains(docType);
            var newFile = DocFiles.TryGetValue(docType, out var f) ? f : null;
            var present = present0 && !removed;
            status.Text = newFile is not null ? "Neue Datei: " + Path.GetFileName(newFile) + " (wird beim Speichern hochgeladen)"
                : removed ? "wird beim Speichern entfernt"
                : present ? "✓ vorhanden" : "nicht hochgeladen (freiwillig)";
            status.ForeColor = present || newFile is not null ? Theme.Green : Theme.Muted;
            open.Enabled = present && _id is not null && newFile is null;
            remove.Enabled = present && newFile is null;
        }

        open.Click += async (_, _) =>
        {
            if (_id is null) return;
            try
            {
                UseWaitCursor = true;
                var (data, ext) = await _api.DownloadDocumentAsync(_id.Value, docType, kind: "staff");
                var dir = Path.Combine(Path.GetTempPath(), "U19Dokumente");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"staff-{docType}-{_id}{ext}");
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
            DocFiles[docType] = dialog.FileName;
            DocRemove.Remove(docType);
            Refresh();
        };
        remove.Click += (_, _) =>
        {
            DocRemove.Add(docType);
            DocFiles.Remove(docType);
            Refresh();
        };
        Refresh();
    }
}
