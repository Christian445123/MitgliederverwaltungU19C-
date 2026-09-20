using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Konto: eigenes Passwort ändern (wie im Web-Panel unter „Konto“).</summary>
public sealed class AccountForm : Form
{
    private readonly ApiClient _api;
    private readonly TextBox _current = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true };
    private readonly TextBox _new = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true };
    private readonly TextBox _repeat = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(400, 0), Margin = new Padding(0, 10, 0, 0) };
    private readonly Button _save = Theme.MakeButton("Passwort ändern", primary: true);

    public AccountForm(ApiClient api, string username, string role)
    {
        _api = api;
        Text = "Konto";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(440, 420);

        var close = Theme.MakeButton("Schließen");
        close.Click += (_, _) => Close();
        _save.Click += async (_, _) => await SaveAsync();
        AcceptButton = _save;
        CancelButton = close;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        buttons.Controls.AddRange(new Control[] { _save, close });

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, AutoScroll = true };
        layout.Controls.Add(new Label { Text = "Konto", Font = new Font(Theme.Title.FontFamily, 16f * Theme.Zoom, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 6, 0, 0),
            Text = $"Angemeldet als {username} ({(role == "administrator" ? "Administrator" : "Benutzer")}). Das Passwort ist dasselbe wie im Web-Panel.",
            MaximumSize = new Size(400, 0),
        });
        layout.Controls.Add(new Label { Text = "Aktuelles Passwort", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 16, 0, 2) });
        layout.Controls.Add(_current);
        layout.Controls.Add(new Label { Text = "Neues Passwort (mind. 8 Zeichen)", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_new);
        layout.Controls.Add(new Label { Text = "Neues Passwort wiederholen", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_repeat);
        layout.Controls.Add(buttons);
        layout.Controls.Add(_status);
        Controls.Add(layout);
    }

    private async Task SaveAsync()
    {
        _status.ForeColor = Theme.Danger;
        if (_current.Text.Length == 0 || _new.Text.Length == 0)
        {
            _status.Text = "Bitte das aktuelle und das neue Passwort eingeben.";
            return;
        }
        if (_new.Text != _repeat.Text)
        {
            _status.Text = "Die beiden neuen Passwörter stimmen nicht überein.";
            return;
        }
        _save.Enabled = false;
        try
        {
            await _api.ChangePasswordAsync(_current.Text, _new.Text);
            _current.Clear();
            _new.Clear();
            _repeat.Clear();
            _status.ForeColor = Theme.Green;
            _status.Text = "Das Passwort wurde geändert. Andere angemeldete Geräte müssen sich neu anmelden.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _save.Enabled = true;
        }
    }
}
