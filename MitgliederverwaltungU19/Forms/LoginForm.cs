using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Anmeldung mit den Benutzerdaten des Web-Panels (gleicher Benutzername und gleiches Passwort).</summary>
public sealed class LoginForm : Form
{
    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly TextBox _user = new() { Dock = DockStyle.Top };
    private readonly TextBox _password = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true };
    private readonly CheckBox _remember = new() { AutoSize = true, Text = "Angemeldet bleiben (auf diesem Rechner)", Margin = new Padding(0, 10, 0, 0) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(400, 0), ForeColor = Theme.Danger, Margin = new Padding(0, 10, 0, 0) };
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
        ClientSize = new Size(440, 360);

        var quit = Theme.MakeButton("Beenden");
        quit.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _login.Click += async (_, _) => await LoginAsync();
        AcceptButton = _login;
        CancelButton = quit;

        var title = new Label { Text = "Mitgliederverwaltung U19", Font = new Font(Theme.Title.FontFamily, 16f, FontStyle.Bold), AutoSize = true };
        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 6, 0, 0),
            Text = "Melde dich mit deinen Zugangsdaten des Web-Panels an (gleicher Benutzername und gleiches Passwort).",
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        buttons.Controls.AddRange(new Control[] { _login, quit });

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, AutoScroll = true };
        layout.Controls.Add(title);
        layout.Controls.Add(intro);
        layout.Controls.Add(new Label { Text = "Benutzername", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 16, 0, 2) });
        layout.Controls.Add(_user);
        layout.Controls.Add(new Label { Text = "Passwort", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_password);
        layout.Controls.Add(_remember);
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
