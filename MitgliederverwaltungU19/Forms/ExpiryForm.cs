using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Liste der abgelaufenen bzw. bald ablaufenden NADA-Zertifikate und Reisepässe.</summary>
public sealed class ExpiryForm : Form
{
    public ExpiryForm(IReadOnlyList<ExpiryItem> items)
    {
        Text = "Ablaufende Dokumente";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 480);
        MinimumSize = new Size(600, 320);

        var expired = items.Count(i => i.State == ExpiryState.Expired);
        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(14, 12, 14, 0),
            ForeColor = expired > 0 ? Theme.Danger : Theme.AccentDark,
            Font = Theme.Bold,
            Text = $"{expired} abgelaufen, {items.Count - expired} laufen bald ab " +
                   $"(NADA: gelb ab {Expiry.NadaWarnMonths} Monat vorher, blau ab {Expiry.NadaUrgentDays} Tagen, rot am Ablauftag; Reisepass: ab {Expiry.PassWarnMonths} Monaten vorher).",
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        Theme.StyleGrid(grid);
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Dokument", FillWeight = 20 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ablaufdatum", FillWeight = 16 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", FillWeight = 34 });

        foreach (var item in items)
        {
            var idx = grid.Rows.Add(item.Member.FullName, item.Document, item.Date.ToString("dd.MM.yyyy"), item.Describe());
            var (back, fore) = Expiry.Colors(item.State);
            grid.Rows[idx].DefaultCellStyle.BackColor = back;
            grid.Rows[idx].DefaultCellStyle.ForeColor = fore;
        }

        var close = Theme.MakeButton("Schließen", primary: true);
        close.Click += (_, _) => Close();
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 0),
            BackColor = Theme.Background,
        };
        bar.Controls.Add(close);
        AcceptButton = close;

        var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 8) };
        center.Controls.Add(grid);

        Controls.Add(center);
        Controls.Add(info);
        Controls.Add(bar);
    }
}
