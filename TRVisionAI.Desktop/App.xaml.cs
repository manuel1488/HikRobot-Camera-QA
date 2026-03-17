using System;
using System.Windows;
using TRVisionAI.Data;
using Velopack;
using Velopack.Sources;

namespace TRVisionAI.Desktop;

public partial class App : Application
{
    // Update server URL — change once the VPS is ready
    private const string UpdateUrl = "https://releases.tworockets.com.mx/trvisionai";

    public static InspectionDbService DbService { get; private set; } = null!;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        DbService = InspectionDbService.Create();
        await DbService.EnsureDatabaseAsync();

        var home = new Views.HomeWindow();
        home.Show();

        await CheckForUpdatesAsync();
    }

    private static async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new UpdateManager(new SimpleWebSource(UpdateUrl));

            if (!mgr.IsInstalled)
                return;

            var update = await mgr.CheckForUpdatesAsync();
            if (update is null)
                return;

            var result = MessageBox.Show(
                $"Hay una nueva versión disponible: {update.TargetFullRelease.Version}\n\n¿Deseas actualizar ahora?",
                "Actualización disponible",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                await mgr.DownloadUpdatesAsync(update);
                mgr.ApplyUpdatesAndRestart(update);
            }
        }
        catch
        {
            // No connection or server unavailable — continue normally
        }
    }
}
