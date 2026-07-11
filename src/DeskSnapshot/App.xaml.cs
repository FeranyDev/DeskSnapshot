using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;

namespace DeskSnapshot;

public partial class App : Application
{
    private Window? _window;
    internal static string StartupLogPath { get; } = Path.Combine(Path.GetTempPath(), "DeskSnapshot-startup.log");

    public App()
    {
        ResetLog();
        Log("App constructor: begin");
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log($"AppDomain unhandled: {args.ExceptionObject}");
        UnhandledException += (_, args) => Log($"XAML unhandled: {args.Exception}");
        InitializeComponent();
        Log("App constructor: InitializeComponent complete");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Log("OnLaunched: creating MainWindow");
            _window = new MainWindow();
            Log("OnLaunched: activating MainWindow");
            _window.Activate();
            Log("OnLaunched: complete");
        }
        catch (Exception exception)
        {
            Log($"OnLaunched failed: {exception}");
            throw;
        }
    }

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(StartupLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // 诊断日志不得影响应用启动。
        }
    }

    internal static void ApplyWindowIcon(AppWindow appWindow)
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DeskSnapshot.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
            else
            {
                Log($"Window icon not found: {iconPath}");
            }
        }
        catch (Exception exception)
        {
            Log($"Unable to apply window icon: {exception.Message}");
        }
    }

    private static void ResetLog()
    {
        try
        {
            File.WriteAllText(StartupLogPath, string.Empty);
        }
        catch
        {
            // 诊断日志不得影响应用启动。
        }
    }
}
