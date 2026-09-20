using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>
/// Fehlende Dokumente, Spieler und Staff getrennt. Spieler (ohne Personen mit Staff-Position): NADA-Zertifikat, Reisepass, E-Card, Rechte &amp; Pflichten;
/// Staff: Rechte &amp; Pflichten und Foto des Reisepasses sind freiwillig (nur Hinweis). Die Liste lässt sich auch als PDF oder Excel erstellen.
/// </summary>
public sealed class MissingDocsForm : Form
{
    public MissingDocsForm(ApiClient api, IReadOnlyList<Member> members, IReadOnlyList<Dictionary<string, string?>> staff)
    {
        Text = "Fehlende Dokumente";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 600);
        MinimumSize = new Size(640, 400);

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Px(52),
            Padding = new Padding(14, 12, 14, 0),
            ForeColor = Theme.Muted,
            Text = "Ein Dokument fehlt, wenn keine Datei hochgeladen ist oder es mit „Fehlt“ markiert wurde. " +
                   "E-Card und Reisepass haben nur eine Vorderseite.",
        };

        // Register Spieler
        var players = RosterDownload.BuildGrid(("Name", 28), ("NADA-Zertifikat", 18), ("Reisepass", 18), ("E-Card", 18), ("Rechte & Pflichten", 18));
        foreach (var m in members)
        {
            var cells = new List<object> { m.FullName };
            foreach (var type in new[] { "nada", "pass", "ecard", "rechte" })
            {
                var missing = m.MissingDocuments.Contains(type);
                var marked = m.MissingFlags.TryGetValue(type, out var f) && f;
                cells.Add(missing ? (marked ? "✗ fehlt (markiert)" : "✗ fehlt") : "✓");
            }
            Mark(players, players.Rows.Add(cells.ToArray()));
        }

        // Register Staff: nur Personen aus dem Staff. Rechte & Pflichten, Reisepass und E-Card sind freiwillig,
        // deshalb steht dort „nicht hochgeladen“ (Hinweis) und nicht „fehlt“.
        var staffGrid = RosterDownload.BuildGrid(("Name", 28), ("Position", 12), ("Rechte & Pflichten", 20), ("Reisepass", 20), ("E-Card", 20));
        static bool Has(Dictionary<string, string?> s, string type) => RosterDownload.StaffValue(s, "dokument_" + type) == "true";
        var staffOpen = staff
            .Where(s => RosterDownload.StaffValue(s, "status") != "inaktiv" && (!Has(s, "rechte") || !Has(s, "pass") || !Has(s, "ecard")))
            .OrderBy(s => RosterDownload.StaffName(s), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var s in staffOpen)
        {
            var i = staffGrid.Rows.Add(RosterDownload.StaffName(s), RosterDownload.StaffValue(s, "position"),
                Has(s, "rechte") ? "✓" : "nicht hochgeladen", Has(s, "pass") ? "✓" : "nicht hochgeladen", Has(s, "ecard") ? "✓" : "nicht hochgeladen");
            for (var c = 2; c <= 4; c++)
            {
                var cell = staffGrid.Rows[i].Cells[c];
                var open = cell.Value?.ToString() != "✓";
                cell.Style.ForeColor = open ? Theme.AccentDark : Theme.Green;
            }
        }

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.Bold };
        tabs.TabPages.Add(TabPage($"Spieler ({members.Count})", players, members.Count == 0 ? "Bei allen Spielern im Kader sind die Dokumente vollständig." : ""));
        tabs.TabPages.Add(TabPage($"Staff ({staffOpen.Count})", staffGrid, staffOpen.Count == 0 ? "Beim Staff ist alles hochgeladen (die Dokumente sind freiwillig)." : ""));

        Controls.Add(tabs);
        Controls.Add(info);
        Controls.Add(RosterDownload.BuildBar(this, api, "-fehlend", "Fehlende-Dokumente", () => new Dictionary<string, string> { ["kader"] = "kader" }));
    }

    private static TabPage TabPage(string title, DataGridView grid, string emptyHint)
    {
        var page = new TabPage(title) { BackColor = Theme.Background, Padding = new Padding(0, 8, 0, 0) };
        page.Controls.Add(grid);
        if (emptyHint.Length > 0)
        {
            page.Controls.Add(new Label { Dock = DockStyle.Top, Height = Theme.Px(34), ForeColor = Theme.Green, Font = Theme.Bold, Text = "✓ " + emptyHint, TextAlign = ContentAlignment.MiddleLeft });
        }
        return page;
    }

    private static void Mark(DataGridView grid, int rowIndex)
    {
        for (var c = 1; c < grid.Columns.Count; c++)
        {
            var cell = grid.Rows[rowIndex].Cells[c];
            var value = cell.Value?.ToString() ?? "";
            if (value.Length == 0) continue;
            var isMissing = value.StartsWith('✗');
            cell.Style.ForeColor = isMissing ? Theme.Danger : Theme.Green;
            cell.Style.BackColor = isMissing ? Color.FromArgb(0xFE, 0xE2, 0xE2) : Color.White;
        }
    }
}
