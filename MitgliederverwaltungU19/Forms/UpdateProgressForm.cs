using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>
/// Update laden: zeigt einen Fortschrittsbalken für den Download (falls die Datei nicht schon im Hintergrund geladen wurde)
/// und startet danach die Installation. Am Ende meldet ein Fenster „Update abgeschlossen“, das Programm startet automatisch neu.
/// </summary>
public sealed class UpdateProgressForm : Form
{
    private readonly UpdateInfo _info;
    private readonly string _token;
    private readonly string? _preloaded;
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 24, Minimum = 0, Maximum = 100 };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Top, Height = 48, ForeColor = Theme.Muted };
    private readonly CancellationTokenSource _cts = new();

    public UpdateProgressForm(UpdateInfo info, string token, string? preloadedMsi)
    {
        _info = info;
        _token = token;
        _preloaded = preloadedMsi is not null && File.Exists(preloadedMsi) ? preloadedMsi : null;
        Text = $"Update auf Version {info.Version}";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(480, 170);

        var cancel = Theme.MakeButton("Abbrechen");
        cancel.Click += (_, _) => { _cts.Cancel(); Close(); };
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel);

        var layout = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 8) };
        layout.Controls.Add(_status);
        layout.Controls.Add(_bar);
        layout.Controls.Add(new Label { Dock = DockStyle.Top, AutoSize = false, Height = 30, Font = Theme.Bold, Text = $"Version {info.Version} wird geladen" });
        Controls.Add(layout);
        Controls.Add(buttons);

        Shown += async (_, _) => await RunAsync(cancel);
        FormClosing += (_, _) => _cts.Cancel();
    }

    private async Task RunAsync(Button cancel)
    {
        try
        {
            string msi;
            if (_preloaded is not null)
            {
                _bar.Value = 100;
                msi = _preloaded;
            }
            else
            {
                _status.Text = "Update wird heruntergeladen …";
                var progress = new Progress<int>(p =>
                {
                    _bar.Value = Math.Clamp(p, 0, 100);
                    _status.Text = $"Update wird heruntergeladen … {p} %";
                });
                msi = await UpdateService.DownloadAsync(_info, _token, progress, _cts.Token);
            }

            cancel.Enabled = false;
            _bar.Value = 100;
            _status.ForeColor = Theme.Green;
            _status.Text = "Download abgeschlossen. Die Installation startet jetzt – die Anwendung wird kurz beendet und startet danach automatisch neu.";
            await Task.Delay(1200);
            UpdateService.InstallAndExit(msi);
        }
        catch (OperationCanceledException)
        {
            // abgebrochen
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = ex.Message;
        }
    }
}
