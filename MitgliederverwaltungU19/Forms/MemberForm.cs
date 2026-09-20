using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Bearbeiten/Anlegen eines Mitglieds. Die Eingabefelder werden aus Fields.All erzeugt.</summary>
public sealed class MemberForm : Form
{
    private readonly ApiClient _api;
    private readonly Member? _member;
    private readonly Dictionary<string, Control> _controls = new();
    private readonly Label _error = new() { AutoSize = true, ForeColor = Theme.Danger, MaximumSize = new Size(700, 0) };
    private Button _save = null!;
    private readonly bool _readOnly;
    private readonly Dictionary<string, bool> _docs = new();

    public MemberForm(ApiClient api, Member? member, bool readOnly)
    {
        _api = api;
        _member = member;
        _readOnly = readOnly;
        if (member is not null)
        {
            foreach (var kv in member.Documents) _docs[kv.Key] = kv.Value;
        }

        Text = member is null ? "Neues Mitglied" : "Mitglied bearbeiten – " + member.FullName;
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(800, 720);
        MinimumSize = new Size(620, 560);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        foreach (var group in Fields.Groups)
        {
            tabs.TabPages.Add(BuildTab(group));
        }

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 0),
            BackColor = Theme.Background,
        };
        var cancel = Theme.MakeButton(readOnly ? "Schließen" : "Abbrechen");
        _save = Theme.MakeButton("Speichern", primary: true);
        cancel.Margin = new Padding(8, 0, 0, 0);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _save.Click += async (_, _) => await SaveAsync();
        bar.Controls.Add(cancel);
        if (!readOnly) bar.Controls.Add(_save);
        bar.Controls.Add(_error);
        CancelButton = cancel;

        Controls.Add(tabs);
        Controls.Add(bar);

        if (member is not null) FillFrom(member);
        Shown += async (_, _) => await LoadCampsAsync();
        if (readOnly)
        {
            foreach (var c in _controls.Values) c.Enabled = false;
        }
    }

    private TabPage BuildTab(string group)
    {
        var page = new TabPage(group) { BackColor = Color.White, Padding = new Padding(14) };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        foreach (var f in Fields.All.Where(x => x.Group == group))
        {
            Control input = f.Type switch
            {
                FieldType.Bool => new CheckBox { AutoSize = true, Text = "Ja" },
                FieldType.Date => new DateTimePicker { ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Short, Width = 150 },
                FieldType.Status => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Items = { "aktiv", "inaktiv" }, SelectedIndex = 0 },
                FieldType.Kader => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Items = { "Im Kader", "Spieler nicht im Kader" }, SelectedIndex = 0, Tag = "kader" },
                FieldType.Multiline => new TextBox { Multiline = true, Height = 60, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Top, MaxLength = 255 },
                _ => new TextBox { Dock = DockStyle.Top },
            };
            input.Margin = new Padding(0, 4, 0, 4);
            _controls[f.Key] = input;

            var label = new Label
            {
                Text = f.Label + (f.Required ? " *" : ""),
                AutoSize = true,
                Margin = new Padding(0, 9, 8, 0),
                Font = f.Required ? Theme.Bold : Theme.Body,
            };
            table.Controls.Add(label);
            table.Controls.Add(input);
        }
        if (group == Fields.GCamps) _campsTable = table;
        page.Controls.Add(table);
        if (group == Fields.GDoku)
        {
            page.Controls.Add(BuildDocumentsPanel());
        }
        return page;
    }

    // ── Weitere Camps (Ja/Nein je Camp, wie im Web-Formular) ────────────────
    private TableLayoutPanel? _campsTable;
    private readonly Dictionary<int, CheckBox> _campBoxes = new();

    /// <summary>Lädt die zusätzlich angelegten Camps und zeigt je Camp ein Ja/Nein-Feld unter „Camps &amp; Zustimmung“.</summary>
    private async Task LoadCampsAsync()
    {
        if (_campsTable is null) return;
        try
        {
            var camps = await _api.GetCampListAsync();
            foreach (var camp in camps)
            {
                var box = new CheckBox
                {
                    AutoSize = true,
                    Text = "Ja",
                    Margin = new Padding(0, 4, 0, 4),
                    Checked = _member?.ExtraCamps.Contains(camp.Name) == true,
                    Enabled = !_readOnly,
                };
                _campBoxes[camp.Id] = box;
                _campsTable.Controls.Add(new Label { Text = camp.Name, AutoSize = true, Margin = new Padding(0, 9, 8, 0) });
                _campsTable.Controls.Add(box);
            }
        }
        catch (Exception)
        {
            // Server kennt "camps" noch nicht (alte Version): dann gibt es nur die festen Camps
        }
    }

    // ── Dokumente (E-Card, Pass, NADA, Rechte & Pflichten) ─────────────────
    private readonly Dictionary<string, Label> _docStatus = new();
    private readonly Dictionary<string, Button[]> _docButtons = new();

    /// <summary>true, wenn Dokumente hochgeladen/entfernt wurden (Liste muss neu geladen werden).</summary>
    public bool DocumentsChanged { get; private set; }

    private Control BuildDocumentsPanel()
    {
        var box = new GroupBox { Text = "Dokumente (PDF, JPG, PNG – max. 15 MB)", Dock = DockStyle.Bottom, Height = 34 + DocumentTypes.All.Count * 42, Padding = new Padding(10, 6, 10, 6) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = DocumentTypes.All.Count };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < DocumentTypes.All.Count; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        }

        foreach (var (key, label) in DocumentTypes.All)
        {
            var status = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            _docStatus[key] = status;

            var open = Theme.MakeButton("Öffnen");
            var upload = Theme.MakeButton("Hochladen …");
            var remove = Theme.MakeButton("Entfernen");
            var typeKey = key;
            open.Click += async (_, _) => await OpenDocumentAsync(typeKey);
            upload.Click += async (_, _) => await UploadDocumentAsync(typeKey);
            remove.Click += async (_, _) => await RemoveDocumentAsync(typeKey);
            _docButtons[key] = new[] { open, upload, remove };

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            buttons.Controls.AddRange(new Control[] { open, upload, remove });

            // "Fehlt"-Haken für die Pflichtdokumente (nicht für die Rückseiten)
            if (key is "ecard" or "pass" or "nada" or "rechte")
            {
                var missing = new CheckBox
                {
                    Text = "Fehlt",
                    AutoSize = true,
                    ForeColor = Theme.Danger,
                    Margin = new Padding(8, 8, 0, 0),
                    Checked = _member?.MissingFlags.TryGetValue(key, out var flagged) == true && flagged,
                    Enabled = _member is not null && !_readOnly,
                };
                missing.CheckedChanged += async (_, _) => await SetMissingFlagAsync(typeKey, missing);
                buttons.Controls.Add(missing);
            }

            table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            table.Controls.Add(status);
            table.Controls.Add(buttons);
        }
        box.Controls.Add(table);
        RefreshDocumentState();
        return box;
    }

    private async Task SetMissingFlagAsync(string type, CheckBox box)
    {
        if (_member is null) return;
        try
        {
            await _api.SetDocumentFlagAsync(_member.Id, type, box.Checked);
            DocumentsChanged = true;
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private void RefreshDocumentState()
    {
        foreach (var (key, _) in DocumentTypes.All)
        {
            var present = _docs.TryGetValue(key, out var p) && p;
            _docStatus[key].Text = _member is null ? "–" : present ? "✓ vorhanden" : "fehlt";
            _docStatus[key].ForeColor = present ? Theme.Green : Theme.Muted;
            var buttons = _docButtons[key];
            buttons[0].Enabled = _member is not null && present;
            buttons[1].Enabled = _member is not null && !_readOnly;
            buttons[2].Enabled = _member is not null && present && !_readOnly;
        }
    }

    private async Task OpenDocumentAsync(string type)
    {
        if (_member is null) return;
        try
        {
            UseWaitCursor = true;
            var (data, ext) = await _api.DownloadDocumentAsync(_member.Id, type);
            var dir = Path.Combine(Path.GetTempPath(), "U19Dokumente");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{type}-{_member.Id}{ext}");
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
    }

    private async Task UploadDocumentAsync(string type)
    {
        if (_member is null) return;
        using var dialog = new OpenFileDialog { Filter = "Dokumente|*.pdf;*.jpg;*.jpeg;*.png|Alle Dateien|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            await _api.UploadDocumentAsync(_member.Id, type, dialog.FileName);
            _docs[type] = true;
            DocumentsChanged = true;
            RefreshDocumentState();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async Task RemoveDocumentAsync(string type)
    {
        if (_member is null) return;
        if (MessageBox.Show(this, "Dokument wirklich entfernen?", "Entfernen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            UseWaitCursor = true;
            await _api.DeleteDocumentAsync(_member.Id, type);
            _docs[type] = false;
            DocumentsChanged = true;
            RefreshDocumentState();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void FillFrom(Member m)
    {
        foreach (var f in Fields.All)
        {
            var v = m.Get(f.Key);
            switch (_controls[f.Key])
            {
                case CheckBox cb:
                    cb.Checked = v == "true";
                    break;
                case DateTimePicker dtp:
                    if (DateTime.TryParse(v, out var d)) { dtp.Value = d; dtp.Checked = true; }
                    break;
                case ComboBox kaderCombo when kaderCombo.Tag as string == "kader":
                    kaderCombo.SelectedIndex = v == "nicht_im_kader" ? 1 : 0;
                    break;
                case ComboBox combo:
                    combo.SelectedItem = v == "inaktiv" ? "inaktiv" : "aktiv";
                    break;
                default:
                    _controls[f.Key].Text = v;
                    break;
            }
        }
    }

    private Dictionary<string, string?> Collect()
    {
        var values = new Dictionary<string, string?>();
        foreach (var f in Fields.All)
        {
            values[f.Key] = _controls[f.Key] switch
            {
                CheckBox cb => cb.Checked ? "true" : "false",
                DateTimePicker dtp => dtp.Checked ? dtp.Value.ToString("yyyy-MM-dd") : null,
                ComboBox kaderCombo when kaderCombo.Tag as string == "kader" => kaderCombo.SelectedIndex == 1 ? "nicht_im_kader" : "kader",
                ComboBox combo => combo.SelectedItem?.ToString(),
                var c => c.Text,
            };
        }
        return values;
    }

    private async Task SaveAsync()
    {
        var values = Collect();
        foreach (var f in Fields.All.Where(x => x.Required))
        {
            if (string.IsNullOrWhiteSpace(values[f.Key]))
            {
                _error.Text = f.Label + " ist ein Pflichtfeld.";
                return;
            }
        }
        foreach (var f in Fields.All.Where(x => x.Type == FieldType.Int))
        {
            var v = values[f.Key];
            if (!string.IsNullOrWhiteSpace(v) && !v.Trim().All(char.IsDigit))
            {
                _error.Text = f.Label + ": bitte eine ganze Zahl eingeben.";
                return;
            }
        }

        _error.Text = "";
        _save.Enabled = false;
        try
        {
            var payload = Member.ToJson(values);
            foreach (var (campId, box) in _campBoxes)
            {
                payload[$"camp:{campId}"] = box.Checked; // weitere Camps
            }
            await _api.SaveAsync(_member?.Id, payload);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _error.Text = ex.Message.Replace("\n", " ");
        }
        finally
        {
            _save.Enabled = true;
        }
    }
}
