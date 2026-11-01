using System.IO;
using System.Windows;

namespace TECNO.GalleryBridge;

public partial class App : System.Windows.Application
{
    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TECNO Gallery Bridge", "startup-error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            ReportError(args.Exception);
            args.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) WriteLog(exception);
        };

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            ReportError(ex);
            Shutdown(1);
        }
    }

    private static void ReportError(Exception exception)
    {
        WriteLog(exception);
        System.Windows.MessageBox.Show($"TECNO Gallery Bridge couldn't start.\n\n{exception.Message}\n\nError log:\n{LogPath}",
            "Startup error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }

    private static void WriteLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {exception}\n\n");
        }
        catch { /* Never let diagnostics trigger another startup crash. */ }
    }
}
