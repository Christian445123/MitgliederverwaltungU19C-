using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>
/// Neue Mitglieder: Anmeldungen über den öffentlichen Registrierungslink, die noch nicht dem Kader oder
/// "nicht im Kader" zugewiesen sind. Entspricht dem Web-Panel unter „Neue Mitglieder“.
/// </summary>
public sealed class RegistrationsPanel : UserControl
{
    private readonly ApiClient _api;
    private readonly bool _canWrite;
    private readonly DataGridView _grid = new();
    private readonly ComboBox _target = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _footer = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private List<Member> _all = new();

    private readonly DataGridView _staffGrid = new();
    private readonly Label _staffFooter = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private List<PendingStaff> _allStaff = new();

    /// <summary>Wird ausgelöst, nachdem eine Anmeldung übernommen, abgelehnt oder bearbeitet wurde (Hauptliste/Badge aktualisieren).</summary>
    public event Action? Changed;

    public RegistrationsPanel(ApiClient api, bool canWrite)
    {
        _api = api;
        _canWrite = canWrite;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        Padding = new Padding(26, 18, 26, 8);
        Font = Theme.Body;

        var titleRow = new Panel { Dock = DockStyle.Top, Height = 54 };
        var title = new Label
        {
            Text = "Neue Mitglieder",
            Font = new Font(Theme.Title.FontFamily, 20f * Theme.Zoom, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x17, 0x19, 0x23),
            AutoSize = true,
            Location = new Point(0, 4),
        };
        var links = Theme.MakeButton("Registrierungslinks …", primary: true);
        var refresh = Theme.MakeButton("Aktualisieren");
        links.Click += (_, _) => { using var form = new RegistrationLinksForm(_api, _canWrite); form.ShowDialog(FindForm()); };
        refresh.Click += async (_, _) => await ReloadAsync();
        links.Margin = new Padding(0);
        refresh.Margin = new Padding(0, 0, 10, 0);
        var titleActions = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        titleActions.Controls.Add(links);
        titleActions.Controls.Add(refresh);
        titleRow.Controls.Add(title);
        titleRow.Controls.Add(titleActions);

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Px(40),
            ForeColor = Theme.Muted,
            Text = "Meldet sich jemand mit einer bereits vorhandenen E-Mail-Adresse an, wird kein zweiter Datensatz angelegt " +
                   "(die Person bekommt automatisch einen Link zum Aktualisieren ihrer Daten).",
        };

