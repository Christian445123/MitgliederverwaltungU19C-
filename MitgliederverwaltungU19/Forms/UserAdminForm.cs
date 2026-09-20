using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Benutzer, Rollen und Rechte verwalten (wie im Web-Panel unter „Benutzer“ und „Rollen“).</summary>
public sealed class UserAdminForm : Form
{
    private readonly ApiClient _api;
    private List<PermissionDef> _permissions = new();
    private List<RoleInfo> _roles = new();
    private List<UserRow> _users = new();
    private int _youId;
    private bool _youAreAdmin;

    private readonly DataGridView _userGrid = new();
    private readonly DataGridView _roleGrid = new();
    private readonly Label _status = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 8, 0, 0) };

    public UserAdminForm(ApiClient api)
    {
        _api = api;
        Text = "Benutzer & Rechte";
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 620);
        MinimumSize = new Size(760, 440);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        tabs.TabPages.Add(BuildUsersTab());
        tabs.TabPages.Add(BuildRolesTab());
        Controls.Add(tabs);

        Shown += async (_, _) => await ReloadAsync();
    }

    // ── Aufbau ────────────────────────────────────────────────────────────

    private static void PrepareGrid(DataGridView g)
    {
        g.Dock = DockStyle.Fill;
        g.ReadOnly = true;
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.MultiSelect = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Theme.StyleGrid(g);
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    }

    private TabPage BuildUsersTab()
    {
        var page = new TabPage("Benutzer") { BackColor = Theme.Background, Padding = new Padding(12) };
        PrepareGrid(_userGrid);
        _userGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Benutzername", FillWeight = 24 });
        _userGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rolle", FillWeight = 20 });
        _userGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Einzelrechte", FillWeight = 14 });
        _userGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", FillWeight = 18 });
        _userGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Angelegt am", FillWeight = 14 });
        _userGrid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await EditUserAsync(); };

        var add = Theme.MakeButton("+ Neuer Benutzer", primary: true);
        var edit = Theme.MakeButton("Bearbeiten / Rechte");
        var delete = Theme.MakeButton("Löschen");
        delete.ForeColor = Theme.Danger;
        var refresh = Theme.MakeButton("Aktualisieren");
        add.Click += async (_, _) => await NewUserAsync();
        edit.Click += async (_, _) => await EditUserAsync();
        delete.Click += async (_, _) => await DeleteUserAsync();
        refresh.Click += async (_, _) => await ReloadAsync();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(0, 4, 0, 0) };
        bar.Controls.AddRange(new Control[] { add, edit, delete, refresh, _status });
        page.Controls.Add(_userGrid);
        page.Controls.Add(bar);
        return page;
    }

    private TabPage BuildRolesTab()
    {
        var page = new TabPage("Rollen") { BackColor = Theme.Background, Padding = new Padding(12) };
        PrepareGrid(_roleGrid);
        _roleGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rolle", FillWeight = 20 });
        _roleGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Beschreibung", FillWeight = 40 });
        _roleGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rechte", FillWeight = 10 });
        _roleGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Benutzer", FillWeight = 10 });
        _roleGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Art", FillWeight = 12 });
        _roleGrid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await EditRoleAsync(); };

        var add = Theme.MakeButton("+ Neue Rolle", primary: true);
        var edit = Theme.MakeButton("Bearbeiten / Rechte");
        var delete = Theme.MakeButton("Löschen");
        delete.ForeColor = Theme.Danger;
        add.Click += async (_, _) => await NewRoleAsync();
        edit.Click += async (_, _) => await EditRoleAsync();
        delete.Click += async (_, _) => await DeleteRoleAsync();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(0, 4, 0, 0) };
        bar.Controls.AddRange(new Control[] { add, edit, delete });
        page.Controls.Add(_roleGrid);
        page.Controls.Add(bar);
        return page;
    }

    // ── Daten ─────────────────────────────────────────────────────────────

    private async Task ReloadAsync()
    {
        try
        {
            UseWaitCursor = true;
            _status.Text = "Lade …";
            _permissions = await _api.GetPermissionsAsync();
            _roles = await _api.GetRolesAsync();
            (_users, _youId, _youAreAdmin) = await _api.GetUsersAsync();
            FillGrids();
            _status.Text = $"{_users.Count} Benutzer · {_roles.Count} Rollen";
        }
        catch (Exception ex)
        {
            _status.Text = "";
            Theme.ShowError(this, ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void FillGrids()
    {
        _userGrid.Rows.Clear();
        foreach (var u in _users)
        {
            var i = _userGrid.Rows.Add(
                u.Username + (u.Id == _youId ? "  (du)" : ""),
                u.RoleName,
                u.Overrides > 0 ? $"{u.Overrides} gesetzt" : "–",
                u.MustChangePassword ? "Passwort ausstehend" : "Aktiv",
                DateTime.TryParse(u.CreatedAt, out var d) ? d.ToString("dd.MM.yyyy") : u.CreatedAt);
            _userGrid.Rows[i].Tag = u;
            if (u.MustChangePassword) _userGrid.Rows[i].Cells[3].Style.ForeColor = Theme.AccentDark;
        }

        _roleGrid.Rows.Clear();
        foreach (var r in _roles)
        {
            var i = _roleGrid.Rows.Add(r.Name, r.Description, r.Permissions.Count.ToString(), r.Users.ToString(), r.IsSystem ? "eingebaut" : "eigene");
            _roleGrid.Rows[i].Tag = r;
        }
    }

    private UserRow? SelectedUser() => _userGrid.CurrentRow?.Tag as UserRow;
    private RoleInfo? SelectedRole() => _roleGrid.CurrentRow?.Tag as RoleInfo;

    // ── Benutzer ──────────────────────────────────────────────────────────

    private async Task NewUserAsync()
    {
        using var dialog = new UserEditDialog(_api, _permissions, _roles, null, _youAreAdmin);
        if (dialog.ShowDialog(this) == DialogResult.OK) await ReloadAsync();
    }

    private async Task EditUserAsync()
    {
        if (SelectedUser() is not { } user) return;
        try
        {
            UseWaitCursor = true;
            var detail = await _api.GetUserAsync(user.Id);
            UseWaitCursor = false;
            using var dialog = new UserEditDialog(_api, _permissions, _roles, detail, _youAreAdmin);
            if (dialog.ShowDialog(this) == DialogResult.OK) await ReloadAsync();
        }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            Theme.ShowError(this, ex);
        }
    }

    private async Task DeleteUserAsync()
    {
        if (SelectedUser() is not { } user) return;
        if (MessageBox.Show(this, $"Benutzer „{user.Username}“ wirklich löschen?", "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _api.DeleteUserAsync(user.Id);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    // ── Rollen ────────────────────────────────────────────────────────────

    private async Task NewRoleAsync()
    {
        using var dialog = new RoleEditDialog(_api, _permissions, null);
        if (dialog.ShowDialog(this) == DialogResult.OK) await ReloadAsync();
    }

    private async Task EditRoleAsync()
    {
        if (SelectedRole() is not { } role) return;
        using var dialog = new RoleEditDialog(_api, _permissions, role);
        if (dialog.ShowDialog(this) == DialogResult.OK) await ReloadAsync();
    }

    private async Task DeleteRoleAsync()
    {
        if (SelectedRole() is not { } role) return;
        if (MessageBox.Show(this, $"Rolle „{role.Name}“ wirklich löschen?", "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _api.DeleteRoleAsync(role.Id);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }
}

/// <summary>Benutzer anlegen/bearbeiten: Name, Passwort, Rolle und Einzelrechte (Standard / Erlauben / Verbieten je Schritt).</summary>
internal sealed class UserEditDialog : Form
{
    private const string Default = "Standard", Allow = "Erlauben", Deny = "Verbieten";

    private readonly ApiClient _api;
    private readonly UserDetail? _detail;
    private readonly IReadOnlyList<PermissionDef> _permissions;
    private readonly TextBox _username = new() { Width = 300 };
    private readonly TextBox _password = new() { Width = 300, UseSystemPasswordChar = true };
    private readonly ComboBox _role = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _grid = new();
    private readonly Label _error = new() { AutoSize = true, ForeColor = Theme.Danger, MaximumSize = new Size(560, 0) };
    private readonly Button _save = Theme.MakeButton("Speichern", primary: true);

    public UserEditDialog(ApiClient api, IReadOnlyList<PermissionDef> permissions, IReadOnlyList<RoleInfo> roles, UserDetail? detail, bool youAreAdmin)
    {
        _api = api;
        _detail = detail;
        _permissions = permissions;
        Text = detail is null ? "Neuer Benutzer" : "Benutzer bearbeiten – " + detail.Username;
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 760);
        MinimumSize = new Size(640, 560);

        // Nur Administratoren dürfen die Administrator-Rolle vergeben (wie im Web-Panel)
        foreach (var r in roles.Where(r => youAreAdmin || !r.IsAdministrator)) _role.Items.Add(r);
        var current = roles.FirstOrDefault(r => r.Id == detail?.RoleId) ?? roles.FirstOrDefault(r => r.Key == "editor");
        _role.SelectedItem = current is not null && _role.Items.Contains(current) ? current : (_role.Items.Count > 0 ? _role.Items[0] : null);
        _username.Text = detail?.Username ?? "";

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(16, 14, 16, 0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control input)
        {
            top.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            input.Margin = new Padding(0, 4, 0, 4);
            top.Controls.Add(input);
        }
        Row("Benutzername *", _username);
        Row(detail is null ? "Passwort * (mind. 8 Zeichen)" : "Neues Passwort (leer = unverändert)", _password);
        Row("Rolle", _role);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 64,
            Padding = new Padding(16, 8, 16, 0),
            ForeColor = Theme.Muted,
            Text = "Einzelrechte überschreiben die Rolle für diesen Benutzer: „Erlauben“ gibt ein Recht dazu, „Verbieten“ nimmt es weg, „Standard“ übernimmt die Rolle. " +
                   "Ein neues Passwort muss der Benutzer beim nächsten Login im Web-Panel ändern.",
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        Theme.StyleGrid(_grid);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Bereich", FillWeight = 13, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Berechtigung", FillWeight = 40, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Durch Rolle", FillWeight = 12, ReadOnly = true });
        var choice = new DataGridViewComboBoxColumn { HeaderText = "Einzelrecht", FillWeight = 16, FlatStyle = FlatStyle.Flat };
        choice.Items.AddRange(Default, Allow, Deny);
        _grid.Columns.Add(choice);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Wirksam", FillWeight = 13, ReadOnly = true });
        _grid.DataError += (_, e) => e.ThrowException = false;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 3) RefreshRow(_grid.Rows[e.RowIndex]); };

        foreach (var p in permissions)
        {
            var value = detail is not null && detail.Overrides.TryGetValue(p.Key, out var o) ? (o == "allow" ? Allow : o == "deny" ? Deny : Default) : Default;
            var i = _grid.Rows.Add(p.Group, p.Label, "", value, "");
            _grid.Rows[i].Tag = p;
        }
        _role.SelectedIndexChanged += (_, _) => RefreshAll();
        RefreshAll();

        var cancel = Theme.MakeButton("Abbrechen");
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _save.Click += async (_, _) => await SaveAsync();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 10, 12, 0), BackColor = Theme.Background };
        bar.Controls.Add(cancel);
        bar.Controls.Add(_save);
        bar.Controls.Add(_error);
        CancelButton = cancel;

        Controls.Add(_grid);
        Controls.Add(hint);
        Controls.Add(top);
        Controls.Add(bar);
    }

    private RoleInfo? SelectedRole => _role.SelectedItem as RoleInfo;

    private void RefreshAll()
    {
        var isAdmin = SelectedRole?.IsAdministrator == true;
        foreach (DataGridViewRow row in _grid.Rows) RefreshRow(row);
        _grid.Columns[3].ReadOnly = isAdmin; // Administratoren haben immer alle Rechte, es gibt keine Ausnahmen
    }

    private void RefreshRow(DataGridViewRow row)
    {
        if (row.Tag is not PermissionDef p) return;
        var role = SelectedRole;
        var byRole = role is not null && role.Permissions.Contains(p.Key);
        var choice = row.Cells[3].Value as string ?? Default;
        var effective = role?.IsAdministrator == true || (choice == Allow ? true : choice == Deny ? false : byRole);
        row.Cells[2].Value = byRole ? "✓" : "–";
        row.Cells[4].Value = effective ? "✓" : "✗";
        row.Cells[4].Style.ForeColor = effective ? Theme.Green : Theme.Danger;
        row.Cells[3].Style.ForeColor = choice == Allow ? Theme.Green : choice == Deny ? Theme.Danger : Color.Black;
    }

    private async Task SaveAsync()
    {
        if (SelectedRole is not { } role)
        {
            _error.Text = "Bitte eine Rolle auswählen.";
            return;
        }
        var overrides = new Dictionary<string, string>();
        if (!role.IsAdministrator)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not PermissionDef p) continue;
                var c = row.Cells[3].Value as string;
                if (c == Allow) overrides[p.Key] = "allow";
                else if (c == Deny) overrides[p.Key] = "deny";
            }
        }

        _save.Enabled = false;
        _error.Text = "";
        try
        {
            await _api.SaveUserAsync(_detail?.Id, _username.Text.Trim(), _password.Text, role.Id, overrides);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _error.Text = ex.Message;
        }
        finally
        {
            _save.Enabled = true;
        }
    }
}

