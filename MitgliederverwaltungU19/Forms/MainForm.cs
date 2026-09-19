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

    public MainForm(AppSettings settings, ApiClient api, PingResult ping)
    {
        _settings = settings;
        _api = api;
        _ping = ping;

        Text = "Mitgliederverwaltung U19";
        Font = Theme.Body;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 720);
        MinimumSize = new Size(900, 500);

        BuildLayout();
        Shown += async (_, _) => await ReloadAsync();
        FormClosed += (_, _) => _api.Dispose();
    }

    private void BuildLayout()
    {
        // Kopfleiste
        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Theme.Navy };
        var brand = new Label
        {
            Text = "U19  Mitgliederverwaltung",
            ForeColor = Color.White,
            Font = Theme.Title,
            AutoSize = true,
            Location = new Point(18, 12),
        };
        var user = new Label
        {
            Text = $"{_ping.TokenName} · {(_ping.CanWrite ? "Lesen & Schreiben" : "nur Lesen")}",
            ForeColor = Color.FromArgb(0xC7, 0xCB, 0xE0),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        header.Controls.Add(brand);
        header.Controls.Add(user);
        header.Resize += (_, _) => user.Location = new Point(header.Width - user.Width - 18, 19);
        user.Location = new Point(header.Width - user.Width - 18, 19);

        // Werkzeugleiste
        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 92,
            Padding = new Padding(14, 10, 14, 0),
            BackColor = Color.White,
            WrapContents = true,
        };
        _statusFilter.Items.AddRange(new object[] { "Alle", "Aktiv", "Inaktiv" });
        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        _kaderFilter.Items.AddRange(new object[] { "Alle Spieler", "Im Kader", "Spieler nicht im Kader" });
        _kaderFilter.SelectedIndex = 0;
        _kaderFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        _kaderFilter.Margin = new Padding(0, 2, 8, 0);
        _search.TextChanged += (_, _) => ApplyFilter();
        _search.Margin = new Padding(0, 2, 8, 0);
        _statusFilter.Margin = new Padding(0, 2, 16, 0);

        var refresh = Theme.MakeButton("Aktualisieren");
        var add = Theme.MakeButton("+ Neues Mitglied", primary: true);
        var edit = Theme.MakeButton("Bearbeiten");
        var delete = Theme.MakeButton("Auswahl löschen");
        var deleteAll = Theme.MakeButton("Alle löschen …");
        deleteAll.ForeColor = Theme.Danger;
        var import = Theme.MakeButton("Import …");
        var export = Theme.MakeButton("Export CSV");
        var settings = Theme.MakeButton("Einstellungen");
        var deploy = Theme.MakeButton("Änderungen einspielen");

        refresh.Click += async (_, _) => await ReloadAsync();
        add.Click += async (_, _) => await OpenEditorAsync(null);
        edit.Click += async (_, _) => { if (SelectedMember() is { } m) await OpenEditorAsync(m); };
        delete.Click += async (_, _) => await DeleteSelectedAsync();
        deleteAll.Click += async (_, _) => await DeleteAllAsync();
        deleteAll.Enabled = _ping.CanWrite;
        import.Click += async (_, _) => await OpenImportAsync();
        export.Click += async (_, _) => await ExportAsync();
        settings.Click += (_, _) => OpenSettings();
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
        deploy.Enabled = _ping.CanWrite;
        _writeButtons.AddRange(new[] { add, edit, delete, import });

        tools.Controls.AddRange(new Control[] { _search, _statusFilter, _kaderFilter, refresh, add, edit, delete, import, export, deleteAll, deploy, settings });
        if (!_ping.CanWrite)
        {
            foreach (var b in _writeButtons) b.Enabled = false;
        }

        // Tabelle
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = Theme.Background;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.GridColor = Theme.Border;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersHeight = 36;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.NavyLight;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.Font = Theme.Bold;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.NavyLight;
        _grid.DefaultCellStyle.BackColor = Color.White;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
        _grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        _grid.RowTemplate.Height = 30;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        AddColumn("name", "Name", 22);
        AddColumn("verein", "Verein", 18);
        AddColumn("position", "Position", 12);
        AddColumn("jersey", "Jersey", 7);
        AddColumn("email", "E-Mail", 24);
        AddColumn("telefon", "Telefon", 14);
        AddColumn("status", "Status", 8);
        AddColumn("kader", "Kader", 11);
        AddColumn("bestaetigt", "Bestätigt", 9);
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && SelectedMember() is { } m) await OpenEditorAsync(m);
        };
        _grid.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; if (SelectedMember() is { } m) await OpenEditorAsync(m); }
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(14, 0, 14, 0), BackColor = Color.White };
        footer.Controls.Add(_footer);
        tools.Controls.Add(_stats);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 8) };
        body.Controls.Add(_grid);

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(tools);
        Controls.Add(header);
    }

    private void AddColumn(string name, string title, float weight)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = title,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.Automatic,
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
                m.ConfirmedAt is null ? "ausstehend" : "✓ " + FormatDate(m.ConfirmedAt));
            var row = _grid.Rows[idx];
            row.Tag = m;
            if (m.ConfirmedAt is null) row.Cells["bestaetigt"].Style.ForeColor = Theme.AccentDark;
            else row.Cells["bestaetigt"].Style.ForeColor = Theme.Green;
            if (m.Get("status") == "inaktiv") row.DefaultCellStyle.ForeColor = Theme.Muted;
            if (m.Id == selectedId) { row.Selected = true; _grid.CurrentCell = row.Cells[0]; }
        }
        _grid.ResumeLayout();

        var active = _all.Count(m => m.Get("status") != "inaktiv");
        var confirmed = _all.Count(m => m.ConfirmedAt is not null);
        _stats.Text = $"{_all.Count} Mitglieder · {active} aktiv · {confirmed} bestätigt";
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
