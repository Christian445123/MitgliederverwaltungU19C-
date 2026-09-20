using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>
/// Abgelaufene bzw. bald ablaufende Dokumente, Spieler und Staff getrennt (zwei Register).
/// Spieler: NADA-Zertifikat und Reisepass; Staff: Reisepass. Die Liste lässt sich auch als PDF oder Excel erstellen.
/// </summary>
public sealed class ExpiryForm : Form
{
    public ExpiryForm(ApiClient api, IReadOnlyList<ExpiryItem> items, IReadOnlyList<Dictionary<string, string?>> staff)
    {
        Text = "Abgelaufene und ablaufende Dokumente";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 600);
        MinimumSize = new Size(640, 400);

        // Staff: nur Reisepass, gleiche Fristen wie bei den Spielern
        var today = DateTime.Today;
        var staffItems = new List<(ExpiryItem Item, string Position)>();
        foreach (var s in staff.Where(s => RosterDownload.StaffValue(s, "status") != "inaktiv"))
        {
            var pass = Expiry.Check(RosterDownload.StaffValue(s, "reisepass_gueltig_bis"), today.AddMonths(Expiry.PassWarnMonths), today);
            if (pass.State == ExpiryState.Ok || pass.Date is not { } date) continue;
            var member = new Member();
            member.Values["nachname"] = RosterDownload.StaffValue(s, "nachname");
            member.Values["vorname"] = RosterDownload.StaffValue(s, "vorname");
            staffItems.Add((new ExpiryItem(member, "Reisepass", date, pass.State, pass.Days), RosterDownload.StaffValue(s, "position")));
        }
        staffItems = staffItems.OrderBy(i => i.Item.Days).ThenBy(i => i.Item.Member.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();

        var expiredPlayers = items.Count(i => i.State == ExpiryState.Expired);
        var expiredStaff = staffItems.Count(i => i.Item.State == ExpiryState.Expired);
        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Px(66),
            Padding = new Padding(14, 12, 14, 0),
            ForeColor = expiredPlayers + expiredStaff > 0 ? Theme.Danger : Theme.AccentDark,
            Font = Theme.Bold,
            Text = $"{expiredPlayers + expiredStaff} abgelaufen, {items.Count + staffItems.Count - expiredPlayers - expiredStaff} laufen bald ab " +
                   $"(NADA: gelb ab {Expiry.NadaWarnMonths} Monat vorher, blau ab {Expiry.NadaUrgentDays} Tagen, rot am Ablauftag; Reisepass: ab {Expiry.PassWarnMonths} Monaten vorher).",
        };

        var players = RosterDownload.BuildGrid(("Name", 30), ("Dokument", 20), ("Ablaufdatum", 16), ("Status", 34));
        foreach (var item in items)
        {
            Color(players, players.Rows.Add(item.Member.FullName, item.Document, item.Date.ToString("dd.MM.yyyy"), item.Describe()), item.State);
        }

        var staffGrid = RosterDownload.BuildGrid(("Name", 30), ("Position", 16), ("Ablaufdatum", 16), ("Status", 38));
        foreach (var (item, position) in staffItems)
        {
            Color(staffGrid, staffGrid.Rows.Add(item.Member.FullName, position, item.Date.ToString("dd.MM.yyyy"), item.Describe()), item.State);
        }

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.Bold };
        tabs.TabPages.Add(TabPage($"Spieler ({items.Count})", players, items.Count == 0 ? "Bei allen Spielern ist nichts abgelaufen." : ""));
        tabs.TabPages.Add(TabPage($"Staff ({staffItems.Count})", staffGrid, staffItems.Count == 0 ? "Bei allen Staff-Personen ist nichts abgelaufen." : ""));

        Controls.Add(tabs);
        Controls.Add(info);
        Controls.Add(RosterDownload.BuildBar(this, api, "-abgelaufen", "Abgelaufene-Dokumente", () => new Dictionary<string, string> { ["kader"] = "kader" }));
    }

    private static void Color(DataGridView grid, int index, ExpiryState state)
    {
        var (back, fore) = Expiry.Colors(state);
        grid.Rows[index].DefaultCellStyle.BackColor = back;
        grid.Rows[index].DefaultCellStyle.ForeColor = fore;
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
}
