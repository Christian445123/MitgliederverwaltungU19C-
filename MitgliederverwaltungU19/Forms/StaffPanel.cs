using System.Text.Json.Nodes;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>
/// Staff (Coaches/Betreuer) direkt im Hauptfenster, im gleichen Aufbau wie die Spielerliste:
/// Titel, Kennzahlen, Suche/Filter mit Aktionen, Tabelle mit „Link senden“ pro Zeile.
/// </summary>
public sealed class StaffPanel : UserControl
{
    /// <summary>Felder des Staffs (Schlüssel wie in der API, siehe includes/staff.php).</summary>
    internal static readonly (string Key, string Label, bool Date, bool Multiline, bool Required)[] StaffFields =
    {
        ("nachname", "Nachname", false, false, true),
        ("vorname", "Vorname", false, false, true),
        ("position", "Position (z. B. HC, OC, DC, TM)", false, false, false),
        ("nada", "Nada (Ja/Nein)", false, false, false),
        ("geburtsdatum", "Geburtsdatum", true, false, false),
        ("telefon", "Telefon", false, false, false),
        ("email", "Mail", false, false, false),
        ("telefon_angehoeriger", "Telefonnummer Angehörige", false, false, false),
        ("sozialversicherungsnummer", "Sozial Ver. Nr.", false, false, false),
        ("reisepass_nr", "Reisepass Nr", false, false, false),
        ("reisepass_ausgestellt_am", "Reisepass ausgestellt am", true, false, false),
        ("reisepass_gueltig_bis", "Reisepass gültig bis", true, false, false),
        ("geburtsland", "Geburtsland", false, false, false),
        ("ausstellungsbehoerde", "Ausstellungsbehörde", false, false, false),
        ("plz", "PLZ", false, false, false),
        ("ort", "Ort", false, false, false),
        ("strasse", "Straße", false, false, false),
        ("kontoinhaber", "Kontoinhaber", false, false, false),
        ("iban", "IBAN", false, false, false),
        ("bic", "BIC", false, false, false),
        ("essen", "Essen", false, true, false),
        ("tshirt_polo_groesse", "T-Shirt / Polo Größe", false, false, false),
        ("hoodie_groesse", "Hoodie Größe", false, false, false),
        ("jacken_groesse", "Jacken Größe", false, false, false),
        ("short_groesse", "Short Größe", false, false, false),
        ("shorts_anzahl", "Wie viele Shorts besitzt du?", false, false, false),
        ("coaching_hosen_lang_groesse", "Coaching Hosen (lang) Größe", false, false, false),
    };

    private readonly ApiClient _api;
    private readonly bool _canWrite;
    private readonly bool _canDelete;
    private readonly bool _canExport;
    private readonly TextBox _search = new() { Width = 260, PlaceholderText = "Suche: Name, Position, E-Mail" };
    private readonly ComboBox _statusFilter = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _grid = new();
    private readonly Label _footer = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly StatCard _cardTotal = new("Staff gesamt", Theme.Navy);
    private readonly StatCard _cardConfirmed = new("Daten bestätigt", Color.FromArgb(0x10, 0xB9, 0x81));
    private readonly StatCard _cardExpiry = new("Ablauf Reisepass", Color.FromArgb(0xEA, 0xB3, 0x08));
    private List<Dictionary<string, string?>> _all = new();
    private readonly HashSet<int> _checkedIds = new();
    private Action? _updateBulkButtons;
    private readonly ComboBox _campAssign = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _campAction = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private List<(string Value, string Label)> _campOptions = new();
    private bool _campOptionsLoaded;

