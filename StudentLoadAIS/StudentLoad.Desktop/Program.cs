using StudentLoad.Desktop.Data;
using StudentLoad.Desktop.Forms;
using StudentLoad.Desktop.Services;

namespace StudentLoad.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var settings = ClientSettings.FromEnvironment();
            using var api = new StudentLoadApi(settings);
            var store = new OfflineStore(settings);
            var sessions = new SessionVault(settings);
            while (true)
            {
                using var login = new LoginForm(api, sessions);
                if (login.ShowDialog() != DialogResult.OK || login.Session is null)
                    break;

                using var main = new MainForm(api, store, login.Session, settings);
                Application.Run(main);
                if (!main.SignedOut)
                    break;
                sessions.Clear();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message, "Ошибка запуска АИС", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