        var details = Theme.MakeButton("Details ansehen …");
        _target.Items.AddRange(new object[] { "Kader", "Nicht im Kader", "Staff" });
        _target.SelectedIndex = 0;
        _target.Margin = new Padding(0, 4, 8, 0);
        var approve = Theme.MakeButton("Übernehmen", primary: true);
        var reject = Theme.MakeButton("Ablehnen …");
        reject.ForeColor = Theme.Danger;
        _target.Enabled = approve.Enabled = reject.Enabled = _canWrite;
        details.Click += async (_, _) => { if (Current() is { } m) await OpenDetailsAsync(m); };
        approve.Click += async (_, _) => await ApproveAsync();
        reject.Click += async (_, _) => await RejectAsync();
        var actionBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, Theme.Px(52)), WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
        details.Margin = new Padding(0, 0, 16, 0);
        approve.Margin = new Padding(0, 0, 8, 0);
        actionBar.Controls.AddRange(new Control[] { details, _target, approve, reject });

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Theme.StyleGrid(_grid);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (key, label, weight) in new[]
                 {
                     ("name", "Name & Vorname", 22f), ("email", "E-Mail", 24f), ("telefon", "Telefon", 14f),
                     ("verein", "Verein", 16f), ("angemeldet", "Angemeldet am", 14f),
                 })
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = key, HeaderText = label, FillWeight = weight, SortMode = DataGridViewColumnSortMode.Automatic, MinimumWidth = Theme.Px(70) });
        }
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0 && Current() is { } m) await OpenDetailsAsync(m); };

        var gridCard = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1) };
        gridCard.Controls.Add(_grid);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Background };
        footer.Controls.Add(_footer);

        var playerPage = new TabPage("Spieler") { BackColor = Theme.Background, Padding = new Padding(0, 8, 0, 0) };
        playerPage.Controls.Add(gridCard);
        playerPage.Controls.Add(footer);
        playerPage.Controls.Add(actionBar);
        playerPage.Controls.Add(info);

        // ── Staff-Anmeldungen (eigener Staff-Einladungslink) ────────────────
        var staffApprove = Theme.MakeButton("Übernehmen", primary: true);
        var staffReject = Theme.MakeButton("Ablehnen …");
        staffReject.ForeColor = Theme.Danger;
        staffApprove.Enabled = staffReject.Enabled = _canWrite;
        staffApprove.Click += async (_, _) => await ApproveStaffAsync();
        staffReject.Click += async (_, _) => await RejectStaffAsync();
        var staffActionBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, Theme.Px(52)), WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
        staffApprove.Margin = new Padding(0, 0, 8, 0);
        staffActionBar.Controls.AddRange(new Control[] { staffApprove, staffReject });

        _staffGrid.Dock = DockStyle.Fill;
        _staffGrid.ReadOnly = true;
        _staffGrid.AllowUserToAddRows = false;
        _staffGrid.AllowUserToDeleteRows = false;
        _staffGrid.AllowUserToResizeRows = false;
        _staffGrid.MultiSelect = false;
        _staffGrid.RowHeadersVisible = false;
        _staffGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Theme.StyleGrid(_staffGrid);
        _staffGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (key, label, weight) in new[]
                 {
                     ("name", "Name & Vorname", 26f), ("position", "Position", 16f), ("email", "E-Mail", 26f),
                     ("telefon", "Telefon", 16f), ("angemeldet", "Angemeldet am", 16f),
                 })
        {
            _staffGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = key, HeaderText = label, FillWeight = weight, SortMode = DataGridViewColumnSortMode.Automatic, MinimumWidth = Theme.Px(70) });
        }

        var staffGridCard = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1) };
        staffGridCard.Controls.Add(_staffGrid);
        var staffFooter = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Background };
        staffFooter.Controls.Add(_staffFooter);

        var staffPage = new TabPage("Staff") { BackColor = Theme.Background, Padding = new Padding(0, 8, 0, 0) };
        staffPage.Controls.Add(staffGridCard);
        staffPage.Controls.Add(staffFooter);
        staffPage.Controls.Add(staffActionBar);

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.Bold };
        tabs.TabPages.Add(playerPage);
        tabs.TabPages.Add(staffPage);

        Controls.Add(tabs);
        Controls.Add(titleRow);
    }

    private Member? Current() => _grid.CurrentRow?.Tag as Member;
    private PendingStaff? CurrentStaff() => _staffGrid.CurrentRow?.Tag as PendingStaff;

    private static string FormatDate(string s) => DateTime.TryParse(s, out var d) ? d.ToString("dd.MM.yyyy HH:mm") : s;

    public async Task ReloadAsync()
    {
        try
        {
            _footer.Text = "Lade neue Anmeldungen …";
            _all = await _api.ListRegistrationsAsync();
            Fill();
        }
        catch (Exception ex)
        {
            _footer.Text = "Fehler beim Laden.";
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
        try
        {
            _staffFooter.Text = "Lade neue Staff-Anmeldungen …";
            _allStaff = await _api.ListPendingStaffAsync();
            FillStaff();
        }
        catch (Exception ex)
        {
            _staffFooter.Text = "Fehler beim Laden.";
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
    }

    private void Fill()
    {
        var selectedId = Current()?.Id;
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var m in _all.OrderByDescending(m => m.Get("created_at")))
        {
            var idx = _grid.Rows.Add(m.FullName, m.Get("email"), m.Get("telefon"), m.Get("verein"), FormatDate(m.Get("created_at")));
            var row = _grid.Rows[idx];
            row.Tag = m;
            if (m.Id == selectedId) { row.Selected = true; _grid.CurrentCell = row.Cells[0]; }
        }
        _grid.ResumeLayout();
        _footer.Text = _all.Count == 0 ? "Aktuell keine neuen Anmeldungen." : $"{_all.Count} Anmeldung(en) warten auf Prüfung · Doppelklick für Details";
    }

    private void FillStaff()
    {
        var selectedId = CurrentStaff()?.Id;
        _staffGrid.SuspendLayout();
        _staffGrid.Rows.Clear();
        foreach (var s in _allStaff.OrderByDescending(s => s.CreatedAt))
        {
            var idx = _staffGrid.Rows.Add(s.DisplayName, s.Position, s.Email, s.Telefon, FormatDate(s.CreatedAt ?? ""));
            var row = _staffGrid.Rows[idx];
            row.Tag = s;
            if (s.Id == selectedId) { row.Selected = true; _staffGrid.CurrentCell = row.Cells[0]; }
        }
        _staffGrid.ResumeLayout();
        _staffFooter.Text = _allStaff.Count == 0 ? "Aktuell keine neuen Staff-Anmeldungen." : $"{_allStaff.Count} Staff-Anmeldung(en) warten auf Freigabe";
    }

    private async Task OpenDetailsAsync(Member member)
    {
        using var form = new MemberForm(_api, member, readOnly: !_canWrite);
        var saved = form.ShowDialog(FindForm()) == DialogResult.OK;
        if (saved || form.DocumentsChanged)
        {
            await ReloadAsync();
            Changed?.Invoke();
        }
    }

    private async Task ApproveAsync()
    {
        if (Current() is not { } member) return;
        var target = _target.SelectedIndex switch { 1 => "nicht_im_kader", 2 => "staff", _ => "kader" };
        var label = target switch { "nicht_im_kader" => "nicht im Kader", "staff" => "als Staff", _ => "in den Kader" };
        if (MessageBox.Show(FindForm(), $"„{member.FullName}“ wirklich übernehmen ({label})?", "Übernehmen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            if (target == "staff")
            {
                await _api.ApproveRegistrationAsStaffAsync(member.Id);
            }
            else
            {
                await _api.ApproveRegistrationAsync(member.Id, target);
            }
            await ReloadAsync();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
    }

    private async Task RejectAsync()
    {
        if (Current() is not { } member) return;
        if (MessageBox.Show(FindForm(), $"Anmeldung von „{member.FullName}“ wirklich ablehnen und löschen?", "Ablehnen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _api.RejectRegistrationAsync(member.Id);
            await ReloadAsync();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
    }

    private async Task ApproveStaffAsync()
    {
        if (CurrentStaff() is not { } person) return;
        if (MessageBox.Show(FindForm(), $"„{person.DisplayName}“ wirklich als Staff übernehmen?", "Übernehmen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            await _api.ApproveStaffRegistrationAsync(person.Id);
            await ReloadAsync();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
    }

    private async Task RejectStaffAsync()
    {
        if (CurrentStaff() is not { } person) return;
        if (MessageBox.Show(FindForm(), $"Staff-Anmeldung von „{person.DisplayName}“ wirklich ablehnen und löschen?", "Ablehnen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _api.RejectStaffRegistrationAsync(person.Id);
            await ReloadAsync();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Theme.ShowError(FindForm() ?? (IWin32Window)this, ex);
        }
    }
}
