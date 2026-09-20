using MitgliederverwaltungU19.Forms;
using MitgliederverwaltungU19.Services;

namespace MitgliederverwaltungU19;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(Theme.Body);
        var settings = AppSettings.Load();

        while (true)
        {
            if (!settings.IsConfigured)
            {
                using var setup = new SettingsForm(settings, firstRun: true);
                if (setup.ShowDialog() != DialogResult.OK) return;
            }

            try
            {
                var api = new ApiClient(settings);
                // Task.Run: kein Deadlock, auch wenn bereits ein UI-Kontext existiert
                var ping = Task.Run(() => api.PingAsync()).GetAwaiter().GetResult();
                if (!RunLicenseGate(settings, api)) return;
                Application.Run(new MainForm(settings, api, ping));
                return;
            }
            catch (Exception ex)
            {
                var retry = MessageBox.Show(
                    "Verbindung zum Server fehlgeschlagen:\n\n" + ex.Message + "\n\nEinstellungen jetzt anpassen?",
                    "Verbindungsfehler", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (retry != DialogResult.Yes) return;

                using var form = new SettingsForm(settings, firstRun: false);
                if (form.ShowDialog() != DialogResult.OK) return;
            }
        }
    }

    /// <summary>
    /// Ohne gültigen Lizenzschlüssel startet die Anwendung nicht: prüft beim Server und fragt bei Bedarf nach dem Schlüssel.
    /// </summary>
    private static bool RunLicenseGate(AppSettings settings, ApiClient api)
    {
        var check = Task.Run(() => LicenseService.CheckAsync(settings, api)).GetAwaiter().GetResult();
        while (!check.Allowed)
        {
            var message = check.Status == LicenseStatus.NoKey ? "" : check.Message;
            using var form = new LicenseForm(settings, api, message, mustActivate: true);
            if (form.ShowDialog() != DialogResult.OK) return false;
            check = Task.Run(() => LicenseService.CheckAsync(settings, api)).GetAwaiter().GetResult();
        }
        return true;
    }
}