    public StaffPanel(ApiClient api, PingResult ping)
    {
        _api = api;
        _canWrite = ping.CanWrite && ping.Can("staff.edit");
        _canDelete = ping.CanWrite && ping.Can("staff.delete");
        _canExport = ping.Can("members.export");
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        Padding = new Padding(26, 18, 26, 8);
        Font = Theme.Body;

        // Titelzeile
        var titleRow = new Panel { Dock = DockStyle.Top, Height = 54 };
        var title = new Label
        {
            Text = "Staff",
            Font = new Font(Theme.Title.FontFamily, 20f * Theme.Zoom, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x17, 0x19, 0x23),
            AutoSize = true,
            Location = new Point(0, 4),
        };
        var add = Theme.MakeButton("+ Neue Person", primary: true);
        var export = Theme.MakeButton("Export CSV");
        var refresh = Theme.MakeButton("Aktualisieren");
        add.Margin = new Padding(0);
        export.Margin = new Padding(0, 0, 10, 0);
        refresh.Margin = new Padding(0, 0, 10, 0);
        add.Enabled = _canWrite;
        export.Enabled = _canExport;
        var titleActions = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        titleActions.Controls.AddRange(new Control[] { add, export, refresh });
        titleRow.Controls.Add(title);
        titleRow.Controls.Add(titleActions);

        // Kennzahlen (gleiche Breite wie bei den Spielern: fünf Spalten)
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 100, ColumnCount = 5, RowCount = 1, Padding = new Padding(0, 6, 0, 10) };
        for (var i = 0; i < 5; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        foreach (var card in new[] { _cardTotal, _cardConfirmed, _cardExpiry })
        {
            card.Dock = DockStyle.Fill;
            cards.Controls.Add(card);
        }

        cards.Resize += (_, _) => { var need = StatCard.PreferredHeight + cards.Padding.Vertical; if (cards.Height < need) cards.Height = need; };

        // Filter und Aktionen
        _statusFilter.Items.AddRange(new object[] { "Alle", "Aktiv", "Inaktiv" });
        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        _search.TextChanged += (_, _) => ApplyFilter();
        _search.Margin = new Padding(0, 4, 10, 0);
        _statusFilter.Margin = new Padding(0, 4, 12, 0);
        var edit = Theme.MakeButton("Bearbeiten");
        var link = Theme.MakeButton("Zugangslink …");
        var verify = Theme.MakeButton("Bestätigung …");
        var selectAll = Theme.MakeButton("Alle auswählen");
        var selectNone = Theme.MakeButton("Aufheben");
        var delete = Theme.MakeButton("Auswahl löschen");
        edit.Enabled = _canWrite;
        link.Enabled = verify.Enabled = _canWrite;
        delete.Enabled = _canDelete;
        selectAll.Margin = new Padding(0, 0, 4, 0);
        selectNone.Margin = new Padding(0, 0, 8, 0);
        selectAll.Click += (_, _) => SetAllChecked(true);
        selectNone.Click += (_, _) => SetAllChecked(false);
        var filterRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, Theme.Px(52)), WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
        filterRow.Controls.AddRange(new Control[] { _search, _statusFilter, edit, link, verify, selectAll, selectNone, delete });

