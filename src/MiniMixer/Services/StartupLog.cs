using System.IO;

namespace MiniMixer.Services;

/// <summary>
/// Registro breve de arranques y errores: %APPDATA%\MiniMixer\mini-mixer.log
/// Sirve para saber qué pasó si al encender el PC Mini-Mixer no aparece. Se recorta solo (máx. ~200 KB).
/// </summary>
public static class StartupLog
{
    private static readonly object Lock = new();

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniMixer", "mini-mixer.log");

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 200_000)
                {
                    var tail = File.ReadAllLines(FilePath).TakeLast(300).ToArray();
                    File.WriteAllLines(FilePath, tail);
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch { /* el registro nunca debe impedir que la app funcione */ }
    }
}
