using Microsoft.Win32;

namespace MiniMixer.Services;

/// <summary>
/// Inicio automático con Windows mediante HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// (el mismo mecanismo que Discord o Steam; aparece en Administrador de tareas › Aplicaciones de arranque).
/// Arranca con "--tray": directo a la bandeja.
///
/// La preferencia vive en settings.json (AppSettings.StartWithWindows) y en cada arranque se sincroniza
/// el registro con ella, así la entrada se repara sola si se borró o si el .exe cambió de carpeta.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "MiniMixer";
    public const string TrayArgument = "--tray";

    private static string Command => $"\"{Environment.ProcessPath}\" {TrayArgument}";

    /// <summary>true si Windows tiene registrada la entrada de inicio.</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>Cambio hecho por el usuario (bandeja o Configuración): se guarda y se aplica.</summary>
    public static void SetEnabled(bool enabled)
    {
        AppSettings.Current.StartWithWindows = enabled;
        AppSettings.Current.Save();
        Apply(enabled);

        // Si estaba deshabilitada en el Administrador de tareas, al activarla aquí se vuelve a habilitar
        if (enabled)
        {
            using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey, writable: true);
            if (approved.GetValue(ValueName) is byte[] state && state.Length > 0 && state[0] != 2)
                approved.SetValue(ValueName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
    }

    /// <summary>Al arrancar: deja el registro como dice la configuración (por defecto, activado).</summary>
    public static void Sync()
    {
        try { Apply(AppSettings.Current.StartWithWindows); }
        catch { /* sin permisos sobre el registro: se ignora */ }
    }

    private static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            if (key.GetValue(ValueName) as string != Command) key.SetValue(ValueName, Command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
