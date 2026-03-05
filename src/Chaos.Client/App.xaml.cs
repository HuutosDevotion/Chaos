using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Chaos.Client;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Chaos", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

        DispatcherUnhandledException += (_, args) =>
        {
            LogException("UI", args.Exception);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("Task", args.Exception);
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogException("AppDomain", args.ExceptionObject as Exception);
        };
    }

    private static void LogException(string source, Exception? ex)
    {
        try
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n";
            File.AppendAllText(LogPath, entry);
        }
        catch { }
    }
}
