using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>CSV/Excel-Datei wählen, Vorschau prüfen, importieren. Die Prüfung erfolgt auf dem Server.</summary>
public sealed class ImportForm : Form
{
    private static readonly Dictionary<string, string> ActionText = new()
    {
        ["create"] = "Neu",
        ["update"] = "Aktualisieren",
        ["skip"] = "Übersprungen",
        ["error"] = "Fehler",
    };

    private readonly ApiClient _api;
    private readonly TextBox _path = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly CheckBox _update = new() { Text = "Vorhandene Mitglieder (gleiche E-Mail) aktualisieren – leere Zellen überschreiben nichts", Checked = true, AutoSize = true };
    private readonly RadioButton _kaderIn = new() { Text = "Alle importierten Spieler sind im Kader", AutoSize = true };
    private readonly RadioButton _kaderOut = new() { Text = "Alle importierten Spieler sind nicht im Kader", AutoSize = true };
    private readonly RadioButton _kaderFile = new() { Text = "Aus der Datei übernehmen (Spalte „Kader“), sonst „Im Kader“", AutoSize = true, Checked = true };
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new() { AutoSize = true, MaximumSize = new Size(820, 0), Margin = new Padding(0, 6, 0, 6) };
    private readonly Button _preview = Theme.MakeButton("Vorschau");
    private readonly Button _mapping = Theme.MakeButton("Spaltenzuordnung …");
    private ImportResponse? _last;
    private Dictionary<int, string>? _overrides;
    private readonly Button _commit = Theme.MakeButton("Importieren", primary: true);
    private string? _file;

    /// <summary>true, wenn tatsächlich Daten geschrieben wurden (Liste neu laden).</summary>
    public bool Changed { get; private set; }

    public ImportForm(ApiClient api)
    {
        _api = api;
        Text = "Import aus CSV / Excel";
        Font = Theme.Body;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 600);
        MinimumSize = new Size(720, 460);

