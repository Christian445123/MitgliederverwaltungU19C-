using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>Persönlicher Zugangslink eines Mitglieds: ansehen, kopieren, neu erzeugen, Zugangscode neu erzeugen, per E-Mail senden.</summary>
public sealed class MemberLinkForm : Form
{
    private readonly ApiClient _api;
    private readonly int _memberId;
    private readonly string _entity;
    private readonly TextBox _link = new() { Dock = DockStyle.Top, ReadOnly = true, BackColor = Color.FromArgb(0xF3, 0xF4, 0xF8) };
    private readonly TextBox _code = new() { Dock = DockStyle.Top, ReadOnly = true, BackColor = Color.FromArgb(0xF3, 0xF4, 0xF8), Font = new Font("Consolas", 12f * Theme.Zoom, FontStyle.Bold) };
    private readonly Label _who = new() { AutoSize = true, MaximumSize = new Size(560, 0), Font = Theme.Bold };
    private readonly Label _verified = new() { AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 6, 0, 0) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 10, 0, 0) };
    private readonly Panel _codeBox = new() { AutoSize = true, Visible = false, Dock = DockStyle.Top };

    public MemberLinkForm(ApiClient api, Member member) : this(api, "members", member.Id, member.FullName)
    {
    }

    public MemberLinkForm(ApiClient api, string entity, int id, string fullName)
    {
        _api = api;
        _memberId = id;
        _entity = entity;
        Text = "Zugangslink – " + fullName;
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(640, 540);

        var copy = Theme.MakeButton("Link kopieren");
        copy.Click += (_, _) => { if (_link.Text.Length > 0) Clipboard.SetText(_link.Text); _status.ForeColor = Theme.Green; _status.Text = "Link kopiert."; };
        var copyCode = Theme.MakeButton("Code kopieren");
        copyCode.Click += (_, _) => { if (_code.Text.Length > 0) Clipboard.SetText(_code.Text); };
        var newLink = Theme.MakeButton("Neuen Link erzeugen");
        var newCode = Theme.MakeButton("Neuen Zugangscode erzeugen");
        var mail = Theme.MakeButton("Per E-Mail senden", primary: true);
        var reset = Theme.MakeButton("Bestätigung zurücksetzen");
        var export = Theme.MakeButton("Auskunft (Datei) …");
        export.Click += async (_, _) => await ExportAsync();
        var close = Theme.MakeButton("Schließen");
        newLink.Click += async (_, _) => await ActAsync("regenerate_link", "Der bisherige Link wird ungültig. Neuen Link erzeugen?");
        newCode.Click += async (_, _) => await ActAsync("regenerate_password", "Der bisherige Zugangscode wird ungültig. Neuen Code erzeugen?");
        mail.Click += async (_, _) => await ActAsync("send_email", "Link und ein neuer Zugangscode werden per E-Mail an das Mitglied gesendet (der alte Code wird ungültig). Jetzt senden?");
        reset.Click += async (_, _) => await ActAsync("reset_verification", "Die Bestätigung wird zurückgesetzt. Die Person muss ihre Daten danach erneut bestätigen. Fortfahren?");
        close.Click += (_, _) => Close();
        CancelButton = close;

        var linkButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        linkButtons.Controls.AddRange(new Control[] { copy, newLink });

        var codeInner = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        codeInner.Controls.Add(new Label { Text = "Zugangscode (wird nur jetzt angezeigt)", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 14, 0, 2) });
        codeInner.Controls.Add(_code);
        var codeButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        codeButtons.Controls.Add(copyCode);
        codeInner.Controls.Add(codeButtons);
        _codeBox.Controls.Add(codeInner);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 16, 0, 0) };
        actions.Controls.AddRange(new Control[] { mail, newCode, reset, export, close });

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, AutoScroll = true };
        layout.Controls.Add(_who);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 8, 0, 0),
            Text = "Mit diesem Link kann das Mitglied seine Daten prüfen und korrigieren. Zum Öffnen braucht es zusätzlich seine E-Mail-Adresse und den Zugangscode.",
        });
        layout.Controls.Add(new Label { Text = "Persönlicher Link", AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 14, 0, 2) });
        layout.Controls.Add(_link);
        layout.Controls.Add(linkButtons);
        layout.Controls.Add(_verified);
        layout.Controls.Add(_codeBox);
        layout.Controls.Add(actions);
        layout.Controls.Add(_status);
        Controls.Add(layout);

        _who.Text = fullName;
        Shown += async (_, _) => await ActAsync("", "");
    }

    private void Show(LinkInfo info)
    {
        _who.Text = $"{info.Name} ({info.Email})";
        _link.Text = info.Link;
        _verified.Text = info.VerifiedAt is { Length: > 0 } v && DateTime.TryParse(v, out var d)
            ? $"Bestätigt: Die Daten wurden am {d:dd.MM.yyyy HH:mm} Uhr bestätigt."
            : "Die Daten wurden noch nicht bestätigt.";
        if (!string.IsNullOrEmpty(info.Password))
        {
            _code.Text = info.Password;
            _codeBox.Visible = true;
        }
        if (info.Message.Length > 0)
        {
            _status.ForeColor = Theme.Green;
            _status.Text = info.Message;
        }
    }

    /// <summary>Auskunft über alle gespeicherten Daten der Person (Art. 15 DSGVO) als JSON-Datei speichern.</summary>
    private async Task ExportAsync()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = $"auskunft-{_entity}-{_memberId}.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            await File.WriteAllTextAsync(dialog.FileName, await _api.GetDsgvoExportAsync(_entity, _memberId), System.Text.Encoding.UTF8);
            _status.ForeColor = Theme.Green;
            _status.Text = "Auskunft gespeichert. Bitte vertraulich behandeln und nach Versand löschen.";
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = ex.Message;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async Task ActAsync(string action, string confirm)
    {
        if (confirm.Length > 0 && MessageBox.Show(this, confirm, "Zugangslink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            UseWaitCursor = true;
            Show(await _api.GetLinkAsync(_entity, _memberId, action));
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = ex.Message;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
}
