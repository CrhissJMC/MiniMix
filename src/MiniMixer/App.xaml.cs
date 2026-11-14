using System.Windows;
using System.Windows.Threading;
using MiniMixer.Services;

namespace MiniMixer;

/// <summary>
/// Ciclo de vida:
///  - Una sola instancia: si se abre otra vez, se muestra la ventana de la que ya está corriendo.
///  - Cerrar la ventana la oculta; la app sigue en la bandeja (el audio no se corta).
///  - "Salir" del menú de la bandeja (o apagar Windows) la cierra de verdad.
///  - Con "--tray" (inicio automático con Windows) espera a que el audio esté listo y abre la ventana, o se
///    queda en la bandeja con un aviso (Configuración › Inicio).
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

    /// <summary>true sólo en la instancia principal (las que sólo avisan a otra no guardan nada).</summary>
    private bool _isPrimary;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;

        bool exitRequest = e.Args.Any(a => a.Equals(ExitArgument, StringComparison.OrdinalIgnoreCase));
        bool fromWindowsStartup = e.Args.Any(a => a.Equals(StartupRegistration.TrayArgument, StringComparison.OrdinalIgnoreCase));

        // Configuración y reparación del inicio con Windows en CUALQUIER apertura, también cuando ya hay
        // otra instancia (antes sólo lo hacía la principal, y la entrada podía no llegar a registrarse).
        AppSettings.Load();
        if (!exitRequest) StartupRegistration.Sync();

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

        _isPrimary = true;
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartupLog.Write($"Inicio v{typeof(App).Assembly.GetName().Version} ({(fromWindowsStartup ? "arranque de Windows" : "abierta por el usuario")})");

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.InvokeAsync(ShowMainWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent,
            (_, _) => Dispatcher.InvokeAsync(ExitApplication), null, Timeout.Infinite, executeOnlyOnce: true);

        Audio.AudioEngine.Configure(AppSettings.Current.SampleRate);

        // El icono de la bandeja primero: se ve en cuanto arranca, aunque el audio aún no esté listo
        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMainWindow;
        _tray.ExitRequested += ExitApplication;

        // Al encender el PC el servicio de audio de Windows (y los dispositivos virtuales) pueden tardar en
        // estar listos: se espera en vez de arrancar sin dispositivos o fallar.
        if (fromWindowsStartup) await WaitForAudioAsync();

        try
        {
            _window = new MainWindow();
        }
        catch (Exception ex)
        {
            StartupLog.Write("Error al crear el mezclador, reintento en 5 s: " + ex);
            await Task.Delay(5000);
            _window = new MainWindow();
        }
        _window.Closing += OnWindowClosing;
        StartupLog.Write("Mezclador listo");

        if (!fromWindowsStartup || AppSettings.Current.StartupShowWindow)
        {
            ShowMainWindow();
        }
        else
        {
            _tray.ShowBalloon("Mini-Mixer está funcionando",
                "Está en la bandeja del sistema (junto al reloj; si no lo ves, mira dentro de la flecha ^). Haz clic en el icono para abrirlo.");
        }
    }

    /// <summary>
    /// Espera (máx. ~90 s) a que haya dispositivos de salida activos y, después, hasta ~20 s más a que
    /// aparezcan los dispositivos guardados en la configuración (los virtuales suelen cargarse más tarde).
    /// </summary>
    private static async Task WaitForAudioAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(90))
        {
            try
            {
                var en = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                if (en.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active).Count > 0) break;
            }
            catch { /* servicio de audio aún arrancando */ }
            await Task.Delay(2000);
        }
        StartupLog.Write($"Audio de Windows listo tras {sw.Elapsed.TotalSeconds:0.0} s");

        var saved = AppSettings.Current.Devices.Values
            .Where(v => v != AppSettings.NoneKey)
            .Select(v => v.StartsWith("loop|", StringComparison.Ordinal) ? v[5..] : v)
            .Distinct().ToList();
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (saved.Count > 0 && wait.Elapsed < TimeSpan.FromSeconds(20))
        {
            var missing = saved.Where(id => !Audio.DeviceCatalog.IsActive(id)).ToList();
            if (missing.Count == 0) break;
            await Task.Delay(2000);
        }
        StartupLog.Write($"Dispositivos guardados disponibles tras {wait.Elapsed.TotalSeconds:0.0} s más");
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
        if (_isPrimary) AppSettings.Current.Save(); // por si quedaba un guardado diferido pendiente
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
        StartupLog.Write("Error: " + e.Exception);
        MessageBox.Show(e.Exception.Message, "Mini-Mixer", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