        var browse = Theme.MakeButton("Datei wählen …");
        var template = Theme.MakeButton("Vorlage herunterladen");
        browse.Click += (_, _) => Browse();
        template.Click += async (_, _) => await DownloadTemplateAsync();
        _preview.Click += async (_, _) => await RunAsync(commit: false);
        _commit.Click += async (_, _) => await RunAsync(commit: true);
        _preview.Enabled = _commit.Enabled = false;

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(14, 14, 14, 0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _path.Margin = new Padding(0, 4, 8, 0);
        browse.Margin = new Padding(0, 0, 8, 0);
        top.Controls.Add(_path);
        top.Controls.Add(browse);
        top.Controls.Add(template);

        var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(14, 8, 14, 0) };
        options.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Theme.Muted,
            MaximumSize = new Size(820, 0),
            Text = "Unterstützt .xlsx und .csv. Erste Zeile = Spaltenüberschriften; Pflicht: Nachname, Vorname, Mail.",
        });
        options.Controls.Add(_update);
        options.Controls.Add(new Label { Text = "Kader-Status der importierten Spieler:", AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
        foreach (var radio in new[] { _kaderIn, _kaderOut, _kaderFile })
        {
            radio.CheckedChanged += (_, _) => { if (radio.Checked && _file is not null) _ = RunAsync(commit: false); };
            options.Controls.Add(radio);
        }
        options.Controls.Add(_summary);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.NavyLight;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.NavyLight;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Zeile", FillWeight = 7 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Aktion", FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "E-Mail", FillWeight = 24 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Hinweis", FillWeight = 34 });

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 0),
            BackColor = Theme.Background,
        };
        var close = Theme.MakeButton("Schließen");
        close.Click += (_, _) => Close();
        bar.Controls.Add(close);
        bar.Controls.Add(_commit);
        bar.Controls.Add(_preview);
        bar.Controls.Add(_mapping);
        _mapping.Enabled = false;
        _mapping.Click += (_, _) => ShowMapping();

        var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 8) };
        center.Controls.Add(_grid);

        Controls.Add(center);
        Controls.Add(options);
        Controls.Add(top);
        Controls.Add(bar);
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog { Filter = "Excel/CSV|*.xlsx;*.csv;*.txt|Alle Dateien|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _file = dialog.FileName;
        _overrides = null; // neue Datei = neue automatische Zuordnung
        _path.Text = _file;
        _preview.Enabled = true;
        _commit.Enabled = false;
        _grid.Rows.Clear();
        _summary.Text = "";
        _ = RunAsync(commit: false);
    }

    private async Task DownloadTemplateAsync()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV (Excel)|*.csv", FileName = "mitglieder-vorlage.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await File.WriteAllBytesAsync(dialog.FileName, await _api.DownloadCsvAsync(null, template: true));
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private async Task RunAsync(bool commit)
    {
        if (_file is null) return;
        _preview.Enabled = _commit.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var response = await _api.ImportAsync(_file, _update.Checked, commit, KaderDefault(), _overrides);

            if (commit && response.Result is { } result)
            {
                Changed = true;
                var text = $"Import abgeschlossen: {result.Created} neu, {result.Updated} aktualisiert, {result.Failed.Count} fehlgeschlagen.";
                if (result.Failed.Count > 0) text += "\n" + string.Join("\n", result.Failed.Take(15));
                MessageBox.Show(this, text, "Import", MessageBoxButtons.OK,
                    result.Failed.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                _file = null;
                _path.Text = "";
                _grid.Rows.Clear();
                _summary.Text = text.Split('\n')[0];
                _summary.ForeColor = Theme.Green;
                return;
            }

            ShowPreview(response);
        }
        catch (Exception ex)
        {
            _summary.Text = ex.Message;
            _summary.ForeColor = Theme.Danger;
            _grid.Rows.Clear();
        }
        finally
        {
            UseWaitCursor = false;
            _preview.Enabled = _file is not null;
        }
    }

    private string? KaderDefault() => _kaderIn.Checked ? "kader" : _kaderOut.Checked ? "nicht_im_kader" : null;

    private async void ShowMapping()
    {
        if (_last is null) return;
        if (_last.Fields.Count == 0)
        {
            MessageBox.Show(this, "Der Server unterstützt die manuelle Spaltenzuordnung noch nicht (alte Version). Bitte zuerst die Web-Anwendung auf dem Server aktualisieren (git pull).", "Server veraltet", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var form = new MappingForm(_last);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        _overrides = new Dictionary<int, string>(form.Mapping);
        await RunAsync(commit: false);
    }

    private void ShowPreview(ImportResponse r)
    {
        _grid.Rows.Clear();
        foreach (var row in r.Rows)
        {
            var note = string.Join("; ", row.Errors.Count > 0 ? row.Errors : row.Warnings);
            if (row.Action == "skip") note = "Existiert bereits (Aktualisieren nicht aktiviert)";
            var idx = _grid.Rows.Add(row.Line, ActionText.GetValueOrDefault(row.Action, row.Action), row.Name, row.Email, note);
            if (row.Action == "error") _grid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Danger;
            else if (row.Action == "skip") _grid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Muted;
        }

        var c = r.Counts;
        var text = $"{c.Create} neu · {c.Update} aktualisieren · {c.Skip} übersprungen · {c.Error} fehlerhaft";
        if (c.Warning > 0) text += $" · {c.Warning} mit Hinweis";
        if (r.MissingRequired.Count > 0)
        {
            text += "\nPflichtfeld nicht zugeordnet: " + string.Join(", ", r.MissingRequired) + " – bitte über „Spaltenzuordnung …“ zuordnen.";
        }
        if (r.UnknownColumns.Count > 0)
        {
            text += "\nNICHT übernommen (Spalte konnte keinem Feld zugeordnet werden): " + string.Join(", ", r.UnknownColumns);
        }
        _summary.Text = text;
        _summary.ForeColor = c.Error > 0 || r.UnknownColumns.Count > 0 ? Theme.AccentDark : Theme.Green;
        _last = r;
        _mapping.Enabled = true;

        var importable = c.Create + c.Update;
        _commit.Enabled = importable > 0;
        _commit.Text = importable > 0 ? $"{importable} Zeile(n) importieren" : "Importieren";
    }
}
