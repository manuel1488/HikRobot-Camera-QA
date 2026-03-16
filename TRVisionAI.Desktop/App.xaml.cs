using System.Windows;
using TRVisionAI.Data;

namespace TRVisionAI.Desktop;

public partial class App : Application
{
    public static InspectionDbService DbService { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DbService = InspectionDbService.Create();
        await DbService.EnsureDatabaseAsync();
    }
}
