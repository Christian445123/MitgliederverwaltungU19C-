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
        Theme.Prepare(this);
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

        var title = new Label { Text = "Mitgliederverwaltung U19", Font = new Font(Theme.Title.FontFamily, 16f * Theme.Zoom, FontStyle.Bold), AutoSize = true };
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
        catch (ApiException ex) when (ex.Code == "must_change_password")
        {
            using var dialog = new NewPasswordDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    await _api.FirstPasswordAsync(username, _password.Text, dialog.NewPassword);
                    _password.Text = dialog.NewPassword;
                    _login.Enabled = true;
                    await LoginAsync();
                    return;
                }
                catch (ApiException ex2)
                {
                    _status.ForeColor = Theme.Danger;
                    _status.Text = ex2.Message;
                }
            }
            else
            {
                _status.ForeColor = Theme.Danger;
                _status.Text = "Ohne neues Passwort ist keine Anmeldung möglich.";
            }
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

/// <summary>Neues Passwort festlegen (Pflicht bei der ersten Anmeldung eines im Web-Panel angelegten Benutzers).</summary>
internal sealed class NewPasswordDialog : Form
{
    private readonly TextBox _new = new() { Width = 340, UseSystemPasswordChar = true };
    private readonly TextBox _repeat = new() { Width = 340, UseSystemPasswordChar = true };
    private readonly Label _error = new() { AutoSize = true, ForeColor = Theme.Danger, MaximumSize = new Size(340, 0) };

    public string NewPassword => _new.Text;

    public NewPasswordDialog()
    {
        Theme.Prepare(this);
        Text = "Neues Passwort festlegen";
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(390, 330);

        var ok = Theme.MakeButton("Passwort speichern", primary: true);
        var cancel = Theme.MakeButton("Abbrechen");
        ok.Click += (_, _) =>
        {
            if (_new.Text.Length < 8) { _error.Text = "Das Passwort muss mindestens 8 Zeichen haben."; return; }
            if (_new.Text != _repeat.Text) { _error.Text = "Die Passwörter stimmen nicht überein."; return; }
            DialogResult = DialogResult.OK;
        };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0), WrapContents = false };
        buttons.Controls.AddRange(new Control[] { ok, cancel });
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24, 20, 24, 12) };
        layout.Controls.Add(new Label { Text = "Vor der ersten Anmeldung muss ein eigenes Passwort festgelegt werden (mindestens 8 Zeichen).", AutoSize = true, MaximumSize = new Size(340, 0), ForeColor = Theme.Muted });
        layout.Controls.Add(new Label { Text = "Neues Passwort", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 14, 0, 3) });
        layout.Controls.Add(_new);
        layout.Controls.Add(new Label { Text = "Passwort wiederholen", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 10, 0, 3) });
        layout.Controls.Add(_repeat);
        layout.Controls.Add(buttons);
        layout.Controls.Add(_error);
        Controls.Add(layout);
    }
}
