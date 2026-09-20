using System.Text.Json.Nodes;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Formular zum Anlegen/Bearbeiten einer Person im Staff.</summary>
internal sealed class StaffEditDialog : Form
{
    private readonly ApiClient _api;
    private readonly int? _id;
    private readonly Dictionary<string, Control> _inputs = new();
    private readonly ComboBox _status = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };

    public JsonObject Payload { get; private set; } = new();

    /// <summary>Freiwillige Dokumente: Schlüssel (API) und Beschriftung.</summary>
    private static readonly (string Type, string Caption)[] DocumentTypes =
    {
        ("rechte", "Rechte & Pflichten (unterschrieben, freiwillig)"),
        ("pass", "Foto Reisepass Vorderseite (freiwillig)"),
        ("pass_back", "Foto Reisepass Rückseite (freiwillig)"),
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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(600, 680);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(16) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        foreach (var (key, label, isDate, multiline, required) in StaffPanel.StaffFields)
        {
            var current = row is not null && row.TryGetValue(key, out var v) ? v ?? "" : "";
            table.Controls.Add(new Label { Text = label + (required ? " *" : ""), AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
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
                table.Controls.Add(new Label { Text = "Name & Vorname (automatisch)", AutoSize = true, MaximumSize = new Size(190, 0), Margin = new Padding(0, 8, 8, 0) });
                table.Controls.Add(auto);
            }
        }

        _status.Items.AddRange(new object[] { "aktiv", "inaktiv" });
        _status.SelectedItem = row is not null && row.TryGetValue("status", out var st) && st == "inaktiv" ? "inaktiv" : "aktiv";
        table.Controls.Add(new Label { Text = "Status", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        table.Controls.Add(_status);

        // Freiwillige Dokumente: Rechte & Pflichten (unterschrieben) sowie Foto des Reisepasses (Vorder-/Rückseite)
        foreach (var (type, caption) in DocumentTypes)
        {
            var present0 = row is not null && row.TryGetValue("dokument_" + type, out var flag) && flag == "true";
            var status = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            var open = Theme.MakeButton("Öffnen");
            var upload = Theme.MakeButton("Hochladen …");
            var remove = Theme.MakeButton("Entfernen");
            var docBox = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 360, Margin = new Padding(0, 6, 0, 0) };
            docBox.Controls.Add(status);
            docBox.SetFlowBreak(status, true);
            docBox.Controls.AddRange(new Control[] { open, upload, remove });
            table.Controls.Add(new Label { Text = caption, AutoSize = true, MaximumSize = new Size(190, 0), Margin = new Padding(0, 14, 8, 0) });
            table.Controls.Add(docBox);

            var docType = type;
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
            foreach (var (key, _, isDate, _, required) in StaffPanel.StaffFields)
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
