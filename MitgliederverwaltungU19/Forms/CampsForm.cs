using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Weitere Camps verwalten (anlegen, umbenennen, löschen) wie im Web-Panel unter „Camps“.</summary>
public sealed class CampsForm : Form
{
    private readonly ApiClient _api;
    private readonly bool _canDelete;
    private readonly DataGridView _grid = new();
    private readonly TextBox _name = new() { Width = 260, PlaceholderText = "Name des neuen Camps, z. B. Camp 3" };
    private readonly Label _info = new() { AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Theme.Muted };

    public CampsForm(ApiClient api, bool canDelete)
    {
        _api = api;
        _canDelete = canDelete;
        Text = "Camps";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(780, 560);
        MinimumSize = new Size(640, 400);

        _info.Padding = new Padding(0, 0, 0, 0);
        var infoHost = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(16, 12, 16, 0) };
        infoHost.Controls.Add(_info);

        var add = Theme.MakeButton("Camp anlegen", primary: true);
        var rename = Theme.MakeButton("Umbenennen");
        var delete = Theme.MakeButton("Löschen");
        delete.ForeColor = Theme.Danger;
        delete.Enabled = canDelete;
        add.Click += async (_, _) => await AddAsync();
        rename.Click += async (_, _) => await RenameAsync();
        delete.Click += async (_, _) => await DeleteAsync();
        _name.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await AddAsync(); } };

        _name.Margin = new Padding(0, 4, 10, 0);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(16, 6, 16, 0) };
        bar.Controls.AddRange(new Control[] { _name, add, rename, delete });

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Theme.StyleGrid(_grid);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Camp", FillWeight = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Teilnehmer", FillWeight = 30 });
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await RenameAsync(); };

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 16) };
        host.Controls.Add(_grid);
        Controls.Add(host);
        Controls.Add(bar);
        Controls.Add(infoHost);
        Shown += async (_, _) => await LoadAsync();
    }

    private CampInfo? Selected() => _grid.CurrentRow?.Tag as CampInfo;

    private async Task LoadAsync()
    {
        try
        {
            var (fixedNames, camps) = await _api.GetCampsAdminAsync();
            _info.Text = $"Neben den festen Camps ({string.Join(", ", fixedNames)}) können beliebig viele weitere Camps angelegt werden. " +
                         "Jedes Camp erscheint bei allen Spielern als Ja/Nein-Feld, im Import/Export als eigene Spalte und in der API.";
            Fill(camps);
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private void Fill(IEnumerable<CampInfo> camps)
    {
        _grid.Rows.Clear();
        foreach (var c in camps)
        {
            var i = _grid.Rows.Add(c.Name, c.Members.ToString());
            _grid.Rows[i].Tag = c;
        }
    }

    private async Task AddAsync()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0) return;
        try
        {
            await _api.CreateCampAsync(name);
            _name.Clear();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private async Task RenameAsync()
    {
        if (Selected() is not { } camp) return;
        using var dialog = new PromptDialog("Camp umbenennen", "Neuer Name", camp.Name);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await _api.RenameCampAsync(camp.Id, dialog.Value);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected() is not { } camp) return;
        if (MessageBox.Show(this, $"Camp „{camp.Name}“ inklusive der Teilnahmen ({camp.Members}) wirklich löschen?", "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _api.DeleteCampAsync(camp.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }
}

/// <summary>Kleines Eingabefenster mit einer Zeile Text.</summary>
internal sealed class PromptDialog : Form
{
    private readonly TextBox _input = new() { Dock = DockStyle.Top };
    public string Value => _input.Text.Trim();

    public PromptDialog(string title, string label, string initial)
    {
        Text = title;
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(420, 150);
        _input.Text = initial;

        var ok = Theme.MakeButton("OK", primary: true);
        var cancel = Theme.MakeButton("Abbrechen");
        ok.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1 };
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 0, 0, 4) });
        layout.Controls.Add(_input);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        buttons.Controls.AddRange(new Control[] { ok, cancel });
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        Shown += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }
}
