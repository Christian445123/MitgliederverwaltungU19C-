using MitgliederverwaltungU19.Models;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19.Forms;

/// <summary>
/// Pflichtfelder bei der Selbstanmeldung über den Registrierungslink einstellen (Spieler und Staff getrennt),
/// wie im Web-Panel unter „Neue Mitglieder“ → Pflichtfelder. Nachname/Vorname/Telefon/Mail sind immer Pflicht
/// und werden hier nicht aufgeführt.
/// </summary>
public sealed class RegistrationFieldsForm : Form
{
    private readonly ApiClient _api;
    private readonly CheckedListBox _playerList = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly CheckedListBox _staffList = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private RegistrationFieldSet? _playerSet;
    private RegistrationFieldSet? _staffSet;

    public RegistrationFieldsForm(ApiClient api)
    {
        _api = api;
        Text = "Pflichtfelder bei der Anmeldung";
        Theme.Prepare(this);
        Icon = Theme.AppIcon;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 620);
        MinimumSize = new Size(420, 420);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Padding = new Padding(16, 12, 16, 0),
            ForeColor = Theme.Muted,
            Text = "Angehakte Felder müssen beim öffentlichen Registrierungslink ausgefüllt werden, bevor die Anmeldung abgeschickt werden kann. " +
                   "Nachname, Vorname, Telefon und Mail sind immer Pflicht.",
        };

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        var playerPage = new TabPage("Spieler") { BackColor = Color.White, Padding = new Padding(14) };
        playerPage.Controls.Add(_playerList);
        var staffPage = new TabPage("Staff") { BackColor = Color.White, Padding = new Padding(14) };
        staffPage.Controls.Add(_staffList);
        tabs.TabPages.Add(playerPage);
        tabs.TabPages.Add(staffPage);

        var save = Theme.MakeButton("Speichern", primary: true);
        save.Click += async (_, _) => await SaveAsync();
        var close = Theme.MakeButton("Schließen");
        close.Click += (_, _) => Close();
        CancelButton = close;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 10, 12, 10) };
        close.Margin = new Padding(8, 0, 0, 0);
        bar.Controls.Add(close);
        bar.Controls.Add(save);

        Controls.Add(tabs);
        Controls.Add(bar);
        Controls.Add(intro);
        Shown += async (_, _) => await LoadAsync();
    }

    private static void Fill(CheckedListBox list, RegistrationFieldSet set)
    {
        list.Items.Clear();
        foreach (var (key, label) in set.Registry)
        {
            list.Items.Add(label, set.Required.Contains(key));
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            (_playerSet, _staffSet) = await _api.GetRegistrationFieldsAsync();
            Fill(_playerList, _playerSet);
            Fill(_staffList, _staffSet);
        }
        catch (Exception ex)
        {
            Theme.ShowError(this, ex);
        }
    }

    private static List<string> CheckedKeys(CheckedListBox list, RegistrationFieldSet set) =>
        Enumerable.Range(0, list.Items.Count).Where(list.GetItemChecked).Select(i => set.Registry[i].Key).ToList();

    private async Task SaveAsync()
    {
        if (_playerSet is null || _staffSet is null) return;
        try
        {
            UseWaitCursor = true;
            await _api.SaveRegistrationFieldsAsync("player", CheckedKeys(_playerList, _playerSet));
            await _api.SaveRegistrationFieldsAsync("staff", CheckedKeys(_staffList, _staffSet));
            await LoadAsync();
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
}
