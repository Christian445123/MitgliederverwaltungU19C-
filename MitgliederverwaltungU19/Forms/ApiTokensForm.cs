using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>API-Zugänge für PC-Anwendungen (Excel, PowerShell, eigene Tools) verwalten, wie im Web-Panel unter „API-Zugang“.</summary>
public sealed class ApiTokensForm : Form
{
    private readonly ApiClient _api;
    private readonly DataGridView _grid = new();
    private readonly TextBox _name = new() { Width = 220, PlaceholderText = "Bezeichnung, z. B. Excel Büro-PC" };
    private readonly CheckBox _canWrite = new() { AutoSize = true, Text = "Lesen && Schreiben (anlegen, ändern, löschen)" };
    private readonly Panel _newTokenBanner = new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.FromArgb(0xFF, 0xF2, 0xE6), Padding = new Padding(16, 10, 16, 10), Visible = false };
    private readonly TextBox _newTokenBox = new() { Width = 420, ReadOnly = true };

    public ApiTokensForm(ApiClient api)
    {
        _api = api;
        Text = "API-Zugänge";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 580);
        MinimumSize = new Size(640, 420);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Padding = new Padding(16, 12, 16, 0),
            ForeColor = Theme.Muted,
            Text = "Mit einem API-Zugang können PC-Anwendungen (z. B. Excel, PowerShell oder eigene Programme) direkt auf die " +
                   "Mitgliederdaten zugreifen – ohne Browser-Anmeldung. Jeder Zugang hat einen eigenen Schlüssel, der jederzeit widerrufen werden kann.",
        };

        var copyNewToken = Theme.MakeButton("Kopieren");
        copyNewToken.Click += (_, _) => { if (_newTokenBox.Text.Length > 0) Clipboard.SetText(_newTokenBox.Text); };
        var newTokenLayout = new FlowLayoutPanel { AutoSize = true };
        newTokenLayout.Controls.Add(new Label { Text = "Neuer API-Schlüssel (wird nur jetzt einmalig angezeigt):", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 6, 0, 4) });
        var newTokenRow = new FlowLayoutPanel { AutoSize = true };
        newTokenRow.Controls.AddRange(new Control[] { _newTokenBox, copyNewToken });
        newTokenLayout.Controls.Add(newTokenRow);
        newTokenLayout.Controls.Add(new Label { Text = "Bitte sicher aufbewahren. Bei Verlust den Zugang widerrufen und einen neuen erstellen.", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 4, 0, 0) });
        _newTokenBanner.Controls.Add(newTokenLayout);

        var create = Theme.MakeButton("Zugang erstellen", primary: true);
        create.Click += async (_, _) => await CreateAsync();
        _name.Margin = new Padding(0, 6, 10, 0);
        _canWrite.Margin = new Padding(0, 10, 10, 0);
        create.Margin = new Padding(0, 6, 0, 0);
        var createBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 6, 16, 6) };
        createBar.Controls.AddRange(new Control[] { _name, _canWrite, create });

        var revoke = Theme.MakeButton("Widerrufen");
        revoke.ForeColor = Theme.Danger;
        revoke.Click += async (_, _) => await DeleteAsync();
        var gridButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 4, 16, 0) };
        gridButtons.Controls.Add(revoke);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Theme.StyleGrid(_grid);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Bezeichnung", FillWeight = 30 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "access", HeaderText = "Berechtigung", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "created", HeaderText = "Erstellt", FillWeight = 24 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "used", HeaderText = "Zuletzt verwendet", FillWeight = 24 });

        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 16) };
        gridHost.Controls.Add(_grid);

        Controls.Add(gridHost);
        Controls.Add(gridButtons);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border, Margin = new Padding(0, 8, 0, 0) });
        Controls.Add(createBar);
        Controls.Add(_newTokenBanner);
        Controls.Add(intro);
        Shown += async (_, _) => await LoadAsync();
    }

    private ApiTokenInfo? Selected() => _grid.CurrentRow?.Tag as ApiTokenInfo;

    private async Task LoadAsync()
    {
        try
        {
            Fill(await _api.GetApiTokensAsync());
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private void Fill(IEnumerable<ApiTokenInfo> tokens)
    {
        _grid.Rows.Clear();
        foreach (var t in tokens)
        {
            var created = DateTime.TryParse(t.CreatedAt, out var c) ? c.ToString("dd.MM.yyyy") : t.CreatedAt;
            var used = t.LastUsedAt is { Length: > 0 } && DateTime.TryParse(t.LastUsedAt, out var u) ? u.ToString("dd.MM.yyyy HH:mm") : "noch nie";
            var idx = _grid.Rows.Add(t.Name, t.CanWrite ? "Lesen & Schreiben" : "Nur lesen", created + (string.IsNullOrEmpty(t.CreatedByName) ? "" : " · " + t.CreatedByName), used);
            _grid.Rows[idx].Tag = t;
            _grid.Rows[idx].Cells["access"].Style.ForeColor = t.CanWrite ? Theme.AccentDark : Theme.Muted;
        }
    }

    private async Task CreateAsync()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Bitte eine Bezeichnung angeben.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var token = await _api.CreateApiTokenAsync(name, _canWrite.Checked);
            _newTokenBox.Text = token;
            _newTokenBanner.Visible = true;
            _name.Clear();
            _canWrite.Checked = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected() is not { } token) return;
        if (MessageBox.Show(this, $"Zugang „{token.Name}“ widerrufen? Anwendungen mit diesem Schlüssel verlieren sofort den Zugriff.", "Widerrufen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _api.DeleteApiTokenAsync(token.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }
}