        // Camp-Massenzuweisung (wie im Webpanel): Camp + zuweisen/entfernen auf die ausgewählten Personen anwenden
        var campBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 0, 0, 6), Visible = _canWrite };
        _campAction.Items.AddRange(new object[] { "zuweisen", "entfernen" });
        _campAction.SelectedIndex = 0;
        _campAssign.Margin = new Padding(0, 0, 8, 0);
        _campAction.Margin = new Padding(0, 0, 8, 0);
        var campApply = Theme.MakeButton("Bei Ausgewählten anwenden");
        campApply.Click += async (_, _) => await ApplyCampAssignAsync();
        campBar.Controls.AddRange(new Control[] { _campAssign, _campAction, campApply });

        // Tabelle in einer Karte
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoGenerateColumns = false;
        Theme.StyleGrid(_grid);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(Theme.CheckColumn());
        Theme.WireCheckboxCommit(_grid);
        foreach (var (key, label, weight) in new[]
                 {
                     ("name", "Name & Vorname", 18f), ("position", "Position", 9f), ("nada", "Nada", 6f),
                     ("telefon", "Telefon", 12f), ("email", "E-Mail", 19f), ("reisepass_gueltig_bis", "Pass", 9f),
                     ("status", "Status", 7f), ("bestaetigt", "Bestätigt", 9f),
                 })
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = key,
                HeaderText = label,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.Automatic,
                MinimumWidth = key == "reisepass_gueltig_bis" ? Theme.Px(98) : key == "bestaetigt" ? Theme.Px(96) : Theme.Px(70),
                ToolTipText = key == "reisepass_gueltig_bis" ? "Reisepass gültig bis" : "",
                ReadOnly = true,
            });
        }
        _grid.Columns.Add(Theme.LinkColumn());
        var wish = new Dictionary<string, int> { ["check"] = 30, ["name"] = 190, ["position"] = 110, ["nada"] = 70, ["telefon"] = 130, ["email"] = 210, ["reisepass_gueltig_bis"] = 105, ["status"] = 80, ["bestaetigt"] = 120 };
        var hideOrder = new[] { "telefon", "status", "nada", "position", "reisepass_gueltig_bis", "bestaetigt", "email" };
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

        var gridCard = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1) };
        gridCard.Controls.Add(_grid);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Background };
        footer.Controls.Add(_footer);

        Controls.Add(gridCard);
        Controls.Add(footer);
        Controls.Add(campBar);
        Controls.Add(filterRow);
        Controls.Add(cards);
        Controls.Add(titleRow);

        refresh.Click += async (_, _) => await ReloadAsync();
        add.Click += async (_, _) => await OpenEditorAsync(null);
        edit.Click += async (_, _) => { if (Current() is { } row) await OpenEditorAsync(row); };
        delete.Click += async (_, _) => await DeleteAsync();
        link.Click += (_, _) => { if (Current() is { } row) OpenLink(row); };
        verify.Click += async (_, _) => await VerifyAsync();
        export.Click += async (_, _) => await ExportAsync();
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (_canWrite && e.RowIndex >= 0 && Current() is { } row) await OpenEditorAsync(row);
        };
        _grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "link" || !_canWrite) return;
            if (_grid.Rows[e.RowIndex].Tag is Dictionary<string, string?> row) OpenLink(row);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "check" || _grid.Rows[e.RowIndex].Tag is not Dictionary<string, string?> row) return;
            if ((bool) (_grid.Rows[e.RowIndex].Cells["check"].Value ?? false)) _checkedIds.Add(IdOf(row));
            else _checkedIds.Remove(IdOf(row));
            UpdateBulkButtons();
        };

        // Kästchen zum Auswählen (wie im Webpanel): Beschriftung der Aktionen zeigt die Anzahl an
        void UpdateBulkButtons()
        {
            var n = _checkedIds.Count;
            delete.Text = n > 0 ? $"Auswahl löschen ({n})" : "Auswahl löschen";
            delete.Enabled = _canDelete && n > 0;
            verify.Text = n > 0 ? $"Bestätigung … ({n})" : "Bestätigung …";
        }
        void SetAllChecked(bool value)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                row.Cells["check"].Value = value;
                if (row.Tag is Dictionary<string, string?> r) { if (value) _checkedIds.Add(IdOf(r)); else _checkedIds.Remove(IdOf(r)); }
            }
            UpdateBulkButtons();
        }
        _updateBulkButtons = UpdateBulkButtons;
    }

    /// <summary>Lädt die Liste beim ersten Anzeigen des Bereichs neu.</summary>
    public async Task ReloadAsync()
    {
        try
        {
            _footer.Text = "Lade Staff …";
            _all = await _api.ListStaffAsync();
            _checkedIds.IntersectWith(_all.Select(IdOf)); // gelöschte/nicht mehr vorhandene Personen aus der Auswahl entfernen
            ApplyFilter();
            if (_canWrite && !_campOptionsLoaded) await LoadCampOptionsAsync();
        }
        catch (Exception ex)
        {
            _footer.Text = "Fehler beim Laden.";
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
    }

    /// <summary>Lädt die Camp-Auswahlliste (fest + weitere) für die Massenzuweisung.</summary>
    private async Task LoadCampOptionsAsync()
    {
        try
        {
            _campOptions = await _api.GetCampOptionsAsync();
            _campAssign.Items.Clear();
            _campAssign.Items.AddRange(_campOptions.Select(o => (object) o.Label).ToArray());
            if (_campOptions.Count > 0) _campAssign.SelectedIndex = 0;
            _campOptionsLoaded = true;
        }
        catch (Exception)
        {
            // Massenzuweisung ist nur ein Zusatz - bei Fehlern bleibt die Liste leer, der Rest funktioniert normal
        }
    }

    /// <summary>Weist das gewählte Camp allen über die Kästchen ausgewählten Personen zu bzw. entfernt es.</summary>
    private async Task ApplyCampAssignAsync()
    {
        var selected = Selected().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(FindForm(), "Bitte zuerst über die Kästchen Personen auswählen.", "Camp-Zuweisung", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_campAssign.SelectedIndex < 0 || _campAssign.SelectedIndex >= _campOptions.Count)
        {
            MessageBox.Show(FindForm(), "Bitte ein Camp auswählen.", "Camp-Zuweisung", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var (value, label) = _campOptions[_campAssign.SelectedIndex];
        var add = _campAction.SelectedIndex != 1;
        try
        {
            var changed = await _api.AssignCampAsync("staff", selected.Select(IdOf), value, add);
            _footer.Text = $"„{label}“ bei {changed} Person(en) {(add ? "zugewiesen" : "entfernt")}.";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
    }

    private Dictionary<string, string?>? Current() => _grid.CurrentRow?.Tag as Dictionary<string, string?>;

    /// <summary>Über die Kästchen ausgewählte Personen (wie im Webpanel), nicht die reine Zeilenmarkierung.</summary>
    private IEnumerable<Dictionary<string, string?>> Selected() =>
        _all.Where(r => _checkedIds.Contains(IdOf(r)));

    private static string Val(Dictionary<string, string?> row, string key) => row.TryGetValue(key, out var v) ? v ?? "" : "";

    private static string NameOf(Dictionary<string, string?> row) => $"{Val(row, "nachname")} {Val(row, "vorname")}".Trim();

    private static int IdOf(Dictionary<string, string?> row) => int.TryParse(Val(row, "id"), out var id) ? id : 0;

    private static string NadaText(string value) => value.Equals("true", StringComparison.OrdinalIgnoreCase) || value is "1" ? "Ja" : "Nein";

    private static string FormatDate(string s) => DateTime.TryParse(s, out var d) ? d.ToString("dd.MM.yyyy") : s;

    private static VerifyPerson ToVerifyPerson(Dictionary<string, string?> r) =>
        new(IdOf(r), NameOf(r), Val(r, "status") != "inaktiv", Val(r, "email").Length > 0, Val(r, "bestaetigt_am").Length > 0);

    /// <summary>Gleiche Farben wie bei den Spielern: gelb / blau / orange / rot.</summary>
    private static void MarkExpiry(DataGridViewCell cell, (ExpiryState State, int Days, DateTime? Date) e)
    {
        if (e.State == ExpiryState.Ok || e.Date is null) return;
        var (back, fore) = Expiry.Colors(e.State);
        cell.Style.BackColor = back;
        cell.Style.ForeColor = fore;
        cell.ToolTipText = new ExpiryItem(new Models.Member(), "", e.Date.Value, e.State, e.Days).Describe();
    }

    private void ApplyFilter()
    {
        var q = _search.Text.Trim();
        var status = _statusFilter.SelectedIndex switch { 1 => "aktiv", 2 => "inaktiv", _ => null };
        var selectedId = Current() is { } cur ? IdOf(cur) : 0;
        var rows = _all.Where(r =>
                (status is null || (Val(r, "status") == "inaktiv" ? "inaktiv" : "aktiv") == status) &&
                (q.Length == 0 || new[] { "nachname", "vorname", "position", "email", "telefon" }.Any(k => Val(r, k).Contains(q, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(r => Val(r, "nachname"), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => Val(r, "vorname"), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var r in rows)
        {
            var pass = Expiry.Check(Val(r, "reisepass_gueltig_bis"), DateTime.Today.AddMonths(Expiry.PassWarnMonths), DateTime.Today);
            var confirmed = Val(r, "bestaetigt_am");
            var idx = _grid.Rows.Add(
                _checkedIds.Contains(IdOf(r)), NameOf(r), Val(r, "position"), NadaText(Val(r, "nada")),
                Val(r, "telefon"), Val(r, "email"),
                Val(r, "reisepass_gueltig_bis").Length > 0 ? FormatDate(Val(r, "reisepass_gueltig_bis")) : "",
                Val(r, "status") == "inaktiv" ? "Inaktiv" : "Aktiv",
                confirmed.Length == 0 ? "ausstehend" : "✓ " + FormatDate(confirmed));
            var row = _grid.Rows[idx];
            row.Tag = r;
            MarkExpiry(row.Cells["reisepass_gueltig_bis"], pass);
            row.Cells["bestaetigt"].Style.ForeColor = confirmed.Length == 0 ? Theme.AccentDark : Theme.Green;
            if (Val(r, "status") == "inaktiv") row.DefaultCellStyle.ForeColor = Theme.Muted;
            if (IdOf(r) == selectedId && selectedId != 0) { row.Selected = true; _grid.CurrentCell = row.Cells["name"]; }
        }
        _grid.ResumeLayout();

        var active = _all.Count(r => Val(r, "status") != "inaktiv");
        var confirmedCount = _all.Count(r => Val(r, "bestaetigt_am").Length > 0);
        var expiring = _all.Count(r =>
            Expiry.Check(Val(r, "reisepass_gueltig_bis"), DateTime.Today.AddMonths(Expiry.PassWarnMonths), DateTime.Today).State != ExpiryState.Ok);
        _cardTotal.Set(_all.Count.ToString(), $"Staff · {active} aktiv");
        _cardConfirmed.Set(confirmedCount.ToString(), $"Bestätigt · {_all.Count - confirmedCount} offen");
        _cardExpiry.Set(expiring.ToString(), "Läuft ab / abgelaufen");
        _footer.Text = $"{rows.Count} von {_all.Count} angezeigt · Doppelklick zum Bearbeiten";
        _updateBulkButtons?.Invoke();
    }

    private void OpenLink(Dictionary<string, string?> row)
    {
        using var form = new MemberLinkForm(_api, "staff", IdOf(row), NameOf(row));
        form.ShowDialog(FindForm());
        _ = ReloadAsync();
    }

    private async Task VerifyAsync()
    {
        using var form = new VerificationForm(_api, "staff", _all.Select(ToVerifyPerson).ToList(), Selected().Select(ToVerifyPerson).ToList());
        form.ShowDialog(FindForm());
        if (form.Changed) await ReloadAsync();
    }

    private async Task ExportAsync()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV (Excel)|*.csv", FileName = $"staff-{DateTime.Now:yyyy-MM-dd}.csv" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            await File.WriteAllBytesAsync(dialog.FileName, await _api.DownloadCsvAsync(null, template: false, staff: true));
        }
        catch (Exception ex)
        {
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
    }

    private async Task OpenEditorAsync(Dictionary<string, string?>? row)
    {
        using var dialog = new StaffEditDialog(_api, row);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            int? id = row is not null && IdOf(row) > 0 ? IdOf(row) : null;
            var savedId = await _api.SaveStaffAsync(id, dialog.Payload);
            if (id is not null)
            {
                foreach (var type in dialog.DocRemove) await _api.DeleteDocumentAsync(savedId, type, kind: "staff");
            }
            foreach (var (type, file) in dialog.DocFiles) await _api.UploadDocumentAsync(savedId, type, file, kind: "staff");
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
    }

    private async Task DeleteAsync()
    {
        var rows = Selected().ToList();
        if (rows.Count == 0) return;
        if (MessageBox.Show(FindForm(), $"{rows.Count} Person(en) wirklich löschen?", "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            foreach (var r in rows)
            {
                if (IdOf(r) > 0) await _api.DeleteStaffAsync(IdOf(r));
            }
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
    }
}
