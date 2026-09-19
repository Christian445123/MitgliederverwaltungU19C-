using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Änderungen am Web-Projekt committen (mit Kommentar), pushen und auf dem Server einspielen.</summary>
public sealed class DeployForm : Form
{
    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly Label _repoLabel = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly TextBox _changes = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9.5f), Dock = DockStyle.Fill };
    private readonly TextBox _message = new() { Dock = DockStyle.Top };
    private readonly CheckBox _pullServer = new() { Text = "Danach den Server aktualisieren (git pull)", Checked = true, AutoSize = true };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9.5f), Dock = DockStyle.Fill };
    private readonly Button _deploy = Theme.MakeButton("Committen && Pushen", primary: true);
    private GitService? _git;

    public DeployForm(AppSettings settings, ApiClient api)
    {
        _settings = settings;
        _api = api;

        Text = "Änderungen einspielen";
        Font = Theme.Body;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 640);
        MinimumSize = new Size(620, 520);

        var changeRepo = Theme.MakeButton("Ordner ändern …");
        var refresh = Theme.MakeButton("Aktualisieren");
        var close = Theme.MakeButton("Schließen");
        changeRepo.Click += async (_, _) => { if (ChooseRepo()) await ReloadAsync(); };
        refresh.Click += async (_, _) => await ReloadAsync();
        close.Click += (_, _) => Close();
        _deploy.Click += async (_, _) => await DeployAsync();
        _message.PlaceholderText = "Kommentar zur Änderung, z. B. „Fix: Telefonnummer wird formatiert“";

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(14, 12, 14, 0), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(new Label { Text = "Web-Projekt (Git-Ordner):", Font = Theme.Bold, AutoSize = true });
        top.Controls.Add(_repoLabel);
        var repoButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        repoButtons.Controls.AddRange(new Control[] { changeRepo, refresh });
        top.Controls.Add(repoButtons);

        var changesBox = new GroupBox { Text = "Geänderte Dateien", Dock = DockStyle.Top, Height = 180, Padding = new Padding(8) };
        changesBox.Controls.Add(_changes);

        var messageBox = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new Padding(14, 8, 14, 0) };
        _message.Margin = new Padding(0);
        messageBox.Controls.Add(_pullServer);
        _pullServer.Dock = DockStyle.Bottom;
        messageBox.Controls.Add(_message);
        messageBox.Controls.Add(new Label { Text = "Kommentar (Commit-Nachricht)", Font = Theme.Bold, AutoSize = true, Dock = DockStyle.Top });

        var logBox = new GroupBox { Text = "Protokoll", Dock = DockStyle.Fill, Padding = new Padding(8) };
        logBox.Controls.Add(_log);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 10, 12, 0), BackColor = Theme.Background };
        bar.Controls.Add(close);
        bar.Controls.Add(_deploy);

        var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 8) };
        center.Controls.Add(logBox);

        Controls.Add(center);
        Controls.Add(messageBox);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        Controls.Add(changesBox);
        Controls.Add(top);
        Controls.Add(bar);

        Shown += async (_, _) =>
        {
            if (!GitService.IsRepository(_settings.RepoPath) && !ChooseRepo())
            {
                Close();
                return;
            }
            await ReloadAsync();
        };
    }

    private bool ChooseRepo()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Ordner des Web-Projekts wählen (enthält den versteckten Ordner .git)",
            UseDescriptionForTitle = true,
            SelectedPath = _settings.RepoPath,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        if (!GitService.IsRepository(dialog.SelectedPath))
        {
            MessageBox.Show(this, "In diesem Ordner gibt es kein Git-Repository (.git).", "Kein Git-Ordner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _settings.RepoPath = dialog.SelectedPath;
        _settings.Save();
        return true;
    }

    private async Task ReloadAsync()
    {
        _repoLabel.Text = _settings.RepoPath;
        _git = GitService.IsRepository(_settings.RepoPath) ? new GitService(_settings.RepoPath) : null;
        if (_git is null)
        {
            _changes.Text = "Kein Git-Ordner gewählt.";
            _deploy.Enabled = false;
            return;
        }
        try
        {
            var status = await _git.StatusAsync();
            _changes.Text = status.Length == 0 ? "(keine lokalen Änderungen – es wird nur gepusht, falls Commits offen sind)" : status.Replace("\n", Environment.NewLine);
            _deploy.Enabled = true;
        }
        catch (Exception ex)
        {
            _changes.Text = ex.Message;
            _deploy.Enabled = false;
        }
    }

    private void Log(string text)
    {
        _log.AppendText(text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) + Environment.NewLine);
    }

    private async Task DeployAsync()
    {
        if (_git is null) return;
        var message = _message.Text.Trim();
        if (message.Length == 0)
        {
            MessageBox.Show(this, "Bitte einen Kommentar für den Commit eingeben.", "Kommentar fehlt", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _message.Focus();
            return;
        }
        if (_changes.Text.Contains(".env", StringComparison.Ordinal)
            && MessageBox.Show(this, "In der Änderungsliste steht eine Datei mit „.env“. Sie enthält normalerweise Passwörter und darf nicht ins Repository.\n\nTrotzdem fortfahren?",
                "Achtung", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _deploy.Enabled = false;
        UseWaitCursor = true;
        _log.Clear();
        try
        {
            var pushed = await _git.CommitAndPushAsync(message, Log);
            if (!pushed)
            {
                Log("\nAbgebrochen – der Server wurde nicht aktualisiert.");
                return;
            }
            Log("\nErfolgreich zu GitHub gepusht.");

            if (_pullServer.Checked)
            {
                Log("\n> Server: git pull");
                var (ok, log) = await _api.UpdateServerAsync();
                Log(log);
                Log(ok ? "\nServer ist aktuell." : "\nServer-Update fehlgeschlagen (siehe Protokoll).");
            }
            _message.Clear();
        }
        catch (Exception ex)
        {
            Log("Fehler: " + ex.Message);
        }
        finally
        {
            UseWaitCursor = false;
            await ReloadAsync();
        }
    }
}
