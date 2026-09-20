using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Adresse der Web-API und API-Schlüssel eintragen und testen.</summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _url = new() { Dock = DockStyle.Top };
    private readonly TextBox _token = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true };
    private readonly TextBox _encKey = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(440, 0) };

    public SettingsForm(AppSettings settings, bool firstRun)
    {
        _settings = settings;
        Text = firstRun ? "Ersteinrichtung – Verbindung" : "Verbindungseinstellungen";
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(480, 420);

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Text = "Trage die Adresse der Web-Anwendung, einen API-Schlüssel und den Verschlüsselungsschlüssel ein. " +
                   "Beides findet ein Administrator im Web-Bereich unter „API-Zugang“. Die gesamte Übertragung ist verschlüsselt.",
            ForeColor = Theme.Muted,
        };

        _url.Text = settings.BaseUrl;
        _url.PlaceholderText = "https://verein.example.at/api";
        _token.Text = settings.Token;
        _encKey.Text = settings.TransportKey;

        var test = Theme.MakeButton("Verbindung testen");
        var save = Theme.MakeButton("Speichern", primary: true);
        var cancel = Theme.MakeButton("Abbrechen");
        test.Click += async (_, _) => await TestAsync();
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange(new Control[] { test, save, cancel });

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            AutoScroll = true,
        };
        layout.Controls.Add(intro);
        layout.Controls.Add(new Label { Text = "API-Adresse", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 14, 0, 2) });
        layout.Controls.Add(_url);
        layout.Controls.Add(new Label { Text = "API-Schlüssel", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_token);
        layout.Controls.Add(new Label { Text = "Verschlüsselungsschlüssel (Webpanel → API-Zugang)", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_encKey);
        layout.Controls.Add(buttons);
        layout.Controls.Add(_status);

        // Programm-Updates (nicht bei der Ersteinrichtung)
        if (!firstRun)
        {
            var updates = Theme.MakeButton("Updates suchen / einspielen …");
            updates.Click += (_, _) =>
            {
                using var form = new UpdateForm(_settings);
                form.ShowDialog(this);
            };
            layout.Controls.Add(new Label
            {
                Text = $"Programm-Updates (installierte Version {UpdateService.CurrentVersion})",
                AutoSize = true,
                Font = Theme.Bold,
                Margin = new Padding(0, 22, 0, 4),
            });
            layout.Controls.Add(updates);

            // Lizenz
            var license = Theme.MakeButton("Lizenzschlüssel ändern …");
            var licenseInfo = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(440, 0),
                ForeColor = Theme.Muted,
                Text = string.IsNullOrEmpty(settings.LicenseKey)
                    ? "Kein Lizenzschlüssel hinterlegt."
                    : $"Schlüssel {LicenseService.MaskKey(settings.LicenseKey)}" + (string.IsNullOrEmpty(settings.LicenseName) ? "" : $" ({settings.LicenseName})"),
            };
            license.Click += async (_, _) =>
            {
                using var api = new ApiClient(_settings);
                using var form = new LicenseForm(_settings, api, "", mustActivate: false);
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    licenseInfo.Text = $"Schlüssel {LicenseService.MaskKey(_settings.LicenseKey)}" + (string.IsNullOrEmpty(_settings.LicenseName) ? "" : $" ({_settings.LicenseName})");
                }
                await Task.CompletedTask;
            };
            layout.Controls.Add(new Label { Text = "Lizenz", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 22, 0, 4) });
            layout.Controls.Add(licenseInfo);
            layout.Controls.Add(license);
            ClientSize = new Size(480, 580);
        }
        Controls.Add(layout);
    }

    private AppSettings Candidate()
    {
        var s = new AppSettings { BaseUrl = _url.Text.Trim() };
        s.Token = _token.Text.Trim();
        s.TransportKey = _encKey.Text.Trim();
        return s;
    }

    private async Task TestAsync()
    {
        if (string.IsNullOrWhiteSpace(_url.Text) || string.IsNullOrWhiteSpace(_token.Text) || !TransportCryptoHandler.IsValidKey(_encKey.Text))
        {
            SetStatus("Bitte Adresse, API-Schlüssel und Verschlüsselungsschlüssel (44 Zeichen) eintragen.", false);
            return;
        }
        SetStatus("Verbinde …", null);
        try
        {
            using var api = new ApiClient(Candidate());
            var ping = await api.PingAsync();
            SetStatus($"Verbindung erfolgreich – Zugang „{ping.TokenName}“ ({(ping.CanWrite ? "Lesen & Schreiben" : "nur Lesen")}).", true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, false);
        }
    }

    private void SetStatus(string text, bool? ok)
    {
        _status.Text = text;
        _status.ForeColor = ok is null ? Theme.Muted : ok.Value ? Theme.Green : Theme.Danger;
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_url.Text) || string.IsNullOrWhiteSpace(_token.Text) || !TransportCryptoHandler.IsValidKey(_encKey.Text))
        {
            SetStatus("Bitte Adresse, API-Schlüssel und Verschlüsselungsschlüssel (44 Zeichen) eintragen.", false);
            return;
        }
        _settings.BaseUrl = _url.Text.Trim();
        _settings.Token = _token.Text.Trim();
        _settings.TransportKey = _encKey.Text.Trim();
        _settings.Save();
        DialogResult = DialogResult.OK;
    }
}
