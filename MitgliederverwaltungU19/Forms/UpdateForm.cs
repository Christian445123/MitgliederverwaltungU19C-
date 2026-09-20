using System.Diagnostics;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Programm-Updates: nach neuer Version suchen, Hinweise lesen und installieren.</summary>
public sealed class UpdateForm : Form
{
    private readonly AppSettings _settings;
    private UpdateInfo? _found;
    private CancellationTokenSource? _cts;

    private readonly Label _current = new() { AutoSize = true, Font = Theme.Bold };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 10, 0, 6) };
    private readonly TextBox _notes = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 130, Dock = DockStyle.Top, Visible = false, BackColor = Color.White };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 14, Visible = false };
    private readonly TextBox _repo = new() { Dock = DockStyle.Top };
    private readonly TextBox _token = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true, PlaceholderText = "nur bei privatem Repository nötig" };
    private readonly CheckBox _auto = new() { AutoSize = true, Text = "Beim Start automatisch nach Updates suchen" };
    private readonly Button _check = Theme.MakeButton("Nach Updates suchen");
    private readonly Button _install = Theme.MakeButton("Update installieren", primary: true);

    /// <param name="found">Bereits gefundenes Update (z. B. aus der Prüfung beim Start) oder null.</param>
    public UpdateForm(AppSettings settings, UpdateInfo? found = null)
    {
        _settings = settings;
        Text = "Programm-Updates";
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(620, 600);

        _current.Text = $"Installierte Version: {UpdateService.CurrentVersion}";
        _repo.Text = settings.GitHubRepo;
        _token.Text = settings.GitHubToken;
        _auto.Checked = settings.AutoCheckUpdates;
        _install.Enabled = false;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var close = Theme.MakeButton("Schließen");
        close.Click += (_, _) => Close();
        buttons.Controls.AddRange(new Control[] { _check, _install, close });
        _check.Click += async (_, _) => await CheckAsync();
        _install.Click += async (_, _) => await InstallAsync();

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 14, 0, 4),
            Text = "Die Updates kommen von GitHub (Bereich „Releases“). Ist das Repository privat, wird einmalig ein Zugriffstoken " +
                   "mit Leserechten benötigt (GitHub → Einstellungen → Developer settings → Fine-grained tokens → Contents: Read-only).",
        };

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), AutoScroll = true };
        layout.Controls.Add(_current);
        layout.Controls.Add(_status);
        layout.Controls.Add(_notes);
        layout.Controls.Add(_progress);
        layout.Controls.Add(buttons);
        layout.Controls.Add(help);
        layout.Controls.Add(new Label { Text = "GitHub-Repository (Besitzer/Repository)", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_repo);
        layout.Controls.Add(new Label { Text = "GitHub-Zugriffstoken (optional)", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_token);
        layout.Controls.Add(_auto);
        foreach (var c in new Control[] { _notes, _progress, _repo, _token })
        {
            c.Width = 560;
        }
        Controls.Add(layout);

        FormClosing += (_, _) => _cts?.Cancel();

        if (found is not null)
        {
            ShowFound(found);
        }
        else
        {
            SetStatus("Noch nicht geprüft. Mit „Nach Updates suchen“ wird die neueste Version von GitHub abgefragt.", null);
        }
    }

    private void SetStatus(string text, bool? ok)
    {
        _status.Text = text;
        _status.ForeColor = ok is null ? Theme.Muted : ok.Value ? Theme.Green : Theme.Danger;
    }

    private void SaveOptions()
    {
        _settings.GitHubRepo = _repo.Text.Trim();
        _settings.GitHubToken = _token.Text.Trim();
        _settings.AutoCheckUpdates = _auto.Checked;
        _settings.Save();
    }

    private void ShowFound(UpdateInfo info)
    {
        _found = info;
        SetStatus($"Neue Version verfügbar: {info.Version} (installiert: {UpdateService.CurrentVersion}).", true);
        _notes.Text = string.IsNullOrWhiteSpace(info.Notes) ? "(keine Versionshinweise)" : info.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _notes.Visible = true;
        _install.Enabled = true;
    }

    private async Task CheckAsync()
    {
        SaveOptions();
        _check.Enabled = _install.Enabled = false;
        _notes.Visible = false;
        SetStatus("Suche nach Updates …", null);
        try
        {
            var info = await UpdateService.CheckAsync(_settings.GitHubRepo, _settings.GitHubToken);
            if (info is null)
            {
                _found = null;
                SetStatus($"Die Anwendung ist auf dem neuesten Stand (Version {UpdateService.CurrentVersion}).", true);
            }
            else
            {
                ShowFound(info);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, false);
        }
        finally
        {
            _check.Enabled = true;
            _install.Enabled = _found is not null;
        }
    }

    private async Task InstallAsync()
    {
        if (_found is not { } info) return;
        var answer = MessageBox.Show(this,
            $"Version {info.Version} wird heruntergeladen und installiert.\n\n" +
            "Die Anwendung wird dafür beendet und danach automatisch neu gestartet. " +
            "Windows fragt einmal nach Administratorrechten.\n\nJetzt installieren?",
            "Update installieren", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        SaveOptions();
        _check.Enabled = _install.Enabled = false;
        _progress.Value = 0;
        _progress.Visible = true;
        SetStatus("Lade Update herunter …", null);
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(p =>
            {
                _progress.Value = Math.Clamp(p, 0, 100);
                SetStatus($"Lade Update herunter … {p} %", null);
            });
            var msi = await UpdateService.DownloadAsync(info, _settings.GitHubToken, progress, _cts.Token);
            SetStatus("Installation wird gestartet …", null);
            UpdateService.InstallAndExit(msi);
        }
        catch (OperationCanceledException)
        {
            // Fenster wurde geschlossen
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, false);
            _progress.Visible = false;
            _check.Enabled = true;
            _install.Enabled = true;
        }
    }
}
