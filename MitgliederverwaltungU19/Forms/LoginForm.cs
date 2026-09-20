using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Anmeldung mit den Benutzerdaten des Web-Panels (gleicher Benutzername und gleiches Passwort).</summary>
public sealed class LoginForm : Form
{
    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private const int ContentWidth = 392;
    private readonly TextBox _user = new() { Width = ContentWidth };
    private readonly TextBox _password = new() { Width = ContentWidth, UseSystemPasswordChar = true };
    private readonly CheckBox _remember = new() { AutoSize = true, Text = "Anmeldedaten speichern (21 Tage)", Checked = true, Margin = new Padding(0, 14, 0, 0) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0), ForeColor = Theme.Danger, Margin = new Padding(0, 12, 0, 0) };
    private readonly Button _login = Theme.MakeButton("Anmelden", primary: true);

    /// <summary>Angemeldeter Benutzer nach erfolgreichem Login.</summary>
    public UserInfo? User { get; private set; }

    public LoginForm(AppSettings settings, ApiClient api, string message = "")
    {
        _settings = settings;
        _api = api;
        Text = "Anmelden";
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(440, 480);

        var quit = Theme.MakeButton("Beenden");
        quit.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _login.Click += async (_, _) => await LoginAsync();
        AcceptButton = _login;
        CancelButton = quit;

        var title = new Label { Text = "Mitgliederverwaltung U19", Font = new Font(Theme.Title.FontFamily, 16f, FontStyle.Bold), AutoSize = true };
        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 6, 0, 0),
            Text = "Melde dich mit deinen Zugangsdaten des Web-Panels an (gleicher Benutzername und gleiches Passwort).",
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 18, 0, 0), WrapContents = false };
        buttons.Controls.AddRange(new Control[] { _login, quit });

        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 4, 0, 0),
            Text = "Nach 3 Wochen wird wieder nach dem Passwort gefragt. Das Passwort selbst wird nicht gespeichert.",
        };

        // Untereinander angeordnet, feste Breite: so ist immer die komplette Anmeldung sichtbar
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(24, 22, 24, 16),
        };
        layout.Controls.Add(title);
        layout.Controls.Add(intro);
        layout.Controls.Add(new Label { Text = "Benutzername", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 18, 0, 3) });
        layout.Controls.Add(_user);
        layout.Controls.Add(new Label { Text = "Passwort", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 12, 0, 3) });
        layout.Controls.Add(_password);
        layout.Controls.Add(_remember);
        layout.Controls.Add(hint);
        layout.Controls.Add(buttons);
        layout.Controls.Add(_status);
        Controls.Add(layout);

        _user.Text = settings.LastUsername;
        if (!string.IsNullOrWhiteSpace(message)) _status.Text = message;
        Shown += (_, _) => (_user.Text.Length == 0 ? _user : _password).Focus();
    }

    private async Task LoginAsync()
    {
        var username = _user.Text.Trim();
        if (username.Length == 0 || _password.Text.Length == 0)
        {
            _status.Text = "Bitte Benutzername und Passwort eingeben.";
            return;
        }

        _login.Enabled = false;
        _status.ForeColor = Theme.Muted;
        _status.Text = "Melde an …";
        try
        {
            var result = await _api.LoginAsync(username, _password.Text, _remember.Checked, LicenseService.MachineName);
            _api.SessionToken = result.Token;
            _settings.LastUsername = username;
            _settings.SessionToken = _remember.Checked ? result.Token : "";
            _settings.Save();
            User = result.User;
            DialogResult = DialogResult.OK;
        }
        catch (ApiException ex)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = ex.Message;
            _password.Clear();
            _password.Focus();
        }
        finally
        {
            _login.Enabled = true;
        }
    }
}
