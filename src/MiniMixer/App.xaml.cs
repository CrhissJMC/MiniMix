using System.Windows;
using System.Windows.Threading;
using MiniMixer.Services;

namespace MiniMixer;

/// <summary>
/// Ciclo de vida:
///  - Una sola instancia: si se abre otra vez, se muestra la ventana de la que ya está corriendo.
///  - Cerrar la ventana la oculta; la app sigue en la bandeja (el audio no se corta).
///  - "Salir" del menú de la bandeja (o apagar Windows) la cierra de verdad.
///  - Con "--tray" (inicio automático con Windows) arranca sin mostrar la ventana.
/// </summary>
public partial class App : Application
{
    private const string MutexName = "MiniMixer.SingleInstance";
    private const string ShowEventName = "MiniMixer.ShowWindow";
    private const string ExitEventName = "MiniMixer.Exit";
    public const string ExitArgument = "--exit";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent, _exitEvent;
    private RegisteredWaitHandle? _showWait, _exitWait;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _balloonShown;

    /// <summary>true cuando hay que cerrar de verdad (Salir o apagado de Windows).</summary>
    public static bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;

        bool exitRequest = e.Args.Any(a => a.Equals(ExitArgument, StringComparison.OrdinalIgnoreCase));

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance || exitRequest)
        {
            // Ya hay una abierta: pedirle que se muestre (o que se cierre con --exit) y salir.
            try { using var ev = EventWaitHandle.OpenExisting(exitRequest ? ExitEventName : ShowEventName); ev.Set(); } catch { }
            if (isFirstInstance) _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.InvokeAsync(ShowMainWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent,
            (_, _) => Dispatcher.InvokeAsync(ExitApplication), null, Timeout.Infinite, executeOnlyOnce: true);

        // Configuración primero: de ella dependen el inicio con Windows y la frecuencia de mezcla
        AppSettings.Load();
        StartupRegistration.Sync();
        Audio.AudioEngine.Configure(AppSettings.Current.SampleRate);

        _window = new MainWindow();
        _window.Closing += OnWindowClosing;

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMainWindow;
        _tray.ExitRequested += ExitApplication;

        bool startHidden = e.Args.Any(a => a.Equals(StartupRegistration.TrayArgument, StringComparison.OrdinalIgnoreCase));
        if (!startHidden) ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_window == null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;  // traerla al frente aunque otra app tenga el foco
        _window.Topmost = false;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (IsExiting) return;

        // La X oculta la ventana; el mezclador sigue funcionando en segundo plano.
        e.Cancel = true;
        _window?.Hide();
        if (!_balloonShown)
        {
            _balloonShown = true;
            _tray?.ShowBalloon("Mini-Mixer sigue funcionando",
                "Está en la bandeja del sistema. Usa «Salir» en su menú para cerrarlo del todo.");
        }
    }

    private void ExitApplication()
    {
        IsExiting = true;
        _window?.Close();
        Shutdown();
    }

    private bool _restartRequested;

    /// <summary>Cierra y vuelve a abrir Mini-Mixer (para aplicar la frecuencia de muestreo).</summary>
    public void Restart()
    {
        _restartRequested = true;
        ExitApplication();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Apagado / cierre de sesión de Windows: cerrar sin bloquearlo.
        IsExiting = true;
        _window?.Close();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppSettings.Current.Save(); // por si quedaba un guardado diferido pendiente
        _tray?.Dispose();
        _showWait?.Unregister(null);
        _exitWait?.Unregister(null);
        _showEvent?.Dispose();
        _exitEvent?.Dispose();
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
        }

        // Reinicio: la nueva instancia arranca cuando esta ya liberó el mutex
        if (_restartRequested && Environment.ProcessPath is string exe)
            System.Diagnostics.Process.Start(exe);

        base.OnExit(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Mini-Mixer", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
