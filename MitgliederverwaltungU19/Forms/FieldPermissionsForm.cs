using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>
/// Feld-Rechte wie im Web-Panel: welche Felder Spieler (persönlicher Link) sehen/ändern dürfen und
/// welche Felder Bearbeiter sehen. Administratoren sehen und ändern immer alles.
/// </summary>
public sealed class FieldPermissionsForm : Form
{
    private const string Edit = "Ansehen & ändern", View = "Nur ansehen", Hidden = "Ausgeblendet";

    private readonly ApiClient _api;
    private List<FieldPerm> _fields = new();
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(14, 9, 0, 0) };
    private readonly Button _save = Theme.MakeButton("Speichern", primary: true);

    public FieldPermissionsForm(ApiClient api)
    {
        _api = api;
        Text = "Feld-Rechte";
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(940, 720);
        MinimumSize = new Size(760, 480);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(16, 12, 16, 0),
            ForeColor = Theme.Muted,
            Text = "Lege fest, welche Felder Spieler in ihrem persönlichen Link sehen oder ändern dürfen und welche Felder Bearbeiter (Rolle „Bearbeiter“) sehen. " +
                   "Administratoren sehen und ändern immer alles. Ausgeblendete Felder werden nicht ausgeliefert.",
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        Theme.StyleGrid(_grid);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Bereich", FillWeight = 22, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Feld", FillWeight = 34, ReadOnly = true });
        var player = new DataGridViewComboBoxColumn { HeaderText = "Spieler (persönlicher Link)", FillWeight = 26, FlatStyle = FlatStyle.Flat };
        player.Items.AddRange(Edit, View, Hidden);
        _grid.Columns.Add(player);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Bearbeiter sehen", FillWeight = 18 });
        _grid.DataError += (_, e) => e.ThrowException = false;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) UpdateSummary(); };

        var bulkLabel = new Label { Text = "Alle Spieler-Felder:", AutoSize = true, Margin = new Padding(0, 9, 8, 0), Font = Theme.Bold };
        var bulkEdit = Theme.MakeButton("ansehen & ändern");
        var bulkView = Theme.MakeButton("nur ansehen");
        var bulkHide = Theme.MakeButton("ausblenden");
        bulkEdit.UseMnemonic = false; // "&" im Text nicht als Tastenkürzel deuten
        bulkEdit.Click += (_, _) => SetAllPlayers(Edit);
        bulkView.Click += (_, _) => SetAllPlayers(View);
        bulkHide.Click += (_, _) => SetAllPlayers(Hidden);
        var bulk = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(16, 6, 16, 0) };
        bulk.Controls.AddRange(new Control[] { bulkLabel, bulkEdit, bulkView, bulkHide, _summary });

        var reset = Theme.MakeButton("Auf Standard zurücksetzen");
        reset.ForeColor = Theme.Danger;
        reset.Click += async (_, _) => await ResetAsync();
        var close = Theme.MakeButton("Schließen");
        close.Click += (_, _) => Close();
        _save.Click += async (_, _) => await SaveAsync();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 10, 12, 0), BackColor = Color.White };
        bar.Controls.Add(close);
        bar.Controls.Add(_save);
        bar.Controls.Add(reset);

        Controls.Add(_grid);
        Controls.Add(bulk);
        Controls.Add(intro);
        Controls.Add(bar);
        Shown += async (_, _) => await LoadAsync();
    }

    private static string PlayerText(string key) => key switch { "view" => View, "hidden" => Hidden, _ => Edit };
    private static string PlayerKey(string text) => text switch { View => "view", Hidden => "hidden", _ => "edit" };

    private async Task LoadAsync()
    {
        try
        {
            UseWaitCursor = true;
            _fields = await _api.GetFieldPermissionsAsync();
            Fill();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void Fill()
    {
        _grid.Rows.Clear();
        string? lastGroup = null;
        foreach (var f in _fields)
        {
            var i = _grid.Rows.Add(f.Group == lastGroup ? "" : f.Group, f.Label, PlayerText(f.Player), f.Editor || f.Core);
            lastGroup = f.Group;
            var row = _grid.Rows[i];
            row.Tag = f;
            if (f.AdminOnly)
            {
                // Nur im Admin-Formular vorhanden: für Spieler nicht steuerbar
                // (ReadOnly und Format erst setzen, wenn die Zelle in der Zeile eingefügt ist)
                row.Cells[2] = new DataGridViewTextBoxCell { Value = "immer ausgeblendet" };
                row.Cells[2].ReadOnly = true;
                row.Cells[2].Style.ForeColor = Theme.Muted;
            }
            if (f.Core)
            {
                row.Cells[3].ReadOnly = true;
                row.Cells[3].Value = true;
                row.Cells[3].ToolTipText = "Stammdaten: für Bearbeiter immer sichtbar";
            }
        }
        UpdateSummary();
    }

    private void SetAllPlayers(string text)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is FieldPerm { AdminOnly: false }) row.Cells[2].Value = text;
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int edit = 0, view = 0, hidden = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not FieldPerm { AdminOnly: false }) continue;
            switch (row.Cells[2].Value as string)
            {
                case View: view++; break;
                case Hidden: hidden++; break;
                default: edit++; break;
            }
        }
        _summary.Text = $"Spieler: {edit} ansehen & ändern · {view} nur ansehen · {hidden} ausgeblendet";
    }

    private async Task SaveAsync()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not FieldPerm f) continue;
            if (!f.AdminOnly) f.Player = PlayerKey(row.Cells[2].Value as string ?? Edit);
            f.Editor = f.Core || row.Cells[3].Value is true;
        }
        _save.Enabled = false;
        try
        {
            await _api.SaveFieldPermissionsAsync(_fields);
            MessageBox.Show(this, "Die Feld-Rechte wurden gespeichert. Sie gelten sofort für alle Links und den Bearbeiter-Zugang.", "Gespeichert", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
        finally
        {
            _save.Enabled = true;
        }
    }

    private async Task ResetAsync()
    {
        if (MessageBox.Show(this, "Alle Feld-Rechte auf den Standard zurücksetzen? Spieler dürfen danach wieder alle Felder sehen und ändern, Bearbeiter sehen alles.",
                "Zurücksetzen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _api.ResetFieldPermissionsAsync();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }
}
