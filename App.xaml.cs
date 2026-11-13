using Microsoft.UI.Xaml;

namespace BiliPlay;
public partial class App : Application
{
#if VISUAL_CHECKS
    public static string? VisualCheckDirectory { get; } = GetVisualCheckDirectory();
    public static bool IsVisualCheck => VisualCheckDirectory is not null;
    private static string? GetVisualCheckDirectory()
    {
        var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "--visual-check");
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
    }
#else
    public static string? VisualCheckDirectory => null;
    public static bool IsVisualCheck => false;
#endif
    private Services.SingleInstanceService? _instance;
    private MainWindow? _mainWindow;
    private bool _activationPending;
    public static Window Window { get; private set; } = null!;
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);
    public App()
    {
        Services.StartupTrace.Log("app-ctor-begin");
        UnhandledException += (_, e) =>
        {
            if(Services.DebugSession.IsActive)return;
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "startup-error.log"), $"{DateTimeOffset.Now:O} {e.Exception}\n"); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        };
        InitializeComponent();
        Services.StartupTrace.Log("app-ctor-end");
    }
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Services.StartupTrace.Log("onlaunched-begin");
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _instance = new(() => DispatcherQueue.TryEnqueue(() =>
        {
            if (_mainWindow is { } window) window.ShowMainWindow();
            else _activationPending = true;
        }));
        if (!_instance.IsPrimary)
        {
            _instance.Dispose(); _instance = null; Exit(); return;
        }
        _mainWindow = new MainWindow(); Window = _mainWindow;
        Window.Closed += (_, _) => { _instance?.Dispose(); _mainWindow = null; };
        Window.Activate();
        if (_activationPending) { _activationPending = false; _mainWindow.ShowMainWindow(); }
        Services.StartupTrace.Log("window-activated");
        // The abandoned-session scan is directory IO; keep it off the pre-first-frame path.
        if (!IsVisualCheck) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Services.DebugSession.CleanAbandoned);
    }
}
