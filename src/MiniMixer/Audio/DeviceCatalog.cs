using NAudio.CoreAudioApi;

namespace MiniMixer.Audio;

public sealed record DeviceOption(string Label, string? DeviceId, bool IsLoopback = false)
{
    public override string ToString() => Label;
}

/// <summary>Enumeración de endpoints WASAPI (usar desde el hilo de UI).</summary>
public static class DeviceCatalog
{
    private static readonly MMDeviceEnumerator Enumerator = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> NameCache = new();

    public static DeviceOption None { get; } = new("— ninguno —", null);

    /// <summary>
    /// Nombre del dispositivo, leído una sola vez. MMDevice.FriendlyName recorre todas las propiedades del
    /// endpoint y el driver tarda 35–65 ms por dispositivo (medido): sin caché, el sondeo de apps costaba
    /// ~550 ms de CPU por segundo.
    /// </summary>
    public static string NameOf(MMDevice device) =>
        NameCache.GetOrAdd(device.ID, _ => device.FriendlyName);

    public static List<DeviceOption> Outputs()
    {
        var list = new List<DeviceOption> { None };
        foreach (var d in Enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            list.Add(new DeviceOption(NameOf(d), d.ID));
        return list;
    }

    /// <param name="includeLoopback">Añade "Loopback · X" por cada salida (útil para entradas virtuales).</param>
    public static List<DeviceOption> Inputs(bool includeLoopback)
    {
        var list = new List<DeviceOption> { None };
        foreach (var d in Enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            list.Add(new DeviceOption(NameOf(d), d.ID));

        if (includeLoopback)
            foreach (var d in Enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                list.Add(new DeviceOption("Loopback · " + NameOf(d), d.ID, IsLoopback: true));
        return list;
    }

    /// <summary>
    /// Busca el lado "de reproducción" de un cable virtual para el micrófono virtual.
    /// VB-Cable funciona solo; los VAIO de Voicemeeter necesitan Voicemeeter abierto.
    /// </summary>
    public static string? FindVirtualCableId(IEnumerable<DeviceOption> outputs)
    {
        var list = outputs.Where(o => o.DeviceId != null).ToList();
        return (list.FirstOrDefault(o => o.Label.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(o => o.Label.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase)))
               ?.DeviceId;
    }

    /// <summary>Endpoint de reproducción que hace de "MixSalidas" (buscado por nombre).</summary>
    public static string? FindMixSalidasId(IEnumerable<DeviceOption> outputs) =>
        outputs.FirstOrDefault(o => o.DeviceId != null &&
                                    o.Label.Contains("MixSalidas", StringComparison.OrdinalIgnoreCase))?.DeviceId;

    /// <summary>true si el nombre parece un dispositivo virtual (no se debe usar como salida física).</summary>
    public static bool IsVirtual(string label) =>
        label.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase) ||
        label.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        label.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
        label.Contains("MixSalidas", StringComparison.OrdinalIgnoreCase);

    /// <summary>Salida física para A1: la predeterminada de Windows salvo que sea virtual (p.ej. MixSalidas).</summary>
    public static string? PickPhysicalDefault(IReadOnlyList<DeviceOption> outputs)
    {
        string? def = DefaultId(DataFlow.Render);
        var defOption = outputs.FirstOrDefault(o => o.DeviceId == def);
        if (defOption != null && !IsVirtual(defOption.Label)) return def;

        // La predeterminada es virtual (MixSalidas): preferir altavoces/auriculares antes que el audio HDMI de un monitor
        var physical = outputs.Where(o => o.DeviceId != null && !IsVirtual(o.Label)).ToList();
        static bool IsMonitor(string l) =>
            l.Contains("NVIDIA High Definition", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("HDMI", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Display Audio", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("AMD High Definition", StringComparison.OrdinalIgnoreCase);
        return (physical.FirstOrDefault(o => !IsMonitor(o.Label)) ?? physical.FirstOrDefault())?.DeviceId;
    }

    /// <summary>true si el dispositivo existe y está activo.</summary>
    public static bool IsActive(string id)
    {
        try { return new MMDeviceEnumerator().GetDevice(id).State == DeviceState.Active; }
        catch { return false; }
    }

    public static MMDevice? Get(string id)
    {
        try { return Enumerator.GetDevice(id); }
        catch { return null; }
    }

    public static string? DefaultId(DataFlow flow)
    {
        try { return Enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID; }
        catch { return null; } // no hay dispositivo
    }
}
