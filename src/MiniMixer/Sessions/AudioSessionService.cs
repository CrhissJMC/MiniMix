using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces; // AudioSessionState

namespace MiniMixer.Sessions;

/// <summary>Todas las sesiones de audio de un mismo proceso (puede tener una por dispositivo).</summary>
/// <param name="AppKey">Identificador estable entre aperturas: nombre del ejecutable en minúsculas ("brave").</param>
public sealed record AppAudioGroup(uint ProcessId, string DisplayName, bool IsSystemSounds,
                                   IReadOnlyList<AudioSessionControl> Sessions,
                                   IReadOnlyList<string> DeviceIds, IReadOnlyList<string> DeviceNames,
                                   string AppKey);

/// <summary>
/// Envoltorio del Audio Session Manager de Windows (IAudioSessionManager2).
///
/// Cada endpoint de salida tiene su propio gestor de sesiones. Una "sesión" es el
/// conjunto de streams que una app abre en ese endpoint, y expone:
///   - ISimpleAudioVolume    → volumen 0..1 y mute (lo mismo que el Mezclador de volumen de Windows)
///   - IAudioMeterInformation → pico actual (para el VU)
///   - IAudioSessionControl2  → PID, estado (activa/inactiva/expirada), nombre
///
/// Se hace sondeo (polling) cada ~1 s en el hilo de UI: es simple, robusto y barato.
/// Alternativa: AudioSessionManager.OnSessionCreated + RegisterEventClient por sesión.
/// </summary>
public sealed class AudioSessionService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<uint, string> _nameCache = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private List<MMDevice> _devices = new();
    private DateTime _lastDeviceScan = DateTime.MinValue;

    public IReadOnlyDictionary<uint, AppAudioGroup> Poll()
    {
        if (DateTime.UtcNow - _lastDeviceScan > TimeSpan.FromSeconds(5))
        {
            _devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            _lastDeviceScan = DateTime.UtcNow;
        }

        var map = new Dictionary<uint, (string Name, bool System, List<AudioSessionControl> List, List<(string Id, string Name)> Devices)>();

        foreach (var device in _devices)
        {
            try
            {
                string deviceId = device.ID, deviceName = MiniMixer.Audio.DeviceCatalog.NameOf(device);
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    if (s.State == AudioSessionState.AudioSessionStateExpired) continue;

                    uint pid = s.GetProcessID;
                    if (pid == _ownPid) continue; // nunca listarnos a nosotros (evita realimentación)

                    if (!map.TryGetValue(pid, out var entry))
                    {
                        entry = (ResolveName(pid, s), s.IsSystemSoundsSession, new List<AudioSessionControl>(),
                                 new List<(string, string)>());
                        map[pid] = entry;
                    }
                    entry.List.Add(s);
                    if (!entry.Devices.Any(d => d.Id == deviceId)) entry.Devices.Add((deviceId, deviceName));
                }
            }
            catch (COMException)
            {
                // El dispositivo desapareció entre escaneos; se re-escanea en el próximo ciclo.
                _lastDeviceScan = DateTime.MinValue;
            }
        }

        return map.ToDictionary(kv => kv.Key,
            kv => new AppAudioGroup(kv.Key, kv.Value.Name, kv.Value.System, kv.Value.List,
                kv.Value.Devices.Select(d => d.Id).ToList(), kv.Value.Devices.Select(d => d.Name).ToList(),
                ResolveKey(kv.Key, kv.Value.System)));
    }

    private readonly Dictionary<uint, string> _keyCache = new();

    /// <summary>Nombre del ejecutable (sin ".exe", minúsculas); el PID cambia en cada apertura, esto no.</summary>
    private string ResolveKey(uint pid, bool isSystemSounds)
    {
        if (isSystemSounds) return "__sonidos_del_sistema";
        if (_keyCache.TryGetValue(pid, out var cached)) return cached;
        string key;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            key = p.ProcessName.ToLowerInvariant();
        }
        catch { key = $"pid{pid}"; }
        _keyCache[pid] = key;
        return key;
    }

    private string ResolveName(uint pid, AudioSessionControl session)
    {
        if (session.IsSystemSoundsSession) return "Sonidos del sistema";
        if (_nameCache.TryGetValue(pid, out var cached)) return cached;

        string? name = null;
        string display = session.DisplayName;
        if (!string.IsNullOrWhiteSpace(display) && !display.StartsWith('@')) name = display;

        if (name == null)
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                try { name = p.MainModule?.FileVersionInfo.FileDescription; }
                catch { /* acceso denegado a procesos elevados */ }
                if (string.IsNullOrWhiteSpace(name)) name = p.ProcessName;
            }
            catch { name = $"PID {pid}"; }
        }

        _nameCache[pid] = name!;
        return name!;
    }
}
