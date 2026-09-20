using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly PingResult _ping;

    private readonly TextBox _search = new() { Width = 240, PlaceholderText = "Suche: Name, E-Mail, Verein, Jersey Nr." };
    private readonly ComboBox _statusFilter = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _kaderFilter = new() { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _grid = new();
    private readonly Label _stats = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 8, 0, 0) };
    private readonly Label _footer = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<Button> _writeButtons = new();

    private List<Member> _all = new();
    private DeployForm? _deployForm;
    private readonly Panel _banner = new() { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(0xFF, 0xF2, 0xE6), Visible = false };
    private readonly Label _bannerText = new() { AutoSize = true, Location = new Point(16, 11), Font = Theme.Bold };
    private readonly Button _showExpiry = Theme.MakeButton("Ablauf anzeigen");
    private readonly Button _showMissing = Theme.MakeButton("Fehlende Dokumente");
    private bool _expiryAnnounced;
    private readonly Button _updateButton = Theme.MakeButton("", primary: true);
    private UpdateInfo? _update;
    private readonly System.Windows.Forms.Timer _licenseTimer = new() { Interval = 10 * 60 * 1000 };
    private readonly Label _licenseLabel = new() { AutoSize = true, MaximumSize = new Size(186, 0), ForeColor = Theme.SidebarText, Margin = new Padding(20, 6, 0, 0) };
    private readonly StatCard _cardTotal = new("Mitglieder gesamt", Theme.Navy);
    private readonly StatCard _cardKader = new("Im Kader", Theme.Accent);
    private readonly StatCard _cardConfirmed = new("Daten bestätigt", Color.FromArgb(0x10, 0xB9, 0x81));
    private readonly StatCard _cardExpiry = new("Ablauf NADA / Pass", Color.FromArgb(0xEA, 0xB3, 0x08), clickable: true);
    private readonly StatCard _cardMissing = new("Fehlende Dokumente", Theme.Danger, clickable: true);
    private SideNavButton? _navExpiry;
    private SideNavButton? _navMissing;
    private bool _licenseBusy;

    public MainForm(AppSettings settings, ApiClient api, PingResult ping)
    {
        _settings = settings;
        _api = api;
        _ping = ping;

        Text = "Mitgliederverwaltung U19";
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1360, 800);
        MinimumSize = new Size(1080, 600);

        BuildLayout();
        Shown += async (_, _) =>
        {
            _licenseTimer.Tick += async (_, _) => await CheckLicenseAsync();
            _licenseTimer.Start();
            await CheckLicenseAsync();
            await ReloadAsync();
            await CheckForUpdateAsync();
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
        _search.Width = 300;
        _search.PlaceholderText = "Suche: Name, E-Mail, Verein, Jersey Nr.";
        _search.Margin = new Padding(0, 4, 10, 0);
        _statusFilter.Margin = new Padding(0, 4, 10, 0);
        _kaderFilter.Margin = new Padding(0, 4, 18, 0);

        var add = Theme.MakeButton("+ Neues Mitglied", primary: true);
        var export = Theme.MakeButton("Export CSV");
        var edit = Theme.MakeButton("Bearbeiten");
        var delete = Theme.MakeButton("Auswahl löschen");
        var deleteAll = Theme.MakeButton("Alle löschen …");
        deleteAll.ForeColor = Theme.Danger;
        deleteAll.Enabled = _ping.CanWrite;

        add.Click += async (_, _) => await OpenEditorAsync(null);
        export.Click += async (_, _) => await ExportAsync();
        edit.Click += async (_, _) => { if (SelectedMember() is { } m) await OpenEditorAsync(m); };
        delete.Click += async (_, _) => await DeleteSelectedAsync();
        deleteAll.Click += async (_, _) => await DeleteAllAsync();
        _writeButtons.AddRange(new[] { add, edit, delete });

        // ── Seitenleiste ──────────────────────────────────────────────────
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 224, BackColor = Theme.Navy };

        var brand = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Navy };
        var mark = new Label
        {
            Text = "U19",
            Font = new Font(Theme.Bold.FontFamily, 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x1A, 0x0D, 0x00),
            BackColor = Theme.Accent,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(46, 46),
            Location = new Point(20, 20),
        };
        var brandText = new Label
        {
            Text = "AFBÖ\nMitgliederverwaltung",
            Font = Theme.Bold,
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(76, 24),
        };
        brand.Controls.AddRange(new Control[] { mark, brandText });

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0),
            BackColor = Theme.Navy,
        };

        SideNavButton Nav(string text, string glyph, Action click, bool active = false)
        {
            var b = new SideNavButton(text, glyph) { Width = 224, Active = active };
            b.Click += (_, _) => click();
            nav.Controls.Add(b);
            return b;
        }

        Nav("Mitglieder", "", () => _grid.Focus(), active: true);
        Nav("Staff", "", () => { using var form = new StaffForm(_api, _ping.CanWrite); form.ShowDialog(this); });
        var import = Nav("Import …", "", async () => await OpenImportAsync());
        import.Enabled = _ping.CanWrite;
        Nav("Roster", "", () => { using var form = new RosterForm(_api); form.ShowDialog(this); });
        _navExpiry = Nav("Ablaufdaten", "", ShowExpiry);
        _navMissing = Nav("Dokumente fehlen", "", ShowMissing);
        Nav("Aktualisieren", "", async () => await ReloadAsync());

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
        var sep = new Panel { Height = 1, Width = 184, BackColor = Color.FromArgb(0x26, 0x2C, 0x4D), Margin = new Padding(20, 0, 20, 6) };
        bottom.Controls.Add(sep);

        var deploy = new SideNavButton("Änderungen einspielen", "") { Width = 236, Enabled = _ping.CanWrite };
        deploy.Click += (_, _) =>
        {
            // Nicht-modal: Das Fenster darf für die Automatik geöffnet bleiben, während die App weiter benutzt wird
            if (_deployForm is null || _deployForm.IsDisposed)
            {
                _deployForm = new DeployForm(_settings, _api);
                _deployForm.Show(this);
            }
            else
            {
                _deployForm.Activate();
            }
        };
        var settingsButton = new SideNavButton("Einstellungen", "") { Width = 236 };
        settingsButton.Click += (_, _) => OpenSettings();
        bottom.Controls.Add(deploy);
        bottom.Controls.Add(settingsButton);

        _updateButton.Visible = false;
        _updateButton.AutoSize = false;
        _updateButton.Size = new Size(190, 38);
        _updateButton.Margin = new Padding(18, 8, 0, 4);
        _updateButton.Click += (_, _) =>
        {
            using var form = new UpdateForm(_settings, _update);
            form.ShowDialog(this);
        };
        bottom.Controls.Add(_updateButton);

        var account = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(186, 0),
            ForeColor = Theme.SidebarText,
            Margin = new Padding(20, 6, 0, 0),
            UseMnemonic = false,
            Text = $"{_ping.TokenName}\n{(_ping.CanWrite ? "Lesen & Schreiben" : "nur Lesen")}",
        };
        bottom.Controls.Add(account);
        bottom.Controls.Add(_licenseLabel);

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
            Font = new Font(Theme.Title.FontFamily, 20f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x17, 0x19, 0x23),
            AutoSize = true,
            Location = new Point(0, 4),
        };
        var titleActions = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        add.Margin = new Padding(0, 0, 0, 0);
        export.Margin = new Padding(0, 0, 10, 0);
        titleActions.Controls.Add(add);
        titleActions.Controls.Add(export);
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
        _cardExpiry.Click += (_, _) => ShowExpiry();
        _cardMissing.Click += (_, _) => ShowMissing();

        // Filterzeile und Aktionen
        var filterRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        edit.Margin = new Padding(0, 0, 8, 0);
        delete.Margin = new Padding(0, 0, 8, 0);
        deleteAll.Margin = new Padding(0, 0, 0, 0);
        filterRow.Controls.AddRange(new Control[] { _search, _statusFilter, _kaderFilter, edit, delete, deleteAll });

        // Tabelle in einer Karte mit feinem Rahmen
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Theme.StyleGrid(_grid);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

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
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && SelectedMember() is { } m) await OpenEditorAsync(m);
        };
        _grid.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; if (SelectedMember() is { } m) await OpenEditorAsync(m); }
        };

        var gridCard = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1) };
        gridCard.Controls.Add(_grid);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Background };
        _footer.Dock = DockStyle.Fill;
        footer.Controls.Add(_footer);

        // Reihenfolge: Fill zuerst, dann Bottom, dann Top (zuletzt hinzugefügt = ganz oben)
        content.Controls.Add(gridCard);
        content.Controls.Add(footer);
        content.Controls.Add(filterRow);
        content.Controls.Add(cards);
        content.Controls.Add(titleRow);

        Controls.Add(content);
        Controls.Add(sidebar);
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
            MinimumWidth = name is "nada" or "pass" ? 98 : name is "jersey" ? 48 : name is "bestaetigt" ? 96 : 70,
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
            ApplyFilter();
            UpdateExpiryBanner();
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
                m.FullName, m.Get("verein"), m.Get("position"), m.Get("jersey_nr"), m.Get("email"),
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
            if (m.Id == selectedId) { row.Selected = true; _grid.CurrentCell = row.Cells[0]; }
        }
        _grid.ResumeLayout();

        var active = _all.Count(m => m.Get("status") != "inaktiv");
        var confirmed = _all.Count(m => m.ConfirmedAt is not null);
        var inKader = _all.Count(m => m.Get("kader") != "nicht_im_kader");
        _cardTotal.Set(_all.Count.ToString(), $"Mitglieder · {active} aktiv");
        _cardKader.Set(inKader.ToString(), $"Im Kader · {_all.Count - inKader} nicht");
        _cardConfirmed.Set(confirmed.ToString(), $"Bestätigt · {_all.Count - confirmed} offen");
        _footer.Text = $"{filtered.Count} von {_all.Count} angezeigt · Doppelklick zum Bearbeiten";
    }

    private static string FormatDate(string s) =>
        DateTime.TryParse(s, out var d) ? d.ToString("dd.MM.yyyy") : s;

    private async Task OpenEditorAsync(Member? member)
    {
        if (member is null && !_ping.CanWrite) return;

        using var form = new MemberForm(_api, member, readOnly: !_ping.CanWrite);
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
        var items = Expiry.Find(_all);
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
        .Where(m => m.Get("status") != "inaktiv" && m.Get("kader") != "nicht_im_kader" && m.MissingDocuments.Count > 0)
        .OrderBy(m => m.FullName, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private void ShowExpiry()
    {
        using var form = new ExpiryForm(Expiry.Find(_all));
        form.ShowDialog(this);
    }

    private void ShowMissing()
    {
        using var form = new MissingDocsForm(MissingDocuments());
        form.ShowDialog(this);
    }
    private List<Member> SelectedMembers() =>
        _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Member>().ToList();

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
    private async Task CheckForUpdateAsync()
    {
        if (!_settings.AutoCheckUpdates || string.IsNullOrWhiteSpace(_settings.GitHubRepo)) return;
        try
        {
            var info = await UpdateService.CheckAsync(_settings.GitHubRepo, _settings.GitHubToken);
            if (info is null) return;
            _update = info;
            _updateButton.Text = $"⬆ Update {info.Version} verfügbar";
            _updateButton.Visible = true;
            _footer.Text = $"Neue Version {info.Version} verfügbar – Button „Update“ in der Werkzeugleiste oder Einstellungen › Updates.";
        }
        catch (Exception)
        {
            // Keine Internetverbindung o. Ä.: beim Start nicht stören
        }
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings, firstRun: false);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            Application.Restart();
            Environment.Exit(0);
        }
    }
}
