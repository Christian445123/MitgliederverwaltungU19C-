using System.Diagnostics;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Roster/Liste vom Server holen, speichern und öffnen (gemeinsam für die Dokumenten-Fenster).</summary>
internal static class RosterDownload
{
    public static async Task SaveAsync(IWin32Window owner, ApiClient api, string kind, string format, string name, Dictionary<string, string> query, Label status)
    {
        var suggestion = $"{name}-{DateTime.Now:yyyy-MM-dd}.{format}";
        using var dialog = new SaveFileDialog
        {
            Filter = format == "pdf" ? "PDF|*.pdf" : "Excel|*.xlsx",
            FileName = string.Concat(suggestion.Split(Path.GetInvalidFileNameChars())),
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;

        status.ForeColor = Theme.Muted;
        status.Text = "Liste wird erstellt …";
        try
        {
            var data = await api.DownloadRosterAsync(kind, format, query);
            await File.WriteAllBytesAsync(dialog.FileName, data);
            status.ForeColor = Theme.Green;
            status.Text = "Gespeichert: " + dialog.FileName;
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            status.ForeColor = Theme.Danger;
            status.Text = ex.Message;
        }
    }

    /// <summary>Leiste unten mit „Als PDF“, „Als Excel“, Statusanzeige und „Schließen“.</summary>
    public static Panel BuildBar(Form owner, ApiClient api, string kind, string name, Func<Dictionary<string, string>> query)
    {
        var status = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(8, 9, 0, 0), MaximumSize = new Size(420, 0) };
        var pdf = Theme.MakeButton("Liste als PDF …", primary: true);
        var excel = Theme.MakeButton("Liste als Excel …");
        var close = Theme.MakeButton("Schließen");
        pdf.Click += async (_, _) => { pdf.Enabled = excel.Enabled = false; await SaveAsync(owner, api, kind, "pdf", name, query(), status); pdf.Enabled = excel.Enabled = true; };
        excel.Click += async (_, _) => { pdf.Enabled = excel.Enabled = false; await SaveAsync(owner, api, kind, "xlsx", name, query(), status); pdf.Enabled = excel.Enabled = true; };
        close.Click += (_, _) => owner.Close();
        owner.CancelButton = close;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.Px(56), Padding = new Padding(12, 8, 12, 0), BackColor = Theme.Background };
        bar.Controls.AddRange(new Control[] { pdf, excel, close, status });
        return bar;
    }

    /// <summary>Tabelle im einheitlichen Stil mit Spalten (Titel, Gewicht).</summary>
    public static DataGridView BuildGrid(params (string Title, float Weight)[] columns)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        Theme.StyleGrid(grid);
        foreach (var (title, weight) in columns)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight });
        }
        return grid;
    }

    /// <summary>Name der Staff-Person als „Nachname Vorname“.</summary>
    public static string StaffName(Dictionary<string, string?> row) =>
        $"{(row.TryGetValue("nachname", out var n) ? n : "")} {(row.TryGetValue("vorname", out var v) ? v : "")}".Trim();

    public static string StaffValue(Dictionary<string, string?> row, string key) => row.TryGetValue(key, out var v) ? v ?? "" : "";
}
