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
