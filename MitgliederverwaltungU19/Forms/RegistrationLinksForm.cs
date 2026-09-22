using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>
/// Registrierungslinks für neue Mitglieder verwalten (erzeugen, aktivieren/deaktivieren, löschen) und die
/// Benachrichtigungs-Adresse festlegen, an die bei neuen bzw. doppelten Anmeldungen eine E-Mail geht.
/// Entspricht dem Web-Panel unter „Neue Mitglieder“.
/// </summary>
public sealed class RegistrationLinksForm : Form
{
    private readonly ApiClient _api;
    private readonly bool _canWrite;
    private readonly DataGridView _grid = new();
    private readonly TextBox _label = new() { Width = 220, PlaceholderText = "Bezeichnung, z. B. Saison 2026" };
    private readonly ComboBox _linkType = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _expires = new() { ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly TextBox _notifyEmail = new() { Width = 260, PlaceholderText = "admin@verein.at" };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 0) };

    public RegistrationLinksForm(ApiClient api, bool canWrite)
    {
        _api = api;
        _canWrite = canWrite;
        Text = "Registrierungslinks – Neue Mitglieder";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 640);
        MinimumSize = new Size(760, 480);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(16, 12, 16, 0),
            ForeColor = Theme.Muted,
            Text = "Ein Link kann von beliebig vielen Personen zur Anmeldung genutzt werden, bis er deaktiviert oder gelöscht wird.",
        };

        // Benachrichtigungs-Adresse
        var notifyLabel = new Label { Text = "Benachrichtigung bei neuen/doppelten Anmeldungen:", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 6, 8, 0) };
        var saveNotify = Theme.MakeButton("Speichern");
        saveNotify.Enabled = _canWrite;
        saveNotify.Click += async (_, _) => await SaveNotifyEmailAsync();
        _notifyEmail.Enabled = _canWrite;
        var notifyBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 6, 16, 0) };
        notifyBar.Controls.AddRange(new Control[] { notifyLabel, _notifyEmail, saveNotify });

        // Neuen Link erzeugen
        _linkType.Items.AddRange(new object[] { "Spieler-Link", "Staff-Link" });
        _linkType.SelectedIndex = 0;
        var create = Theme.MakeButton("Link erzeugen", primary: true);
        create.Enabled = _canWrite;
        create.Click += async (_, _) => await CreateAsync();
        var expiresLabel = new Label { Text = "gültig bis (optional):", AutoSize = true, Margin = new Padding(10, 8, 4, 0) };
        _label.Enabled = _expires.Enabled = _linkType.Enabled = _canWrite;
        var createBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 10, 16, 0) };
        createBar.Controls.AddRange(new Control[] { _linkType, _label, expiresLabel, _expires, create });

        // Liste
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Theme.StyleGrid(_grid);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "type", HeaderText = "Art", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "label", HeaderText = "Bezeichnung", FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "url", HeaderText = "Link", FillWeight = 32 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "uses", HeaderText = "Verwendet", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "expires", HeaderText = "Gültig bis", FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", FillWeight = 13 });

        var copy = Theme.MakeButton("Link kopieren");
        var toggle = Theme.MakeButton("Aktivieren/Deaktivieren");
        var delete = Theme.MakeButton("Löschen");
        toggle.Enabled = delete.Enabled = _canWrite;
        copy.Click += (_, _) => { if (Selected() is { } l) { Clipboard.SetText(l.Url); _status.Text = "Link kopiert."; _status.ForeColor = Theme.Green; } };
        toggle.Click += async (_, _) => await ToggleAsync();
        delete.Click += async (_, _) => await DeleteAsync();
        var gridButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 10, 16, 0) };
        gridButtons.Controls.AddRange(new Control[] { copy, toggle, delete });

        var gridCard = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1), Margin = new Padding(16, 8, 16, 16) };
        gridCard.Controls.Add(_grid);
        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
        gridHost.Controls.Add(gridCard);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(16, 0, 16, 8) };
        statusHost.Controls.Add(_status);

        Controls.Add(gridHost);
        Controls.Add(gridButtons);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border, Margin = new Padding(0, 8, 0, 0) });
        Controls.Add(createBar);
        Controls.Add(notifyBar);
        Controls.Add(intro);
        Controls.Add(statusHost);

        Shown += async (_, _) => await LoadAsync();
    }

    private RegistrationLink? Selected() => _grid.CurrentRow?.Tag as RegistrationLink;

    private async Task LoadAsync()
    {
        try
        {
            _notifyEmail.Text = await _api.GetRegistrationNotifyEmailAsync();
            var links = await _api.ListRegistrationLinksAsync();
            Fill(links);
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private void Fill(IEnumerable<RegistrationLink> links)
    {
        _grid.Rows.Clear();
        foreach (var l in links)
        {
            var expires = l.ExpiresAt is { Length: > 0 } && DateTime.TryParse(l.ExpiresAt, out var exDate) ? exDate.ToString("dd.MM.yyyy") : "–";
            var idx = _grid.Rows.Add(l.IsStaff ? "Staff" : "Spieler", l.DisplayLabel, l.Url, l.UseCount.ToString(), expires, l.Active ? "Aktiv" : "Deaktiviert");
            var row = _grid.Rows[idx];
            row.Tag = l;
            row.Cells["type"].Style.ForeColor = l.IsStaff ? Theme.Muted : Theme.Green;
            row.Cells["status"].Style.ForeColor = l.Active ? Theme.Green : Theme.Muted;
        }
    }

    private async Task SaveNotifyEmailAsync()
    {
        try
        {
            await _api.SetRegistrationNotifyEmailAsync(_notifyEmail.Text.Trim());
            _status.Text = "Benachrichtigungs-Adresse gespeichert.";
            _status.ForeColor = Theme.Green;
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private async Task CreateAsync()
    {
        try
        {
            var expires = _expires.Checked ? _expires.Value.ToString("yyyy-MM-dd") : null;
            var linkType = _linkType.SelectedIndex == 1 ? "staff" : "player";
            await _api.CreateRegistrationLinkAsync(_label.Text.Trim(), expires, linkType);
            _label.Clear();
            _expires.Checked = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private async Task ToggleAsync()
    {
        if (Selected() is not { } link) return;
        try
        {
            await _api.SetRegistrationLinkActiveAsync(link.Id, !link.Active);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected() is not { } link) return;
        if (MessageBox.Show(this, $"Link „{link.DisplayLabel}“ wirklich löschen? Er funktioniert danach nicht mehr.", "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _api.DeleteRegistrationLinkAsync(link.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }
}
