using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Spalten der Import-Datei manuell den Feldern zuordnen.</summary>
public sealed class MappingForm : Form
{
    private const string Skip = "— nicht importieren —";

    private readonly ImportResponse _response;
    private readonly DataGridView _grid = new();

    /// <summary>Ergebnis: Spaltenindex → Feldname (oder "-" = nicht importieren).</summary>
    public Dictionary<int, string> Mapping { get; } = new();

    public MappingForm(ImportResponse response)
    {
        _response = response;

        Text = "Spaltenzuordnung";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 620);
        MinimumSize = new Size(600, 420);

        var info = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 52,
            Padding = new Padding(14, 10, 14, 0),
            ForeColor = Theme.Muted,
            Text = $"Überschriftenzeile: {response.HeaderRow}. Wählen Sie für jede Spalte das Feld, in das die Werte übernommen werden sollen. " +
                   "Orange markierte Spalten enthalten Daten, sind aber noch keinem Feld zugeordnet.",
        };

        var labels = new List<string> { Skip };
        labels.AddRange(response.Fields.Select(f => f.Label));
        // Sicherheitsnetz: jede vom Server gemeldete Zuordnung muss in der Auswahlliste vorkommen
        foreach (var col in response.Columns)
        {
            if (col.Field is not null && !labels.Contains(col.Field)) labels.Add(col.Field);
        }

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.NavyLight;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.NavyLight;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = 28;
        _grid.DataError += (_, e) => e.ThrowException = false; // ungültige Zellwerte nicht als Standarddialog anzeigen
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Spalte in der Datei", ReadOnly = true, FillWeight = 38 });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Wird übernommen als",
            DataSource = labels,
            FillWeight = 46,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Gefüllte Zellen", ReadOnly = true, FillWeight = 16 });

        foreach (var col in response.Columns)
        {
            var idx = _grid.Rows.Add(col.Header, col.Field ?? Skip, col.Values);
            var row = _grid.Rows[idx];
            row.Tag = col.Index;
            if (col.State == "unmapped" && col.Values > 0)
            {
                row.DefaultCellStyle.BackColor = Theme.AccentSoft;
            }
        }

        var ok = Theme.MakeButton("Übernehmen", primary: true);
        var cancel = Theme.MakeButton("Abbrechen");
        ok.Click += (_, _) => Apply();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        CancelButton = cancel;

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 0),
            BackColor = Theme.Background,
        };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);

        var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 8) };
        center.Controls.Add(_grid);

        Controls.Add(center);
        Controls.Add(info);
        Controls.Add(bar);
    }

    private void Apply()
    {
        var byLabel = _response.Fields.ToDictionary(f => f.Label, f => f.Key);
        var used = new Dictionary<string, string>();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            var label = row.Cells[1].Value?.ToString() ?? Skip;
            var index = (int)row.Tag!;
            if (label == Skip || !byLabel.TryGetValue(label, out var key))
            {
                Mapping[index] = "-";
                continue;
            }

            if (used.TryGetValue(key, out var firstColumn))
            {
                MessageBox.Show(this,
                    $"Das Feld „{label}“ ist doppelt zugeordnet (Spalten „{firstColumn}“ und „{row.Cells[0].Value}“). Ein Feld kann nur aus einer Spalte kommen.",
                    "Doppelte Zuordnung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Mapping.Clear();
                return;
            }
            used[key] = row.Cells[0].Value?.ToString() ?? "";
            Mapping[index] = key;
        }

        DialogResult = DialogResult.OK;
    }
}
