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

    public MemberForm(ApiClient api, Member? member, bool readOnly)
    {
        _api = api;
        _member = member;

        Text = member is null ? "Neues Mitglied" : "Mitglied bearbeiten – " + member.FullName;
        Font = Theme.Body;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 560);
        MinimumSize = new Size(560, 420);

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
        page.Controls.Add(table);
        return page;
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
            await _api.SaveAsync(_member?.Id, Member.ToJson(values));
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
