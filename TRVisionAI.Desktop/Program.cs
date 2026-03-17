using System;
using System.Windows;
using Velopack;

namespace TRVisionAI.Desktop;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack MUST be initialized before anything else
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
