using System.Diagnostics;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Roster erstellen: alphabetische Spielerliste oder IFAF-Roster, als PDF oder Excel.</summary>
public sealed class RosterForm : Form
{
    private readonly ApiClient _api;
    private readonly RadioButton _alpha = new() { Text = "Alphabetischer Roster (Spieler A–Z)", AutoSize = true, Checked = true };
    private readonly RadioButton _clothing = new() { Text = "Rosterbekleidung (Größen je Spieler)", AutoSize = true };
    private readonly RadioButton _clubs = new() { Text = "Roster Vereine (ID, Name, Verein)", AutoSize = true };
    private readonly RadioButton _missing = new() { Text = "Fehlende Dokumente (Spieler und Staff getrennt)", AutoSize = true };
    private readonly RadioButton _expired = new() { Text = "Abgelaufene Dokumente (Spieler und Staff getrennt)", AutoSize = true };
    private readonly CheckBox _onlyExpired = new() { Text = "Nur bereits abgelaufene (nicht die bald ablaufenden)", AutoSize = true, Visible = false, Location = new Point(0, 64) };
    private readonly RadioButton _ifaf = new() { Text = "IFAF-Roster (Spieler + Staff + Unterschriftszeilen)", AutoSize = true };
    private readonly ComboBox _kader = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly TextBox _competition = new() { Width = 300, Text = "IFAF European Championship 2026/27" };
    private readonly TextBox _game = new() { Width = 300, PlaceholderText = "z. B. Austria v Czechia" };
    private readonly TextBox _team = new() { Width = 300, Text = "Austria" };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(470, 0) };
    private readonly Button _pdf = Theme.MakeButton("PDF erstellen", primary: true);
    private readonly Button _excel = Theme.MakeButton("Excel erstellen");
    private readonly Panel _alphaBox = new() { AutoSize = true };
    private readonly Panel _ifafBox = new() { AutoSize = true };

    public RosterForm(ApiClient api)
    {
        _api = api;
        Text = "Roster erstellen";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(500, 540);

        _kader.Items.AddRange(new object[] { "Nur Spieler im Kader", "Alle Spieler", "Nur Spieler nicht im Kader" });
        _kader.SelectedIndex = 0;

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), AutoScroll = true };
        layout.Controls.Add(_alpha);
        layout.Controls.Add(_clothing);
        layout.Controls.Add(_clubs);
        layout.Controls.Add(_missing);
        layout.Controls.Add(_expired);
        layout.Controls.Add(_ifaf);

        _alphaBox.Controls.Add(new Label { Text = "Welche Spieler?", AutoSize = true, Location = new Point(0, 10) });
        _kader.Location = new Point(0, 32);
        _alphaBox.Controls.Add(_kader);
        _alphaBox.Controls.Add(_onlyExpired);
        _alphaBox.Size = new Size(460, 92);

        var y = 0;
        foreach (var (caption, box) in new (string, TextBox)[] { ("Wettbewerb", _competition), ("Spiel (Game)", _game), ("Team", _team) })
        {
            _ifafBox.Controls.Add(new Label { Text = caption, AutoSize = true, Location = new Point(0, y + 4) });
            box.Location = new Point(0, y + 24);
            _ifafBox.Controls.Add(box);
            y += 56;
        }
        _ifafBox.Size = new Size(460, y);
        _ifafBox.Visible = false;
        layout.Controls.Add(_alphaBox);
        layout.Controls.Add(_ifafBox);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        buttons.Controls.AddRange(new Control[] { _pdf, _excel });
        layout.Controls.Add(buttons);
        layout.Controls.Add(_status);
        Controls.Add(layout);

        EventHandler switchMode = (_, _) =>
        {
            _alphaBox.Visible = !_ifaf.Checked;
            _onlyExpired.Visible = _expired.Checked;
            _ifafBox.Visible = _ifaf.Checked;
        };
        _alpha.CheckedChanged += switchMode;
        _ifaf.CheckedChanged += switchMode;
        _clothing.CheckedChanged += switchMode;
        _clubs.CheckedChanged += switchMode;
        _missing.CheckedChanged += switchMode;
        _expired.CheckedChanged += switchMode;
        _pdf.Click += async (_, _) => await CreateAsync("pdf");
        _excel.Click += async (_, _) => await CreateAsync("xlsx");
    }

    private async Task CreateAsync(string format)
    {
        var query = new Dictionary<string, string>();
        if (_ifaf.Checked)
        {
            query["competition"] = _competition.Text.Trim();
            query["game"] = _game.Text.Trim();
            query["team"] = _team.Text.Trim();
        }
        else
        {
            query["kader"] = _kader.SelectedIndex switch { 1 => "alle", 2 => "nicht_im_kader", _ => "kader" };
            if (_expired.Checked && _onlyExpired.Checked) query["nur"] = "abgelaufen";
        }

        var ext = format == "pdf" ? "pdf" : "xlsx";
        var suggestion = (_ifaf.Checked ? "IFAF-Roster-" + (_team.Text.Trim().Length > 0 ? _team.Text.Trim() : "Team") : _missing.Checked ? "Fehlende-Dokumente" : _expired.Checked ? "Abgelaufene-Dokumente" : _clubs.Checked ? "Roster-Vereine" : _clothing.Checked ? "Roster-Bekleidung" : "Roster-alphabetisch")
                         + $"-{DateTime.Now:yyyy-MM-dd}.{ext}";
        using var dialog = new SaveFileDialog
        {
            Filter = format == "pdf" ? "PDF|*.pdf" : "Excel|*.xlsx",
            FileName = string.Concat(suggestion.Split(Path.GetInvalidFileNameChars())),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _pdf.Enabled = _excel.Enabled = false;
        _status.ForeColor = Theme.Muted;
        _status.Text = "Roster wird erstellt …";
        try
        {
            var data = await _api.DownloadRosterAsync(_ifaf.Checked ? "-ifaf" : _missing.Checked ? "-fehlend" : _expired.Checked ? "-abgelaufen" : _clothing.Checked ? "-bekleidung" : _clubs.Checked ? "-vereine" : "", format, query);
            await File.WriteAllBytesAsync(dialog.FileName, data);
            _status.ForeColor = Theme.Green;
            _status.Text = "Gespeichert: " + dialog.FileName;
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = ex.Message;
        }
        finally
        {
            _pdf.Enabled = _excel.Enabled = true;
        }
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        // 
        // RosterForm
        // 
        ClientSize = new Size(284, 261);
        Name = "RosterForm";
        ResumeLayout(false);

    }
}