/// <summary>Rolle anlegen/bearbeiten: Name, Beschreibung und die erlaubten Schritte (Häkchen je Berechtigung, gruppiert).</summary>
internal sealed class RoleEditDialog : Form
{
    private readonly ApiClient _api;
    private readonly RoleInfo? _role;
    private readonly TextBox _name = new() { Width = 320 };
    private readonly TextBox _description = new() { Width = 320 };
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, CheckBoxes = true, ShowLines = false, ItemHeight = 24, BorderStyle = BorderStyle.None };
    private readonly Label _error = new() { AutoSize = true, ForeColor = Theme.Danger, MaximumSize = new Size(480, 0) };
    private readonly Button _save = Theme.MakeButton("Speichern", primary: true);
    private bool _updating;

    public RoleEditDialog(ApiClient api, IReadOnlyList<PermissionDef> permissions, RoleInfo? role)
    {
        _api = api;
        _role = role;
        var locked = role?.IsAdministrator == true;
        Text = role is null ? "Neue Rolle" : "Rolle bearbeiten – " + role.Name;
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 720);
        MinimumSize = new Size(520, 520);

        _name.Text = role?.Name ?? "";
        _description.Text = role?.Description ?? "";
        _name.Enabled = _description.Enabled = !locked;

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(16, 14, 16, 4) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.Controls.Add(new Label { Text = "Name *", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        top.Controls.Add(_name);
        top.Controls.Add(new Label { Text = "Beschreibung", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        top.Controls.Add(_description);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = locked ? 44 : 30,
            Padding = new Padding(16, 6, 16, 0),
            ForeColor = Theme.Muted,
            Text = locked ? "Die Rolle „Administrator“ ist fest: Administratoren dürfen immer alles." : "Setze ein Häkchen bei jedem Schritt, den die Rolle ausführen darf.",
        };

        foreach (var group in permissions.GroupBy(p => p.Group))
        {
            var parent = new TreeNode(group.Key) { ForeColor = Theme.AccentDark };
            foreach (var p in group)
            {
                parent.Nodes.Add(new TreeNode(p.Label) { Tag = p.Key, Checked = role?.Permissions.Contains(p.Key) == true });
            }
            parent.Checked = parent.Nodes.Cast<TreeNode>().All(n => n.Checked);
            _tree.Nodes.Add(parent);
        }
        _tree.ExpandAll();
        _tree.AfterCheck += (_, e) =>
        {
            if (_updating || e.Node is null) return;
            _updating = true;
            if (e.Node.Nodes.Count > 0)
            {
                foreach (TreeNode child in e.Node.Nodes) child.Checked = e.Node.Checked;
            }
            else if (e.Node.Parent is { } parent)
            {
                parent.Checked = parent.Nodes.Cast<TreeNode>().All(n => n.Checked);
            }
            _updating = false;
        };
        _tree.BeforeCheck += (_, e) => { if (locked) e.Cancel = true; };
        _tree.Nodes[0].EnsureVisible();

        var cancel = Theme.MakeButton(locked ? "Schließen" : "Abbrechen");
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _save.Click += async (_, _) => await SaveAsync();
        _save.Enabled = !locked;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 10, 12, 0), BackColor = Theme.Background };
        bar.Controls.Add(cancel);
        if (!locked) bar.Controls.Add(_save);
        bar.Controls.Add(_error);
        CancelButton = cancel;

        var treeHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 8) };
        treeHost.Controls.Add(_tree);
        Controls.Add(treeHost);
        Controls.Add(hint);
        Controls.Add(top);
        Controls.Add(bar);
    }

    private async Task SaveAsync()
    {
        var selected = _tree.Nodes.Cast<TreeNode>()
            .SelectMany(g => g.Nodes.Cast<TreeNode>())
            .Where(n => n.Checked)
            .Select(n => n.Tag as string ?? "")
            .Where(key => key.Length > 0)
            .ToList();

        _save.Enabled = false;
        _error.Text = "";
        try
        {
            await _api.SaveRoleAsync(_role?.Id, _name.Text.Trim(), _description.Text.Trim(), selected);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _error.Text = ex.Message;
        }
        finally
        {
            _save.Enabled = true;
        }
    }
}
