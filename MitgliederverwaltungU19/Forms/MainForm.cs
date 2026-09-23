using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly PingResult _ping;

    private readonly TextBox _search = new() { Width = 240, PlaceholderText = "Suche: Name, E-Mail, Verein, Jersey Nr." };
    private readonly ComboBox _statusFilter = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _kaderFilter = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _grid = new();
    private readonly Label _stats = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 8, 0, 0) };
    private readonly Label _footer = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<Button> _writeButtons = new();

    private List<Member> _all = new();
    private readonly HashSet<int> _checkedIds = new();
    private Action? _updateBulkButtons;
    private readonly ComboBox _campAssign = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _campAction = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private List<(string Value, string Label)> _campOptions = new();
    private readonly Panel _banner = new() { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(0xFF, 0xF2, 0xE6), Visible = false };
    private readonly Label _bannerText = new() { AutoSize = true, Location = new Point(16, 11), Font = Theme.Bold };
    private readonly Button _showExpiry = Theme.MakeButton("Ablauf anzeigen");
    private readonly Button _showMissing = Theme.MakeButton("Fehlende Dokumente");
    private bool _expiryAnnounced;
    private readonly Button _updateButton = Theme.MakeButton("", primary: true);
    private UpdateInfo? _update;
    private string? _updateMsi; // bereits im Hintergrund geladene Installationsdatei
    private readonly System.Windows.Forms.Timer _licenseTimer = new() { Interval = 10 * 60 * 1000 };
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 3 * 60 * 60 * 1000 };
    private readonly Label _versionLabel = new() { AutoSize = true, MaximumSize = new Size(202, 0), ForeColor = Theme.SidebarText, Margin = new Padding(20, 6, 0, 0), UseMnemonic = false };
    private readonly Label _licenseLabel = new() { AutoSize = true, MaximumSize = new Size(202, 0), ForeColor = Theme.SidebarText, Margin = new Padding(20, 4, 0, 10) };
    private readonly StatCard _cardTotal = new("Mitglieder gesamt", Theme.Navy);
    private readonly StatCard _cardKader = new("Im Kader", Theme.Accent);
    private readonly StatCard _cardConfirmed = new("Daten bestätigt", Color.FromArgb(0x10, 0xB9, 0x81));
    private readonly StatCard _cardExpiry = new("Ablauf NADA / Pass", Color.FromArgb(0xEA, 0xB3, 0x08), clickable: true);
    private readonly StatCard _cardMissing = new("Fehlende Dokumente", Theme.Danger, clickable: true);
    private SideNavButton? _navMembers;
    private SideNavButton? _navStaff;
    private Panel? _playersPage;
    private StaffPanel? _staffPanel;
    private bool _staffLoaded;
    private MissingDocsPanel? _missingPanel;
    private RegistrationsPanel? _registrationsPanel;
    private SideNavButton? _navExpiry;
    private SideNavButton? _navMissing;
    private SideNavButton? _navRegistrations;
    private bool _licenseBusy;

    public MainForm(AppSettings settings, ApiClient api, PingResult ping)
    {
        _settings = settings;
        _api = api;
        _ping = ping;

        Text = $"Mitgliederverwaltung U19 – Version {UpdateService.CurrentVersion}";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1360, 800);
        MinimumSize = new Size(1080, 760);

        BuildLayout();
        Shown += async (_, _) =>
        {
            _licenseTimer.Tick += async (_, _) => await CheckLicenseAsync();
            _licenseTimer.Start();
            await CheckLicenseAsync();
            await ReloadAsync();
            if (_ping.CanWrite && _ping.Can("members.edit")) await LoadCampOptionsAsync();
            await CheckForUpdateAsync();
            _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
            _updateTimer.Start();
        };
        FormClosed += (_, _) => _api.Dispose();
    }

    private void BuildLayout()
    {
        BackColor = Theme.Background;

        // ── Filter und Aktionen ───────────────────────────────────────────
        _statusFilter.Items.AddRange(new object[] { "Alle", "Aktiv", "Inaktiv" });
        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        _kaderFilter.Items.AddRange(new object[] { "Alle Spieler", "Im Kader", "Spieler nicht im Kader" });
        _kaderFilter.SelectedIndex = 0;
        _kaderFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        _search.TextChanged += (_, _) => ApplyFilter();
        _search.Width = 230;
        _search.PlaceholderText = "Suche: Name, E-Mail, Verein, Jersey Nr.";
        _search.Margin = new Padding(0, 4, 10, 0);
        _statusFilter.Margin = new Padding(0, 4, 10, 0);
        _kaderFilter.Margin = new Padding(0, 4, 12, 0);

        var add = Theme.MakeButton("+ Neues Mitglied", primary: true);
        var export = Theme.MakeButton("Export CSV");
        var edit = Theme.MakeButton("Bearbeiten");
        var delete = Theme.MakeButton("Auswahl löschen");
        var deleteAll = Theme.MakeButton("Alle löschen …");
        deleteAll.ForeColor = Theme.Danger;
        deleteAll.Enabled = _ping.CanWrite && _ping.Can("members.delete_all");
        var link = Theme.MakeButton("Zugangslink …");
        link.Enabled = _ping.CanWrite && _ping.Can("members.links");
        link.Click += (_, _) => { if (SelectedMember() is { } m) { using var form = new MemberLinkForm(_api, m); form.ShowDialog(this); } };

        var verify = Theme.MakeButton("Bestätigung …");
        verify.Enabled = _ping.CanWrite && _ping.Can("members.links");
        verify.Click += async (_, _) => await VerifyAsync();
        var selectAll = Theme.MakeButton("Alle auswählen");
        var selectNone = Theme.MakeButton("Aufheben");
        selectAll.Click += (_, _) => SetAllChecked(true);
        selectNone.Click += (_, _) => SetAllChecked(false);
        add.Click += async (_, _) => await OpenEditorAsync(null);
        export.Click += async (_, _) => await ExportAsync();
        edit.Click += async (_, _) => { if (SelectedMember() is { } m) await OpenEditorAsync(m); };
        delete.Click += async (_, _) => await DeleteSelectedAsync();
        deleteAll.Click += async (_, _) => await DeleteAllAsync();
        _writeButtons.AddRange(new[] { add, edit, delete });
        add.Enabled = _ping.CanWrite && _ping.Can("members.create");
        delete.Enabled = _ping.CanWrite && _ping.Can("members.delete");
        export.Enabled = _ping.Can("members.export");

        // ── Seitenleiste ──────────────────────────────────────────────────
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 240, BackColor = Theme.Navy };

        var brand = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Navy };
        var mark = new LogoPill { Size = new Size(200, 70), Location = new Point(20, 12) };
        brand.Controls.Add(mark);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0),
            BackColor = Theme.Navy,
        };

        nav.Resize += (_, _) => CompactNav(nav);

        SideNavButton Nav(string text, string glyph, Action click, bool active = false)
        {
            var b = new SideNavButton(text, glyph) { Width = 240, Active = active };
            b.Click += (_, _) => click();
            nav.Controls.Add(b);
            return b;
        }

        _navMembers = Nav("Mitglieder", "", () => ShowPage(false), active: true);
        _navStaff = Nav("Staff", "", () => ShowPage(true));
        _navStaff.Visible = _ping.Can("staff.view");
        _navRegistrations = Nav("Neue Mitglieder", "", ShowRegistrations);
        _navRegistrations.Visible = _ping.Can("members.registrations");
        var import = Nav("Import …", "", async () => await OpenImportAsync());
        import.Visible = _ping.CanWrite && _ping.Can("members.import");
        Nav("Roster", "", () => { using var form = new RosterForm(_api); form.ShowDialog(this); }).Visible = _ping.Can("members.export");
        _navExpiry = Nav("Ablaufdaten", "", ShowExpiry);
        _navMissing = Nav("Dokumente fehlen", "", ShowMissing);
        var signedIn = _ping.User is not null;
        Nav("Camps", "", () => { using var form = new CampsForm(_api, _ping.Can("camps.delete")); form.ShowDialog(this); }).Visible = signedIn && _ping.Can("camps.manage");
        Nav("Feld-Rechte", "", () => { using var form = new FieldPermissionsForm(_api); form.ShowDialog(this); }).Visible = signedIn && _ping.Can("fields.manage");
        Nav("Benutzer & Rechte", "", () => { using var form = new UserAdminForm(_api); form.ShowDialog(this); }).Visible = signedIn && _ping.Can("users.manage");
        Nav("API-Zugänge", "", () => { using var form = new ApiTokensForm(_api); form.ShowDialog(this); }).Visible = signedIn && _ping.Can("api.manage");
        Nav("Protokoll", "", () => { using var form = new LogForm(_api, _ping.Can("logs.purge")); form.ShowDialog(this); }).Visible = signedIn && _ping.Can("logs.view");

        // Unterer Bereich der Seitenleiste
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 6, 0, 10),
            BackColor = Theme.Navy,
        };
        var sep = new Panel { Height = 1, Width = 200, BackColor = Color.FromArgb(0x26, 0x2C, 0x4D), Margin = new Padding(20, 0, 20, 6) };
        bottom.Controls.Add(sep);

        var accountButton = new SideNavButton("Konto", "") { Width = 240, Enabled = signedIn };
        accountButton.Click += (_, _) =>
        {
            if (_ping.User is not { } who) return;
            using var form = new AccountForm(_api, who.Username, who.Role);
            form.ShowDialog(this);
        };
        var settingsButton = new SideNavButton("Einstellungen", "") { Width = 240 };
        settingsButton.Click += (_, _) => OpenSettings();
        bottom.Controls.Add(accountButton);
        bottom.Controls.Add(settingsButton);
        var logoutButton = new SideNavButton("Abmelden", "") { Width = 240, Enabled = signedIn };
        logoutButton.Click += async (_, _) => await LogoutAsync();
        bottom.Controls.Add(logoutButton);

        _updateButton.Visible = false;
        _updateButton.AutoSize = false;
        _updateButton.Size = new Size(190, 38);
        _updateButton.Margin = new Padding(18, 8, 0, 4);
        _updateButton.Click += (_, _) =>
        {
            if (_update is not null)
            {
                using var progressForm = new UpdateProgressForm(_update, _settings.GitHubToken, _updateMsi);
                progressForm.ShowDialog(this);
                return;
            }
            using var form = new UpdateForm(_settings, _update);
            form.ShowDialog(this);
        };
        bottom.Controls.Add(_updateButton);

        var account = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(202, 0),
            ForeColor = Theme.SidebarText,
            Margin = new Padding(20, 6, 0, 0),
            UseMnemonic = false,
            Text = _ping.User is { } who
                ? $"{who.Username}\n{(who.Role == "administrator" ? "Administrator" : "Benutzer")}{(_ping.CanWrite ? "" : " · nur Lesen")}"
                : $"{_ping.TokenName}\n{(_ping.CanWrite ? "Lesen & Schreiben" : "nur Lesen")}",
        };
        bottom.Controls.Add(account);
        bottom.Controls.Add(_licenseLabel);
        bottom.Controls.Add(_versionLabel);
        ShowVersion();

        // Reihenfolge: Fill zuerst, dann Bottom, dann Top
        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(bottom);
        sidebar.Controls.Add(brand);

        // ── Inhalt ────────────────────────────────────────────────────────
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(26, 18, 26, 8) };

        // Titelzeile
        var titleRow = new Panel { Dock = DockStyle.Top, Height = 54 };
        var title = new Label
        {
            Text = "Mitglieder",
            Font = new Font(Theme.Title.FontFamily, 20f * Theme.Zoom, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x17, 0x19, 0x23),
            AutoSize = true,
            Location = new Point(0, 4),
        };
        var titleActions = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        add.Margin = new Padding(0, 0, 0, 0);
        export.Margin = new Padding(0, 0, 10, 0);
        titleActions.Controls.Add(add);
        titleActions.Controls.Add(export);
        var refreshButton = Theme.MakeButton("Aktualisieren");
        refreshButton.Margin = new Padding(0, 0, 10, 0);
        refreshButton.Click += async (_, _) => await ReloadAsync();
        titleActions.Controls.Add(refreshButton);
        titleRow.Controls.Add(title);
        titleRow.Controls.Add(titleActions);

        // Kennzahlen
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 100, ColumnCount = 5, RowCount = 1, Padding = new Padding(0, 6, 0, 10) };
        for (var i = 0; i < 5; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        _cardMissing.Margin = new Padding(0);
        foreach (var card in new[] { _cardTotal, _cardKader, _cardConfirmed, _cardExpiry, _cardMissing })
        {
            card.Dock = DockStyle.Fill;
            cards.Controls.Add(card);
        }
        cards.Resize += (_, _) => { var need = StatCard.PreferredHeight + cards.Padding.Vertical; if (cards.Height < need) cards.Height = need; };
        _cardExpiry.Click += (_, _) => ShowExpiry();
        _cardMissing.Click += (_, _) => ShowMissing();

        // Filterzeile und Aktionen
        var filterRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, Theme.Px(52)), WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
        edit.Margin = new Padding(0, 0, 8, 0);
        link.Margin = new Padding(0, 0, 8, 0);
        verify.Margin = new Padding(0, 0, 8, 0);
        selectAll.Margin = new Padding(0, 0, 4, 0);
        selectNone.Margin = new Padding(0, 0, 8, 0);
        delete.Margin = new Padding(0, 0, 8, 0);
        deleteAll.Margin = new Padding(0, 0, 0, 0);
        filterRow.Controls.AddRange(new Control[] { _search, _statusFilter, _kaderFilter, edit, link, verify, selectAll, selectNone, delete, deleteAll });

        // Camp-Massenzuweisung (wie im Webpanel): Camp + zuweisen/entfernen auf die ausgewählten Mitglieder anwenden
        var campBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 0, 0, 6), Visible = _ping.CanWrite && _ping.Can("members.edit") };
        _campAction.Items.AddRange(new object[] { "zuweisen", "entfernen" });
        _campAction.SelectedIndex = 0;
        _campAssign.Margin = new Padding(0, 0, 8, 0);
        _campAction.Margin = new Padding(0, 0, 8, 0);
        var campApply = Theme.MakeButton("Bei Ausgewählten anwenden");
        campApply.Click += async (_, _) => await ApplyCampAssignAsync();
        campBar.Controls.AddRange(new Control[] { _campAssign, _campAction, campApply });

        // Tabelle in einer Karte mit feinem Rahmen
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Theme.StyleGrid(_grid);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.Columns.Add(Theme.CheckColumn());
        Theme.WireCheckboxCommit(_grid);
        AddColumn("name", "Name", 16);
        AddColumn("verein", "Verein", 13);
        AddColumn("position", "Position", 7);
        AddColumn("jersey", "Nr.", 4);
        AddColumn("email", "E-Mail", 19);
        AddColumn("telefon", "Telefon", 12);
        AddColumn("status", "Status", 7);
        AddColumn("kader", "Kader", 10);
        AddColumn("bestaetigt", "Bestätigt", 9);
        AddColumn("nada", "NADA", 9);
        AddColumn("pass", "Pass", 9);
        _grid.Columns.Add(Theme.LinkColumn());
        // Bei schmalem Fenster weniger wichtige Spalten ausblenden (Reihenfolge: zuerst Telefon, zuletzt E-Mail)
        var wish = new Dictionary<string, int> { ["check"] = 30, ["name"] = 170, ["verein"] = 130, ["position"] = 100, ["jersey"] = 60, ["email"] = 200, ["telefon"] = 130, ["status"] = 80, ["kader"] = 110, ["bestaetigt"] = 120, ["nada"] = 105, ["pass"] = 105 };
        var hideOrder = new[] { "telefon", "kader", "status", "jersey", "position", "pass", "nada", "verein", "bestaetigt", "email" };
        var fitting = false;
        void Refit()
        {
            if (fitting) return;
            fitting = true;
            try { Theme.FitColumns(_grid, hideOrder, wish); }
            finally { fitting = false; }
        }
        _grid.SizeChanged += (_, _) => Refit();
        _grid.HandleCreated += (_, _) => Refit();
        _grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "link" || _grid.Rows[e.RowIndex].Tag is not Member m) return;
            if (!(_ping.CanWrite && _ping.Can("members.links"))) return;
            using var form = new MemberLinkForm(_api, m);
            form.ShowDialog(this);
            _ = ReloadAsync();
        };
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && SelectedMember() is { } m) await OpenEditorAsync(m);
        };
        _grid.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; if (SelectedMember() is { } m) await OpenEditorAsync(m); }
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "check" || _grid.Rows[e.RowIndex].Tag is not Member m) return;
            if ((bool) (_grid.Rows[e.RowIndex].Cells["check"].Value ?? false)) _checkedIds.Add(m.Id);
            else _checkedIds.Remove(m.Id);
            UpdateBulkButtons();
        };

        // Kästchen zum Auswählen (wie im Webpanel): Beschriftung der Aktionen zeigt die Anzahl an
        void UpdateBulkButtons()
        {
            var n = _checkedIds.Count;
            delete.Text = n > 0 ? $"Auswahl löschen ({n})" : "Auswahl löschen";
            delete.Enabled = _ping.CanWrite && _ping.Can("members.delete") && n > 0;
            verify.Text = n > 0 ? $"Bestätigung … ({n})" : "Bestätigung …";
        }
        void SetAllChecked(bool value)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                row.Cells["check"].Value = value;
                if (row.Tag is Member m) { if (value) _checkedIds.Add(m.Id); else _checkedIds.Remove(m.Id); }
            }
            UpdateBulkButtons();
        }
        _updateBulkButtons = UpdateBulkButtons;

        var gridCard = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1) };
        gridCard.Controls.Add(_grid);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Background };
        _footer.Dock = DockStyle.Fill;
        footer.Controls.Add(_footer);

        // Reihenfolge: Fill zuerst, dann Bottom, dann Top (zuletzt hinzugefügt = ganz oben)
        content.Controls.Add(gridCard);
        content.Controls.Add(footer);
        content.Controls.Add(campBar);
        content.Controls.Add(filterRow);
        content.Controls.Add(cards);
        content.Controls.Add(titleRow);

        _playersPage = content;
        if (_ping.Can("staff.view"))
        {
            _staffPanel = new StaffPanel(_api, _ping) { Visible = false };
            Controls.Add(_staffPanel);
        }
        _missingPanel = new MissingDocsPanel(_api) { Visible = false };
        Controls.Add(_missingPanel);
        if (_ping.Can("members.registrations"))
        {
            _registrationsPanel = new RegistrationsPanel(_api, _ping.CanWrite && _ping.Can("members.registrations")) { Visible = false };
            _registrationsPanel.Changed += () =>
            {
                _ = ReloadAsync();
                // Übernahme als Staff wirkt sich auf die Staff-Liste aus; nur neu laden, wenn sie schon geladen wurde
                if (_staffLoaded && _staffPanel is not null) _ = _staffPanel.ReloadAsync();
            };
            Controls.Add(_registrationsPanel);
        }
        Controls.Add(content);
        Controls.Add(sidebar);
    }

    /// <summary>Verkleinert die Menüeinträge, wenn die Fensterhöhe nicht für alle reicht (kein Abschneiden, kein Scrollen).</summary>
    private static void CompactNav(FlowLayoutPanel nav)
    {
        var buttons = nav.Controls.OfType<SideNavButton>().Where(b => b.Visible).ToList();
        if (buttons.Count == 0 || nav.ClientSize.Height <= 0) return;
        var per = (nav.ClientSize.Height - nav.Padding.Vertical) / buttons.Count - 2;
        var height = Math.Clamp(per, Theme.Px(26), Theme.Px(38));
        foreach (var b in buttons)
        {
            if (b.Height != height) b.Height = height;
        }
    }

    /// <summary>Wechselt im Hauptfenster zwischen Spielerliste und Staff (kein eigenes Fenster).</summary>
    private void ShowPage(bool staff) => ShowPage(staff ? "staff" : "players");

    private void ShowPage(string page)
    {
        if (_playersPage is null) return;
        if (page == "staff" && _staffPanel is null) return;
        if (page == "registrations" && _registrationsPanel is null) return;
        _playersPage.Visible = page == "players";
        if (_staffPanel is not null) _staffPanel.Visible = page == "staff";
        if (_missingPanel is not null) _missingPanel.Visible = page == "missing";
        if (_registrationsPanel is not null) _registrationsPanel.Visible = page == "registrations";
        if (_navMembers is not null) _navMembers.Active = page == "players";
        if (_navStaff is not null) _navStaff.Active = page == "staff";
        if (_navMissing is not null) _navMissing.Active = page == "missing";
        if (_navRegistrations is not null) _navRegistrations.Active = page == "registrations";
        _navMembers?.Invalidate();
        _navStaff?.Invalidate();
        _navMissing?.Invalidate();
        _navRegistrations?.Invalidate();
        if (page == "staff" && !_staffLoaded)
        {
            _staffLoaded = true;
            _ = _staffPanel!.ReloadAsync();
        }
        else if (page == "registrations")
        {
            _ = _registrationsPanel!.ReloadAsync();
        }
        else if (page == "players")
        {
            _grid.Focus();
        }
    }

    private void ShowRegistrations() => ShowPage("registrations");

    /// <summary>Zahl der neuen, noch nicht zugewiesenen Anmeldungen (Spieler + Staff) in der Seitenleiste anzeigen (Fehler werden ignoriert).</summary>
    private async Task UpdateRegistrationsBadgeAsync()
    {
        if (_navRegistrations is null || !_ping.Can("members.registrations")) return;
        var count = 0;
        try
        {
            count += (await _api.ListRegistrationsAsync()).Count;
        }
        catch (Exception)
        {
            // Badge ist nur ein Hinweis - beim nächsten Aktualisieren erneut versuchen
        }
        try
        {
            count += (await _api.ListPendingStaffAsync()).Count;
        }
        catch (Exception)
        {
            // z. B. alter Server ohne "registrations/staff" - Spieler-Anzahl trotzdem anzeigen
        }
        _navRegistrations.Badge = count;
        _navRegistrations.Invalidate();
    }

    private void AddColumn(string name, string title, float weight)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = title,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.Automatic,
            ToolTipText = name == "nada" ? "NADA-Zertifikat gültig bis" : name == "pass" ? "Reisepass gültig bis" : "",
            MinimumWidth = Theme.Px(name is "nada" or "pass" ? 98 : name is "jersey" ? 48 : name is "bestaetigt" ? 96 : 70),
            ReadOnly = true,
        });
    }

    private Member? SelectedMember() =>
        _grid.CurrentRow?.Tag as Member;

    private async Task ReloadAsync()
    {
        _footer.Text = "Lade Mitglieder …";
        UseWaitCursor = true;
        try
        {
            _all = await _api.ListAllAsync();
            _checkedIds.IntersectWith(_all.Select(m => m.Id)); // gelöschte/nicht mehr vorhandene Mitglieder aus der Auswahl entfernen
            ApplyFilter();
            UpdateExpiryBanner();
            await UpdateRegistrationsBadgeAsync();
        }
        catch (Exception ex)
        {
            _footer.Text = "Fehler beim Laden.";
            Theme.ShowError(this, ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ApplyFilter()
    {
        var q = _search.Text.Trim();
        var status = _statusFilter.SelectedIndex switch { 1 => "aktiv", 2 => "inaktiv", _ => null };
        var kader = _kaderFilter.SelectedIndex switch { 1 => "kader", 2 => "nicht_im_kader", _ => null };
        var selectedId = SelectedMember()?.Id;

        var filtered = _all.Where(m =>
            (status is null || m.Get("status") == status) &&
            (kader is null || (m.Get("kader") == "nicht_im_kader" ? "nicht_im_kader" : "kader") == kader) &&
            (q.Length == 0 || new[] { "nachname", "vorname", "email", "verein", "jersey_nr" }
                .Any(k => m.Get(k).Contains(q, StringComparison.CurrentCultureIgnoreCase)))).ToList();

        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var m in filtered.OrderBy(m => m.Get("nachname"), StringComparer.CurrentCultureIgnoreCase)
                                  .ThenBy(m => m.Get("vorname"), StringComparer.CurrentCultureIgnoreCase))
        {
            var idx = _grid.Rows.Add(
                _checkedIds.Contains(m.Id), m.FullName, m.Get("verein"), m.Get("position"), m.Get("jersey_nr"), m.Get("email"),
                m.Get("telefon"), m.Get("status") == "inaktiv" ? "Inaktiv" : "Aktiv",
                m.Get("kader") == "nicht_im_kader" ? "nicht im Kader" : "Im Kader",
                m.ConfirmedAt is null ? "ausstehend" : "✓ " + FormatDate(m.ConfirmedAt),
                FormatExpiry(Expiry.Nada(m)), FormatExpiry(Expiry.Pass(m)));
            var row = _grid.Rows[idx];
            row.Tag = m;
            MarkExpiry(row.Cells["nada"], Expiry.Nada(m));
            MarkExpiry(row.Cells["pass"], Expiry.Pass(m));
            if (m.ConfirmedAt is null) row.Cells["bestaetigt"].Style.ForeColor = Theme.AccentDark;
            else row.Cells["bestaetigt"].Style.ForeColor = Theme.Green;
            if (m.Get("status") == "inaktiv") row.DefaultCellStyle.ForeColor = Theme.Muted;
            if (m.Id == selectedId) { row.Selected = true; _grid.CurrentCell = row.Cells["name"]; }
        }
        _grid.ResumeLayout();

        var active = _all.Count(m => m.Get("status") != "inaktiv");
        var confirmed = _all.Count(m => m.ConfirmedAt is not null);
        var inKader = _all.Count(m => m.Get("kader") != "nicht_im_kader");
        _cardTotal.Set(_all.Count.ToString(), $"Mitglieder · {active} aktiv");
        _cardKader.Set(inKader.ToString(), $"Im Kader · {_all.Count - inKader} nicht");
        _cardConfirmed.Set(confirmed.ToString(), $"Bestätigt · {_all.Count - confirmed} offen");
        _footer.Text = $"{filtered.Count} von {_all.Count} angezeigt · Doppelklick zum Bearbeiten";
        _updateBulkButtons?.Invoke();
    }

    private static string FormatDate(string s) =>
        DateTime.TryParse(s, out var d) ? d.ToString("dd.MM.yyyy") : s;

    private async Task OpenEditorAsync(Member? member)
    {
        if (member is null && !(_ping.CanWrite && _ping.Can("members.create"))) return;

        using var form = new MemberForm(_api, member, readOnly: !(_ping.CanWrite && _ping.Can("members.edit")));
        var saved = form.ShowDialog(this) == DialogResult.OK;
        if (saved || form.DocumentsChanged)
        {
            await ReloadAsync();
        }
    }


    // ── Ablauf-Erinnerungen (NADA-Zertifikat, Reisepass) ──────────────────

    private static string FormatExpiry((ExpiryState State, int Days, DateTime? Date) e) =>
        e.Date is { } d ? d.ToString("dd.MM.yyyy") : "";

    private static void MarkExpiry(DataGridViewCell cell, (ExpiryState State, int Days, DateTime? Date) e)
    {
        if (e.State == ExpiryState.Ok) return;
        var (back, fore) = Expiry.Colors(e.State);
        cell.Style.BackColor = back;
        cell.Style.ForeColor = fore;
        cell.ToolTipText = e.Days < 0 ? $"abgelaufen seit {Math.Abs(e.Days)} Tag(en)"
            : e.Days == 0 ? "läuft heute ab"
            : $"läuft in {e.Days} Tag(en) ab";
    }

    private void UpdateExpiryBanner()
    {
        var items = Expiry.Find(_all.Where(m => !StaffPositions.IsStaff(m.Get("position"))));
        var missing = MissingDocuments();
        _showMissing.Visible = missing.Count > 0;
        _showExpiry.Visible = items.Count > 0;

        // Kennzahlen und Zähler in der Seitenleiste
        var expiredCount = items.Count(i => i.State == ExpiryState.Expired);
        var okGreen = Color.FromArgb(0x10, 0xB9, 0x81);
        var warnYellow = Color.FromArgb(0xEA, 0xB3, 0x08);
        _cardExpiry.Set(items.Count.ToString(), expiredCount > 0 ? $"NADA/Pass · {expiredCount} abgelaufen" : "Ablauf NADA / Pass",
            expiredCount > 0 ? Theme.Danger : items.Count > 0 ? warnYellow : okGreen);
        _cardMissing.Set(missing.Count.ToString(), "Spieler ohne Dokumente", missing.Count > 0 ? Theme.Danger : okGreen);
        if (_navExpiry is not null) { _navExpiry.Badge = items.Count; _navExpiry.Invalidate(); }
        if (_navMissing is not null) { _navMissing.Badge = missing.Count; _navMissing.Invalidate(); }
        if (items.Count == 0 && missing.Count == 0)
        {
            _banner.Visible = false;
            return;
        }

        var expired = items.Count(i => i.State == ExpiryState.Expired);
        var parts = new List<string>();
        if (items.Count > 0)
        {
            var nada = items.Count(i => i.Document == "NADA-Zertifikat");
            parts.Add($"{expired} abgelaufen, {items.Count - expired} laufen bald ab (NADA: {nada}, Pass: {items.Count - nada})");
        }
        if (missing.Count > 0)
        {
            parts.Add($"{missing.Count} Spieler mit fehlenden Dokumenten");
        }
        _bannerText.Text = "⚠ " + string.Join("  ·  ", parts);
        _bannerText.ForeColor = expired > 0 || missing.Count > 0 ? Theme.Danger : Theme.AccentDark;
        _banner.Visible = true;

        // Beim Start einmal aktiv melden
        if (!_expiryAnnounced)
        {
            _expiryAnnounced = true;
            var text = new System.Text.StringBuilder();
            if (items.Count > 0)
            {
                var lines = items.Take(10).Select(i => $"• {i.Member.FullName} – {i.Document}: {i.Date:dd.MM.yyyy} ({i.Describe()})");
                text.AppendLine("Ablaufende Dokumente:").AppendLine().AppendLine(string.Join("\n", lines));
                if (items.Count > 10) text.AppendLine($"… und {items.Count - 10} weitere");
                text.AppendLine();
            }
            if (missing.Count > 0)
            {
                var lines = missing.Take(10).Select(m => $"• {m.FullName} – fehlt: {string.Join(", ", m.MissingDocuments.Select(Member.DocumentLabel))}");
                text.AppendLine("Fehlende Dokumente (Spieler im Kader):").AppendLine().AppendLine(string.Join("\n", lines));
                if (missing.Count > 10) text.AppendLine($"… und {missing.Count - 10} weitere (Button „Fehlende Dokumente“)");
            }
            MessageBox.Show(this, text.ToString().TrimEnd(), "Erinnerung",
                MessageBoxButtons.OK, expired > 0 || missing.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
    }

    /// <summary>Aktive Spieler im Kader, bei denen NADA, Reisepass, E-Card oder Rechte &amp; Pflichten fehlen.</summary>
    private List<Member> MissingDocuments() => _all
        .Where(m => m.Get("status") != "inaktiv" && m.Get("kader") != "nicht_im_kader" && m.MissingDocuments.Count > 0 && !StaffPositions.IsStaff(m.Get("position")))
        .OrderBy(m => m.FullName, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>Staff für die Dokumenten-Fenster (ohne Recht „Staff ansehen“ oder bei Fehlern leer).</summary>
    private async Task<IReadOnlyList<Dictionary<string, string?>>> LoadStaffAsync()
    {
        if (!_ping.Can("staff.view")) return Array.Empty<Dictionary<string, string?>>();
        try
        {
            return await _api.ListStaffAsync();
        }
        catch (Exception)
        {
            return Array.Empty<Dictionary<string, string?>>();
        }
    }

    private async void ShowExpiry()
    {
        var staff = await LoadStaffAsync();
        using var form = new ExpiryForm(_api, Expiry.Find(_all.Where(m => !StaffPositions.IsStaff(m.Get("position")))), staff);
        form.ShowDialog(this);
    }

    private async void ShowMissing()
    {
        var staff = await LoadStaffAsync();
        if (_missingPanel is null) return;
        _missingPanel.Load(MissingDocuments(), staff);
        ShowPage("missing");
    }

    private static VerifyPerson ToVerifyPerson(Member m) =>
        new(m.Id, m.FullName, m.Get("status") != "inaktiv", m.Get("email").Length > 0, m.ConfirmedAt is not null);

    /// <summary>Bestätigung zurücksetzen und/oder Massenmail zur Datenprüfung (Auswahl oder alle Spieler).</summary>
    private async Task VerifyAsync()
    {
        using var form = new VerificationForm(_api, "members", _all.Select(ToVerifyPerson).ToList(), SelectedMembers().Select(ToVerifyPerson).ToList());
        form.ShowDialog(this);
        if (form.Changed) await ReloadAsync();
    }

    /// <summary>Über die Kästchen ausgewählte Mitglieder (wie im Webpanel), nicht die reine Zeilenmarkierung.</summary>
    private List<Member> SelectedMembers() =>
        _all.Where(m => _checkedIds.Contains(m.Id)).ToList();

    /// <summary>Lädt die Camp-Auswahlliste (fest + weitere) für die Massenzuweisung.</summary>
    private async Task LoadCampOptionsAsync()
    {
        try
        {
            _campOptions = await _api.GetCampOptionsAsync();
            _campAssign.Items.Clear();
            _campAssign.Items.AddRange(_campOptions.Select(o => (object) o.Label).ToArray());
            if (_campOptions.Count > 0) _campAssign.SelectedIndex = 0;
        }
        catch (Exception)
        {
            // Massenzuweisung ist nur ein Zusatz - bei Fehlern bleibt die Liste leer, der Rest funktioniert normal
        }
    }

    /// <summary>Weist das gewählte Camp allen über die Kästchen ausgewählten Mitgliedern zu bzw. entfernt es.</summary>
    private async Task ApplyCampAssignAsync()
    {
        var selected = SelectedMembers();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Bitte zuerst über die Kästchen Mitglieder auswählen.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_campAssign.SelectedIndex < 0 || _campAssign.SelectedIndex >= _campOptions.Count)
        {
            MessageBox.Show(this, "Bitte ein Camp auswählen.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var (value, label) = _campOptions[_campAssign.SelectedIndex];
        var add = _campAction.SelectedIndex != 1;
        try
        {
            UseWaitCursor = true;
            var changed = await _api.AssignCampAsync("members", selected.Select(m => m.Id), value, add);
            _footer.Text = $"„{label}“ bei {changed} Mitglied(ern) {(add ? "zugewiesen" : "entfernt")}.";
            await ReloadAsync();
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

    private async Task DeleteSelectedAsync()
    {
        var selected = SelectedMembers();
        if (selected.Count == 0) return;

        var text = selected.Count == 1
            ? $"Mitglied „{selected[0].FullName}“ wirklich unwiderruflich löschen?"
            : $"{selected.Count} ausgewählte Mitglieder wirklich unwiderruflich löschen?\n\n" +
              string.Join("\n", selected.Take(8).Select(m => "• " + m.FullName)) +
              (selected.Count > 8 ? $"\n… und {selected.Count - 8} weitere" : "");
        var answer = MessageBox.Show(this, text, "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        try
        {
            UseWaitCursor = true;
            var deleted = await _api.DeleteManyAsync(selected.Select(m => m.Id));
            _footer.Text = $"{deleted} Mitglied(er) gelöscht.";
            await ReloadAsync();
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

    private async Task DeleteAllAsync()
    {
        const string phrase = "ALLE LÖSCHEN";
        using var dialog = new Form
        {
            Text = "Alle Daten löschen",
            Font = Theme.Body,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(480, 230),
        };
        var warning = new Label
        {
            AutoSize = false,
            Location = new Point(18, 16),
            Size = new Size(444, 100),
            ForeColor = Theme.Danger,
            Text = $"ACHTUNG: Es werden ALLE {_all.Count} Mitglieder mit allen Angaben und allen hochgeladenen Dokumenten endgültig gelöscht. " +
                   "Das kann nicht rückgängig gemacht werden.\n\nTipp: Vorher über „Export CSV“ eine Sicherung speichern.",
        };
        var prompt = new Label { AutoSize = true, Location = new Point(18, 124), Text = $"Zur Bestätigung „{phrase}“ eingeben:" };
        var input = new TextBox { Location = new Point(18, 148), Width = 444 };
        var ok = Theme.MakeButton("Alles löschen");
        ok.BackColor = Theme.Danger;
        ok.ForeColor = Color.White;
        ok.Enabled = false;
        ok.Location = new Point(18, 184);
        var cancel = Theme.MakeButton("Abbrechen");
        cancel.Location = new Point(150, 184);
        input.TextChanged += (_, _) => ok.Enabled = input.Text.Trim() == phrase;
        ok.Click += (_, _) => dialog.DialogResult = DialogResult.OK;
        cancel.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;
        dialog.CancelButton = cancel;
        dialog.Controls.AddRange(new Control[] { warning, prompt, input, ok, cancel });

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            UseWaitCursor = true;
            var deleted = await _api.DeleteAllAsync();
            _footer.Text = $"Alle Daten gelöscht ({deleted}).";
            await ReloadAsync();
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

    private async Task OpenImportAsync()
    {
        using var form = new ImportForm(_api);
        form.ShowDialog(this);
        if (form.Changed) await ReloadAsync();
    }

    private async Task ExportAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV (Excel)|*.csv",
            FileName = $"mitglieder-{DateTime.Now:yyyy-MM-dd}.csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var status = _statusFilter.SelectedIndex switch { 1 => "aktiv", 2 => "inaktiv", _ => null };
            await File.WriteAllBytesAsync(dialog.FileName, await _api.DownloadCsvAsync(status, template: false));
            _footer.Text = "Export gespeichert: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    /// <summary>
    /// Prüft die Lizenz beim Server (beim Start und danach alle 10 Minuten). Ohne Verbindung läuft die Anwendung mit der
    /// signierten Offline-Freigabe höchstens 3 Tage weiter; danach oder bei gesperrter Lizenz ist die Anwendung gesperrt.
    /// </summary>
    private async Task CheckLicenseAsync()
    {
        if (_licenseBusy) return;
        _licenseBusy = true;
        try
        {
            var result = await LicenseService.CheckAsync(_settings, _api);
            switch (result.Status)
            {
                case LicenseStatus.Valid:
                    _licenseLabel.Text = "🔑 Lizenz aktiv" + (string.IsNullOrEmpty(_settings.LicenseName) ? "" : " – " + _settings.LicenseName);
                    _licenseLabel.ForeColor = Theme.GreenLight;
                    break;
                case LicenseStatus.OfflineGrace:
                    _licenseLabel.Text = "⚠ Lizenzserver offline – " + result.Message;
                    _licenseLabel.ForeColor = Theme.AccentLight;
                    break;
                default:
                    _licenseTimer.Stop();
                    _licenseLabel.Text = "Lizenz nicht gültig";
                    _licenseLabel.ForeColor = Theme.DangerLight;
                    using (var form = new LicenseForm(_settings, _api, result.Message, mustActivate: true))
                    {
                        if (form.ShowDialog(this) == DialogResult.OK)
                        {
                            _licenseLabel.Text = "🔑 Lizenz aktiv";
                            _licenseLabel.ForeColor = Theme.GreenLight;
                            _licenseTimer.Start();
                        }
                        else
                        {
                            Environment.Exit(0); // ohne gültige Lizenz keine Nutzung
                        }
                    }
                    break;
            }
        }
        catch (Exception)
        {
            // unerwarteter Fehler: beim nächsten Durchlauf erneut versuchen
        }
        finally
        {
            _licenseBusy = false;
        }
    }

    /// <summary>Sucht beim Start still im Hintergrund nach einer neuen Version und blendet bei Erfolg einen Button ein.</summary>
    /// <summary>
    /// Zeigt in der Seitenleiste die installierte und die neueste Version: grün = aktuell, orange = Update verfügbar,
    /// grau = Prüfung nicht möglich. So sieht man immer, ob die Anwendung auf dem neuesten Stand ist.
    /// </summary>
    private void ShowVersion(string? checkNote = null)
    {
        var installed = UpdateService.CurrentVersion;
        var latest = UpdateService.LatestVersion;
        var text = $"Version {installed} (installiert)";
        var color = Theme.SidebarText;
        if (latest is not null)
        {
            if (latest > installed)
            {
                text += $"\nNeueste: {latest} – Update verfügbar";
                color = Theme.AccentLight;
            }
            else
            {
                text += $"\nNeueste: {latest} ✓ aktuell";
                color = Theme.GreenLight;
            }
        }
        else
        {
            text += "\n" + (checkNote ?? "Neueste: wird geprüft …");
        }
        _versionLabel.Text = text;
        _versionLabel.ForeColor = color;
    }

    private async Task CheckForUpdateAsync()
    {
        if (!_settings.AutoCheckUpdates || string.IsNullOrWhiteSpace(_settings.GitHubRepo))
        {
            ShowVersion("Neueste: Prüfung ausgeschaltet");
            return;
        }
        try
        {
            var info = await UpdateService.CheckAsync(_settings.GitHubRepo, _settings.GitHubToken);
            ShowVersion();
            if (info is null) return;
            var isNew = _update is null || _update.Version != info.Version;
            _update = info;
            _updateButton.Text = $"⬆ Update {info.Version} verfügbar";
            _updateButton.Visible = true;
            _footer.Text = $"Neue Version {info.Version} verfügbar – Button „Update“ in der Werkzeugleiste oder Einstellungen › Updates.";
            if (isNew) _ = PreloadUpdateAsync(info);
        }
        catch (Exception)
        {
            // Keine Internetverbindung o. Ä.: beim Start nicht stören
            ShowVersion("Neueste: Prüfung nicht möglich");
        }
    }

    /// <summary>Lädt die neue Version schon im Hintergrund herunter, damit die Installation danach sofort und ohne Rückfrage läuft.</summary>
    private async Task PreloadUpdateAsync(UpdateInfo info)
    {
        try
        {
            _updateMsi = await UpdateService.DownloadAsync(info, _settings.GitHubToken);
            _updateButton.Text = $"⬆ Update {info.Version} installieren";
        }
        catch (Exception)
        {
            _updateMsi = null; // Die Installation lädt dann selbst herunter (Update-Fenster)
        }
    }

    /// <summary>Meldet den Benutzer ab (auch auf dem Server) und startet die Anwendung mit dem Anmeldefenster neu.</summary>
    private async Task LogoutAsync()
    {
        if (MessageBox.Show(this, "Jetzt abmelden? Die Anwendung wird danach neu gestartet.", "Abmelden", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await _api.LogoutAsync();
        _settings.SessionToken = "";
        _settings.Save();
        Program.RestartApp();
        Environment.Exit(0);
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings, firstRun: false);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            Program.RestartApp();
            Environment.Exit(0);
        }
    }
}
