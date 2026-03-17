using System;
using System.Windows;
using Velopack;

namespace TRVisionAI.Desktop;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack DEBE inicializarse antes que cualquier otra cosa
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
