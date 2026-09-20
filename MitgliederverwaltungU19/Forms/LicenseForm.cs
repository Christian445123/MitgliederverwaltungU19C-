using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Lizenzschlüssel eingeben und beim Server aktivieren. Der Schlüssel wird im Web-Bereich unter „Lizenzen“ erzeugt.</summary>
public sealed class LicenseForm : Form
{
    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly TextBox _key = new() { Dock = DockStyle.Top, CharacterCasing = CharacterCasing.Upper, PlaceholderText = "U19-XXXX-XXXX-XXXX-XXXX" };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(0, 10, 0, 0) };
    private readonly Button _activate = Theme.MakeButton("Aktivieren", primary: true);
    private readonly Button _quit;

    /// <param name="message">Grund, warum die Eingabe nötig ist (z. B. „Lizenz gesperrt“) oder leer.</param>
    /// <param name="mustActivate">true = ohne gültige Lizenz kann die Anwendung nicht weiterlaufen („Beenden“ statt „Abbrechen“).</param>
    public LicenseForm(AppSettings settings, ApiClient api, string message, bool mustActivate)
    {
        _settings = settings;
        _api = api;
        Text = "Lizenz";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(500, 440);

        _quit = Theme.MakeButton(mustActivate ? "Anwendung beenden" : "Abbrechen");
        _quit.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _activate.Click += async (_, _) => await ActivateAsync();
        AcceptButton = _activate;
        CancelButton = _quit;

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = Theme.Muted,
            Text = "Für diese Anwendung ist ein Lizenzschlüssel nötig. Den Schlüssel erstellt ein Administrator im Web-Bereich unter „Lizenzen“. " +
                   "Die Lizenz wird regelmäßig beim Server geprüft; bei einer unterbrochenen Verbindung läuft die Anwendung bis zu 3 Tage weiter.",
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        buttons.Controls.AddRange(new Control[] { _activate, _quit });

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, AutoScroll = true };
        layout.Controls.Add(intro);
        layout.Controls.Add(new Label { Text = "Lizenzschlüssel", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 16, 0, 2) });
        layout.Controls.Add(_key);
        layout.Controls.Add(new Label { Text = "Offline-Lizenz (automatisch eingetragen)", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 14, 0, 2) });
        layout.Controls.Add(new TextBox { Dock = DockStyle.Top, ReadOnly = true, Text = LicenseService.OfflineLicenseKey, BackColor = Color.FromArgb(0xF3, 0xF4, 0xF8) });
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 4, 0, 0),
            Text = "Ist der Server nicht erreichbar, gilt sie automatisch, aber höchstens 3 Tage. Danach ist wieder eine Verbindung nötig.",
        });
        layout.Controls.Add(buttons);
        layout.Controls.Add(_status);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 16, 0, 0),
            Text = $"Gerät: {LicenseService.MachineName}",
        });
        Controls.Add(layout);

        var current = settings.LicenseKey;
        if (!string.IsNullOrEmpty(current)) _key.Text = current;
        if (!string.IsNullOrWhiteSpace(message)) SetStatus(message, false);
    }

    private void SetStatus(string text, bool? ok)
    {
        _status.Text = text;
        _status.ForeColor = ok is null ? Theme.Muted : ok.Value ? Theme.Green : Theme.Danger;
    }

    private async Task ActivateAsync()
    {
        var entered = LicenseService.NormalizeKey(_key.Text);
        if (entered.Length == 0)
        {
            SetStatus("Bitte den Lizenzschlüssel eintragen.", false);
            return;
        }

        var previousKey = _settings.LicenseKey;
        var previousLease = _settings.LicenseLease;
        _activate.Enabled = false;
        SetStatus("Prüfe Lizenz …", null);
        try
        {
            _settings.LicenseKey = entered;
            var result = await LicenseService.CheckAsync(_settings, _api);
            // Ohne Serververbindung greift die eingebaute Offline-Lizenz (3 Tage); der Schlüssel muss dann wenigstens das richtige Format haben
            if (result.Status == LicenseStatus.Valid || (result.Status == LicenseStatus.OfflineGrace && LicenseService.LooksLikeKey(entered)))
            {
                _settings.Save();
                DialogResult = DialogResult.OK;
                return;
            }

            // Nicht bestätigt: bisherigen Zustand wiederherstellen (ein Offline-Ergebnis zählt bei neuem Schlüssel nicht)
            _settings.LicenseKey = previousKey;
            _settings.LicenseLease = previousLease;
            _settings.Save();
            SetStatus(result.Message, false);
        }
        catch (Exception ex)
        {
            _settings.LicenseKey = previousKey;
            _settings.LicenseLease = previousLease;
            SetStatus(ex.Message, false);
        }
        finally
        {
            _activate.Enabled = true;
        }
    }
}
