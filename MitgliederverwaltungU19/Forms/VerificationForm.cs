using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Eine Person für die Massenaktion (Spieler oder Staff).</summary>
public sealed record VerifyPerson(int Id, string Name, bool Active, bool HasEmail, bool Confirmed);

/// <summary>
/// Daten bestätigen lassen: Bestätigung zurücksetzen und/oder Link mit neuem Zugangscode per Massenmail senden,
/// für die Auswahl, alle noch nicht bestätigten oder alle aktiven Personen.
/// </summary>
public sealed class VerificationForm : Form
{
    private const int MailBatch = 8; // Server erlaubt höchstens 10 Empfänger pro Aufruf

    private readonly ApiClient _api;
    private readonly string _entity;
    private readonly List<VerifyPerson> _all;
    private readonly List<VerifyPerson> _selected;
    private readonly RadioButton _rSel = new() { AutoSize = true };
    private readonly RadioButton _rPending = new() { AutoSize = true };
    private readonly RadioButton _rAll = new() { AutoSize = true };
    private readonly CheckBox _doReset = new() { AutoSize = true, Checked = true, Text = "Bestätigung zurücksetzen (alle müssen ihre Daten erneut bestätigen)" };
    private readonly CheckBox _doMail = new() { AutoSize = true, Checked = true, Text = "Link und neuen Zugangscode per E-Mail senden (Massenmail)" };
    private readonly ProgressBar _progress = new() { Width = 540, Height = 14, Visible = false };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 8, 0, 0) };
    private readonly ListBox _problems = new() { Width = 540, Height = 110, Visible = false, IntegralHeight = false };
    private readonly Button _run = Theme.MakeButton("Ausführen", primary: true);
    private readonly Button _close = Theme.MakeButton("Schließen");

    /// <summary>True, wenn etwas geändert wurde (Aufrufer lädt dann die Liste neu).</summary>
    public bool Changed { get; private set; }

    public VerificationForm(ApiClient api, string entity, List<VerifyPerson> all, List<VerifyPerson> selected)
    {
        _api = api;
        _entity = entity;
        _all = all;
        _selected = selected;
        var noun = entity == "staff" ? "Staff-Personen" : "Spieler";
        Text = "Daten bestätigen lassen – " + noun;
        Font = Theme.Body;
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(600, 560);

        var active = _all.Where(p => p.Active).ToList();
        var pending = active.Where(p => !p.Confirmed).ToList();
        static string Mail(IEnumerable<VerifyPerson> l)
        {
            var list = l.ToList();
            return $"{list.Count}, davon {list.Count(p => p.HasEmail)} mit E-Mail";
        }
        _rSel.Text = $"Ausgewählte ({Mail(_selected)})";
        _rPending.Text = $"Alle aktiven ohne Bestätigung ({Mail(pending)})";
        _rAll.Text = $"Alle aktiven {noun} ({Mail(active)})";
        _rSel.Enabled = _selected.Count > 0;
        if (_selected.Count > 0) _rSel.Checked = true; else _rAll.Checked = true;

        _run.Click += async (_, _) => await RunAsync();
        _close.Click += (_, _) => Close();
        CancelButton = _close;

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = Theme.Muted,
            Text = "Setzt die Bestätigung zurück, damit jede Person ihre Daten über den persönlichen Link erneut prüfen und bestätigen muss, " +
                   "und/oder sendet allen Link und einen neuen Zugangscode per E-Mail. Personen ohne E-Mail-Adresse erhalten keine Mail.",
        };

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 18, 20, 12), AutoScroll = true };
        layout.Controls.Add(intro);
        layout.Controls.Add(new Label { Text = "Für wen?", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 16, 0, 4) });
        layout.Controls.AddRange(new Control[] { _rSel, _rPending, _rAll });
        layout.Controls.Add(new Label { Text = "Aktion", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 16, 0, 4) });
        layout.Controls.AddRange(new Control[] { _doReset, _doMail });
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0) };
        buttons.Controls.AddRange(new Control[] { _run, _close });
        layout.Controls.Add(buttons);
        _progress.Margin = new Padding(0, 12, 0, 0);
        layout.Controls.Add(_progress);
        layout.Controls.Add(_status);
        _problems.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(_problems);
        Controls.Add(layout);
    }

    private List<VerifyPerson> Scope() =>
        _rSel.Checked ? _selected
        : _rPending.Checked ? _all.Where(p => p.Active && !p.Confirmed).ToList()
        : _all.Where(p => p.Active).ToList();

    private async Task RunAsync()
    {
        var scope = Scope();
        if (scope.Count == 0)
        {
            MessageBox.Show(this, "Für diese Auswahl gibt es keine Personen.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_doReset.Checked && !_doMail.Checked)
        {
            MessageBox.Show(this, "Bitte mindestens eine Aktion wählen.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var what = string.Join(" und ", new[]
        {
            _doReset.Checked ? "Bestätigung zurücksetzen" : null,
            _doMail.Checked ? $"E-Mail an {scope.Count(p => p.HasEmail)} Person(en) senden" : null,
        }.Where(s => s is not null));
        if (MessageBox.Show(this, $"{scope.Count} Person(en): {what}. Jetzt ausführen?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _run.Enabled = _close.Enabled = false;
        _problems.Items.Clear();
        _problems.Visible = false;
        _status.ForeColor = Theme.Muted;
        try
        {
            UseWaitCursor = true;
            var ids = scope.Select(p => p.Id).ToList();
            var summary = new List<string>();

            if (_doReset.Checked)
            {
                _status.Text = "Setze Bestätigung zurück …";
                var count = await _api.ResetVerificationAsync(_entity, ids);
                Changed = true;
                summary.Add($"Bestätigung zurückgesetzt bei {count} Person(en).");
            }

            if (_doMail.Checked)
            {
                var mailable = scope.Where(p => p.HasEmail).Select(p => p.Id).ToList();
                var sent = 0;
                var failed = 0;
                _progress.Maximum = Math.Max(1, mailable.Count);
                _progress.Value = 0;
                _progress.Visible = true;
                for (var i = 0; i < mailable.Count; i += MailBatch)
                {
                    var batch = mailable.Skip(i).Take(MailBatch).ToList();
                    _status.Text = $"Sende E-Mails … {i} von {mailable.Count}";
                    foreach (var r in await _api.SendLinksAsync(_entity, batch))
                    {
                        if (r.Status == "sent") sent++;
                        else
                        {
                            failed++;
                            _problems.Items.Add($"{r.Name}: {r.Message}");
                        }
                    }
                    _progress.Value = Math.Min(_progress.Maximum, i + batch.Count);
                }
                summary.Add($"E-Mails: {sent} gesendet, {failed} fehlgeschlagen, {scope.Count - mailable.Count} ohne E-Mail-Adresse.");
                _problems.Visible = failed > 0;
            }

            _status.ForeColor = Theme.Green;
            _status.Text = string.Join("\n", summary);
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = ex.Message;
        }
        finally
        {
            UseWaitCursor = false;
            _run.Enabled = _close.Enabled = true;
            _progress.Visible = false;
        }
    }
}
