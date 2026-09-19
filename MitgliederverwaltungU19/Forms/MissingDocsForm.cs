using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Liste der Spieler, bei denen NADA-Zertifikat, Reisepass, E-Card oder Rechte &amp; Pflichten fehlen.</summary>
public sealed class MissingDocsForm : Form
{
    public MissingDocsForm(IReadOnlyList<Member> members)
    {
        Text = "Fehlende Dokumente";
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 560);
        MinimumSize = new Size(640, 360);

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(14, 12, 14, 0),
            ForeColor = Theme.Muted,
            Text = $"{members.Count} Spieler im Kader mit fehlenden Dokumenten. Ein Dokument fehlt, wenn keine Datei hochgeladen ist " +
                   "oder es mit „Fehlt“ markiert wurde. Bei E-Card und Reisepass genügt Vorder- oder Rückseite.",
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        Theme.StyleGrid(grid);
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", FillWeight = 28 });
        foreach (var type in new[] { "nada", "pass", "ecard", "rechte" })
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Member.DocumentLabel(type), FillWeight = 18 });
        }

        foreach (var m in members)
        {
            var cells = new List<object> { m.FullName };
            foreach (var type in new[] { "nada", "pass", "ecard", "rechte" })
            {
                var missing = m.MissingDocuments.Contains(type);
                var marked = m.MissingFlags.TryGetValue(type, out var f) && f;
                cells.Add(missing ? (marked ? "✗ fehlt (markiert)" : "✗ fehlt") : "✓");
            }
            var i = grid.Rows.Add(cells.ToArray());
            for (var c = 1; c < grid.Columns.Count; c++)
            {
                var cell = grid.Rows[i].Cells[c];
                var isMissing = cell.Value?.ToString()?.StartsWith('✗') == true;
                cell.Style.ForeColor = isMissing ? Theme.Danger : Theme.Green;
                cell.Style.BackColor = isMissing ? Color.FromArgb(0xFE, 0xE2, 0xE2) : Color.White;
            }
        }

        var close = Theme.MakeButton("Schließen");
        close.DialogResult = DialogResult.OK;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 0) };
        bar.Controls.Add(close);
        CancelButton = close;

        Controls.Add(grid);
        Controls.Add(info);
        Controls.Add(bar);
    }
}
