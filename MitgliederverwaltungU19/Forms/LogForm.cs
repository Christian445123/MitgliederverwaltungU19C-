using System.Diagnostics;
using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Protokoll (Audit- und Zugriffslog) wie im Web-Panel: filtern, blättern, als CSV exportieren, bereinigen.</summary>
public sealed class LogForm : Form
{
    private readonly ApiClient _api;
    private readonly bool _canPurge;
    private readonly ComboBox _source = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _level = new() { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _action = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _actor = new() { Width = 130, PlaceholderText = "Benutzer" };
    private readonly TextBox _query = new() { Width = 170, PlaceholderText = "Suche: Text, Pfad, ID, IP" };
    private readonly DateTimePicker _from = new() { Width = 110, Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false };
    private readonly DateTimePicker _to = new() { Width = 110, Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false };
    private readonly Label _stats = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 0) };
    private readonly DataGridView _grid = new();
    private readonly Label _pageLabel = new() { AutoSize = true, Margin = new Padding(10, 9, 10, 0) };
    private readonly Button _prev = Theme.MakeButton("‹ Zurück");
    private readonly Button _next = Theme.MakeButton("Weiter ›");
    private int _page = 1;
    private int _pages = 1;

    public LogForm(ApiClient api, bool canPurge)
    {
        _api = api;
        _canPurge = canPurge;
        Text = "Protokoll";
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1240, 740);
        MinimumSize = new Size(960, 520);

        _source.Items.AddRange(new object[] { "Alle", "web", "api" });
        _level.Items.AddRange(new object[] { "Alle", "debug", "info", "warning", "error" });
        _source.SelectedIndex = _level.SelectedIndex = 0;
        _action.Items.Add("");

        var filter = Theme.MakeButton("Filtern", primary: true);
        var reset = Theme.MakeButton("Zurücksetzen");
        filter.Click += async (_, _) => { _page = 1; await LoadAsync(); };
        reset.Click += async (_, _) => { ResetFilters(); _page = 1; await LoadAsync(); };
        _query.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _page = 1; await LoadAsync(); } };

        Control Field(string caption, Control input)
        {
            var box = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 0, 10, 0), WrapContents = false };
            box.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 2) });
            input.Margin = new Padding(0);
            box.Controls.Add(input);
            return box;
        }
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(14, 8, 14, 0), WrapContents = false };
        top.Controls.AddRange(new[] { Field("Quelle", _source), Field("Stufe", _level), Field("Aktion", _action), Field("Benutzer", _actor), Field("Suche", _query), Field("Von", _from), Field("Bis", _to) });
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0), WrapContents = false };
        buttons.Controls.AddRange(new Control[] { filter, reset });
        top.Controls.Add(buttons);

        var statsBar = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(14, 0, 14, 0) };
        statsBar.Controls.Add(_stats);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Theme.StyleGrid(_grid);
        _grid.RowTemplate.Height = 30;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Zeit", FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quelle", FillWeight = 5 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Stufe", FillWeight = 6 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Aktion", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Benutzer", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Meldung", FillWeight = 36 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "IP", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", FillWeight = 5 });
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowDetails(); };

        _prev.Click += async (_, _) => { if (_page > 1) { _page--; await LoadAsync(); } };
        _next.Click += async (_, _) => { if (_page < _pages) { _page++; await LoadAsync(); } };
        var export = Theme.MakeButton("Als CSV exportieren");
        export.Click += async (_, _) => await ExportAsync();
        var purge = Theme.MakeButton("Bereinigen …");
        purge.ForeColor = Theme.Danger;
        purge.Visible = canPurge;
        purge.Click += async (_, _) => await PurgeAsync();
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(14, 8, 14, 0), BackColor = Color.White };
        bottom.Controls.AddRange(new Control[] { _prev, _pageLabel, _next, export, purge });

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 8) };
        host.Controls.Add(_grid);
        Controls.Add(host);
        Controls.Add(bottom);
        Controls.Add(statsBar);
        Controls.Add(top);
        Shown += async (_, _) => await LoadAsync();
    }

    private void ResetFilters()
    {
        _source.SelectedIndex = _level.SelectedIndex = 0;
        _action.Text = "";
        _actor.Clear();
        _query.Clear();
        _from.Checked = _to.Checked = false;
    }

    private LogFilter CurrentFilter() => new()
    {
        Source = _source.SelectedIndex > 0 ? _source.Text : "",
        Level = _level.SelectedIndex > 0 ? _level.Text : "",
        Action = _action.Text.Trim(),
        Actor = _actor.Text.Trim(),
        Query = _query.Text.Trim(),
        From = _from.Checked ? _from.Value.ToString("yyyy-MM-dd") : "",
        To = _to.Checked ? _to.Value.ToString("yyyy-MM-dd") : "",
    };

    private async Task LoadAsync()
    {
        try
        {
            UseWaitCursor = true;
            var result = await _api.GetLogsAsync(CurrentFilter(), _page);
            _page = result.Page;
            _pages = result.Pages;
            _grid.Rows.Clear();
            foreach (var e in result.Entries)
            {
                var i = _grid.Rows.Add(FormatTime(e.Time), e.Source, e.Level, e.Action, e.Actor, e.Message, e.Ip, e.Status);
                var row = _grid.Rows[i];
                row.Tag = e;
                if (e.Level == "error") row.DefaultCellStyle.ForeColor = Theme.Danger;
                else if (e.Level == "warning") row.DefaultCellStyle.ForeColor = Theme.AccentDark;
            }
            _stats.Text = $"Letzte 24 Stunden: {result.Requests24h} Anfragen · {result.Errors24h} Fehler · {result.Warnings24h} Warnungen · {result.FailedLogins24h} fehlgeschlagene Anmeldungen";
            _pageLabel.Text = $"Seite {_page} von {_pages} · {result.Total} Einträge";
            _prev.Enabled = _page > 1;
            _next.Enabled = _page < _pages;
            if (_action.Items.Count <= 1)
            {
                foreach (var a in result.Actions) _action.Items.Add(a);
            }
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

    private static string FormatTime(string s) => DateTime.TryParse(s, out var d) ? d.ToString("dd.MM.yyyy HH:mm:ss") : s;

    private void ShowDetails()
    {
        if (_grid.CurrentRow?.Tag is not LogEntry e) return;
        var text = $"Zeit:     {FormatTime(e.Time)}\r\nQuelle:   {e.Source}\r\nStufe:    {e.Level}\r\nAktion:   {e.Action}\r\nBenutzer: {e.Actor}\r\nIP:       {e.Ip}\r\n" +
                   $"Anfrage:  {e.Method} {e.Path}  (Status {e.Status})\r\n\r\n{e.Message}\r\n\r\nDetails:\r\n{e.Details}";
        using var form = new Form
        {
            Text = "Protokolleintrag",
            Font = Theme.Body,
            Icon = Theme.AppIcon,
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(720, 520),
        };
        form.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = text, Font = new Font("Consolas", 10f), BackColor = Color.White });
        form.ShowDialog(this);
    }

    private async Task ExportAsync()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"protokoll-{DateTime.Now:yyyy-MM-dd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            await File.WriteAllBytesAsync(dialog.FileName, await _api.DownloadLogsCsvAsync(CurrentFilter()));
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
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

    private async Task PurgeAsync()
    {
        using var dialog = new PromptDialog("Protokoll bereinigen", "Einträge löschen, die älter sind als … Tage (0 = alle)", "90");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!int.TryParse(dialog.Value, out var days) || days < 0)
        {
            MessageBox.Show(this, "Bitte eine Zahl (Tage) eingeben.", "Bereinigen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var what = days == 0 ? "das gesamte Protokoll" : $"alle Einträge älter als {days} Tage";
        if (MessageBox.Show(this, $"Wirklich {what} löschen?", "Bereinigen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            var deleted = await _api.PurgeLogsAsync(days);
            MessageBox.Show(this, $"{deleted} Protokolleinträge wurden gelöscht.", "Bereinigen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _page = 1;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }
}
