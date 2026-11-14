# Mini-Mixer

> **Descargar:** [Releases](https://github.com/CrhissJMC/MiniMix/releases): instalador `MiniMixer-Setup-x.y.z.exe` o portable `MiniMixer.exe`.
> **Instalar desde cero (paso a paso):** [INSTALAR.md](INSTALAR.md)

Mezclador de audio ligero para Windows (WPF + NAudio), inspirado en Voicemeeter Potato pero reducido a lo que hace falta en el día a día:
micrófono con dinámica y EQ, 3 entradas virtuales, **mezclador por aplicación**, 3 salidas físicas (A1–A3) y un micrófono virtual (B1):

- **MixSalidas**: un dispositivo de reproducción virtual que se pone como salida predeterminada de Windows. Cada app que suena en él se captura por separado y va a A1/A2/A3/B1 según sus botones, así puede sonar en varios dispositivos a la vez. Se usa uno de los VAIO de Voicemeeter ya instalados, renombrado a "MixSalidas" (Panel de sonido › dispositivo › Propiedades › nombre). Se ha medido que su captura funciona sin Voicemeeter abierto.
- **Volumen de Windows ↔ fader de MixSalidas**: enlazados en ambos sentidos. Ese volumen no se aplica a lo capturado (medido: igual al 100 % que al 25 %), así que el mixer lo aplica a las apps capturadas. El volumen y el mute por app de Windows sí afectan a la captura (medido), por eso los faders de las apps funcionan también para lo ruteado.
- **A1–A3**: salidas físicas, cada una en su dispositivo. Su 100 % equivale a −20 dB.
- **B1 · MinMix-B1**: se envía a un cable virtual (VB-Cable, "CABLE Input"). En Meet/Discord se elige "CABLE Output", renombrado en Windows a "MinMix-B1". Los VAIO de Voicemeeter no sirven aquí: su lado de grabación solo recibe audio con Voicemeeter abierto.

Todos los faders van en % con curva de audio (50 % ≈ −12 dB respecto al 100 %). La salida lleva un limitador suave en lugar de recorte duro. La interfaz se adapta al ancho de la ventana (3 disposiciones), y las apps se muestran en filas que se pueden minimizar a "nombre + %".

Requisitos: Windows 10 2004 (build 19041) o posterior, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), x64.

```bash
powershell -ExecutionPolicy Bypass -File tools\install.ps1
```

El script compila en Release, instala en `%LOCALAPPDATA%\Programs\MiniMixer` (sin administrador) y crea el acceso directo "Mini-Mixer" en el menú Inicio. Si la app está abierta, la cierra antes y la vuelve a abrir. Hay que volver a ejecutarlo después de cada cambio en el código.

Pestaña **Configuración** (se guarda en `%APPDATA%\MiniMixer\settings.json`, junto con los dispositivos elegidos en cada fila y las secciones ocultas):
- **Nivel máximo de las salidas**: el nivel que entrega A1/A2/A3/B1 con el fader al 100 %, de −40 a +1.5 dB. Es un segundo control interno: el fader nunca lo supera.
- **Frecuencia de mezcla**: 44.1 / 48 / 96 / 192 kHz; se aplica al reiniciar. Medido con apps sonando: a 48 kHz ≈ 0.6 % de un núcleo en la bandeja y a 192 kHz ≈ 9 %. Las fuentes (apps, Discord) trabajan a 48 kHz, así que 192 kHz no añade calidad audible.
- **Iniciar con Windows**.

**Memoria por aplicación**: se guarda el volumen, el mute y el ruteo (A1/A2/A3/B1) de cada app, identificada por su ejecutable (`brave`, `discord`…), en `settings.json › Apps`. Cuando la app vuelve a abrirse se le aplican solos, y si abre sesiones de audio nuevas se igualan a lo que muestra su fila. Los cambios hechos desde el mezclador de volumen de Windows también se recuerdan.

Las salidas A1–A3 tienen el mismo procesado que las entradas (EQ de 3 bandas, HPF/LPF, paneo y brillo), en la sección "HARDWARE OUT · A1 A2 A3".

Rendimiento: `MMDevice.FriendlyName` tarda 35–65 ms por dispositivo, porque el driver recorre todas las propiedades. Por eso los nombres se leen una sola vez (`DeviceCatalog.NameOf`); antes, el sondeo de apps costaba ~33 % de un núcleo.

Funcionamiento en segundo plano:
- La **X** solo oculta la ventana; el audio sigue funcionando y Mini-Mixer queda en la bandeja del sistema. Para cerrarla del todo: clic derecho en el icono › **Salir**.
- Solo puede haber una instancia: abrirla otra vez muestra la que ya está en marcha.
- **Inicio con Windows** (activado en el primer arranque): `HKCU\...\Run` → `MiniMixer.exe --tray`, que arranca sin mostrar la ventana. Se activa o desactiva desde el menú del icono.
- `MiniMixer.exe --exit` cierra limpiamente la instancia en marcha.
- Con la ventana oculta se pausan los medidores, para consumir lo mínimo.

## Estructura

```
MiniMixer.sln
src/MiniMixer/
├─ MiniMixer.csproj              net8.0-windows, WPF, NAudio 2.2.1, x64
├─ App.xaml(.cs)                 arranque y tema
├─ MainWindow.xaml(.cs)          UI: Hardware Input | Virtual Inputs | Aplicaciones | Hardware Out
├─ Themes/Dark.xaml              paleta, faders verdes, chips A1/Mute/Solo, ComboBox oscuro
├─ Controls/
│  ├─ VuMeter.cs                 medidor estéreo dibujado con OnRender (-60..0 dBFS)
│  ├─ Knob.cs                    perilla (arrastre vertical, rueda, doble clic = reset)
│  ├─ PanPad.cs                  pad XY "Intellipan"
│  └─ FaderBehavior.cs           doble clic / rueda en los faders
├─ Audio/
│  ├─ AudioEngine.cs             motor: lista de canales + buses, estado de Solo
│  ├─ InputChannel.cs            fuente → conversión → remuestreo → DSP → buses
│  ├─ OutputBus.cs               bus A1..An: FIFO por canal + mezcla en el hilo de WasapiOut
│  ├─ ChannelProcessor.cs        Gate → Denoiser → EQ → Compresor → Ganancia/Paneo → Medidor
│  ├─ LevelMeter.cs              pico + RMS
│  ├─ SampleConversion.cs        PCM16/24/32/float → float, adaptador a estéreo
│  ├─ DeviceCatalog.cs           enumeración de endpoints WASAPI
│  ├─ Dsp/Biquad.cs              filtros RBJ (shelf, peaking, HPF, LPF)
│  ├─ Dsp/Equalizer.cs           EQ 3 bandas + HPF/LPF
│  ├─ Sources/                   IAudioSource, captura de dispositivo/loopback, captura por proceso
│  └─ Interop/                   COM: ActivateAudioInterfaceAsync, IAudioClient, IAudioCaptureClient
├─ Sessions/AudioSessionService.cs   Audio Session Manager de Windows (apps con audio)
└─ ViewModels/                   MVVM sin dependencias externas
```

## Arquitectura del audio

Formato interno fijo: **48 kHz · estéreo · float32**.

```
Mic (WasapiCapture) ───────┐
Virtual 1..3 (captura o    ├─► InputChannel ─► ChannelProcessor (DSP, una vez) ─┬─► FIFO ─► OutputBus A1 ─► WasapiOut (altavoces)
   loopback de un device)  │                                                    ├─► FIFO ─► OutputBus A2 ─► WasapiOut (auriculares)
App X (Process Loopback) ──┘                                                    └─► FIFO ─► OutputBus A3 ─► WasapiOut (...)
```

- **Push en la captura, pull en la salida.** Cada fuente entrega bloques en su propio hilo. El canal convierte, remuestrea (WDL) si hace falta y aplica el DSP **una sola vez**; luego copia el resultado a la FIFO de cada bus al que está ruteado.
- **Cada bus lleva su propio reloj.** El hilo de `WasapiOut` de cada bus suma las FIFOs, aplica fader/mute y mide. Así un canal puede sonar a la vez en varios dispositivos.
- **Deriva de reloj.** Dos tarjetas nunca van exactamente a 48 000 Hz. Aquí se resuelve de forma simple: 20 ms de pre-buffer y, si una FIFO pasa de 150 ms, se vacía (un clic breve cada mucho tiempo). La mejora natural es un remuestreo adaptativo (ajustar la relación ±0.1 % según el nivel de la FIFO).
- **Sin asignaciones en el hilo de audio**: los buffers se reutilizan y los parámetros que escribe la UI se leen una vez por bloque. Los coeficientes del EQ se recalculan en el hilo de audio cuando cambian.
- **Solo**: si algún canal tiene Solo, `AudioEngine.SoloActive` silencia los demás dentro de su `ChannelProcessor`.

### DSP por canal

| Etapa | Implementación |
|---|---|
| Gate | Envolvente pico (ataque instantáneo, release 50 ms). Knob 0–10 → umbral −75…−30 dBFS. Apertura 1 ms, cierre 80 ms |
| Denoiser | Expansor descendente 1:3 (umbral −70…−35 dBFS). Es un "denoiser" ligero: para supresión real de ruido conviene integrar **RNNoise** (P/Invoke a `rnnoise.dll`, tramas de 480 muestras a 48 kHz) |
| EQ | Low shelf 120 Hz, peaking 1 kHz (Q 0.8), high shelf 8 kHz, ±12 dB; HPF 20–500 Hz y LPF 2–20 kHz de 12 dB/oct |
| Compresor | Knob 0–10 → umbral 0…−30 dB, ratio 1…4, make-up automático |
| Intellipan | X = paneo (ley de balance), Y = brillo (±6 dB en el shelf de agudos) |
| Fader | −60…+12 dB con rampa por bloque (sin "zipper noise") |

## Audio Session Manager: detectar y controlar otras aplicaciones

Windows asigna a cada app que reproduce audio una **sesión de audio** por dispositivo de salida. Es lo que muestra el "Mezclador de volumen". La API es `IAudioSessionManager2`, que NAudio envuelve:

```csharp
var enumerator = new MMDeviceEnumerator();
foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
{
    var manager = device.AudioSessionManager;   // IAudioSessionManager2 de ese endpoint
    manager.RefreshSessions();                  // vuelve a pedir el IAudioSessionEnumerator
    var sessions = manager.Sessions;

    for (int i = 0; i < sessions.Count; i++)
    {
        AudioSessionControl s = sessions[i];
        if (s.State == AudioSessionState.AudioSessionStateExpired) continue;

        uint pid  = s.GetProcessID;                     // IAudioSessionControl2
        float vol = s.SimpleAudioVolume.Volume;         // ISimpleAudioVolume, 0..1
        bool mute = s.SimpleAudioVolume.Mute;
        float pk  = s.AudioMeterInformation.MasterPeakValue; // medidor gratis, lo calcula Windows

        s.SimpleAudioVolume.Volume = 0.5f;              // cambiar volumen de ESA app
        s.SimpleAudioVolume.Mute = true;                // silenciarla
    }
}
```

Puntos clave (todos implementados en `AudioSessionService` y `AppStripViewModel`):

1. **Hay un gestor por dispositivo.** Una app que suena en altavoces y auriculares tiene dos sesiones. Por eso se **agrupan por PID** y el fader escribe en todas.
2. **Nombre de la app.** `DisplayName` suele venir vacío o como recurso (`@%SystemRoot%\...`). Se usa como alternativa `Process.MainModule.FileVersionInfo.FileDescription` y, si el proceso está elevado y no deja leerlo, `ProcessName`.
3. **Sesiones de sistema.** `IsSystemSoundsSession` identifica "Sonidos del sistema" (PID 0). Se puede cambiar su volumen, pero no capturarla por proceso.
4. **Excluir nuestro propio PID.** Si no, los `WasapiOut` del mezclador aparecerían como una app más y rutearlos crearía realimentación.
5. **Detección de altas y bajas.** Aquí se hace **sondeo cada 1 s** (simple y barato). La alternativa por eventos es `AudioSessionManager.OnSessionCreated` para las nuevas sesiones y `AudioSessionControl.RegisterEventClient(IAudioSessionEventsHandler)` para `OnStateChanged`/`OnSessionDisconnected`/`OnVolumeChanged`. Esas notificaciones llegan en hilos COM del sistema y hay que pasarlas al Dispatcher; aun así conviene mantener un barrido periódico, porque las sesiones expiradas no siempre avisan.
6. **Sincronización con Windows.** Si el usuario cambia el volumen desde el mezclador de Windows, el sondeo actualiza el fader sin volver a escribirlo (bandera `_syncing`).

### Ruteo por aplicación (A1/A2/A3)

Cambiar el volumen de una app es fácil. **Enviarla a varias salidas** no lo es, porque Windows sólo deja elegir un dispositivo por app. Mini-Mixer usa la API **Application Loopback** (Windows 10 2004+):

- `ActivateAudioInterfaceAsync("VAD\\Process_Loopback", IID_IAudioClient, PROPVARIANT{VT_BLOB → AUDIOCLIENT_ACTIVATION_PARAMS{PROCESS_LOOPBACK, pid, INCLUDE_TARGET_PROCESS_TREE}})` devuelve un `IAudioClient` que captura **sólo el audio de ese proceso y sus hijos** (necesario para Chrome/Edge/Discord, que reproducen desde un subproceso).
- No admite `GetMixFormat`, así que se pide un formato fijo (48 kHz/16 bits/estéreo con `AUTOCONVERTPCM`).
- La activación, `Initialize`, y el bucle `GetBuffer`/`ReleaseBuffer` corren en un hilo MTA propio (`ProcessLoopbackCapture`).

Al activar A1..A3 en una app se crea un `InputChannel` con esa fuente; al desactivarlos todos, se destruye.

**Importante: audio duplicado.** La captura no "roba" el audio: la app sigue sonando en su dispositivo original. Para que sólo suene a través del mezclador, en *Configuración → Sistema → Sonido → Mezclador de volumen* asigna a esa app una salida que no escuches (p. ej. "CABLE Input" de VB-Cable o una salida sin usar). La captura por proceso es independiente del endpoint, así que Mini-Mixer la seguirá recibiendo. Esto se puede automatizar con la interfaz no documentada `IAudioPolicyConfigFactory::SetPersistedDefaultAudioEndpoint` (la que usan EarTrumpet y SoundSwitch). Su GUID cambia según la build de Windows, por eso queda fuera de esta base.

> Pendiente de comprobar en tu máquina: si silenciar la sesión de la app en Windows también silencia lo capturado por loopback de proceso. Con el enfoque de "salida muda" descrito arriba no hace falta silenciarla.

## Entradas virtuales

Crear un dispositivo de audio virtual nuevo (como "Voicemeeter Input") **requiere un driver de kernel firmado**, que queda fuera del alcance de una app .NET. Cada "Virtual Input" de Mini-Mixer puede tomar:

- cualquier dispositivo de grabación, por ejemplo **"CABLE Output" de VB-Audio Cable** (gratuito). Las apps envían a "CABLE Input" y Mini-Mixer lo recibe aquí;
- o el **loopback de cualquier salida** ("Loopback · Altavoces..."). Si esa salida es también la de un bus, la tira avisa de la realimentación.

## Limitaciones conocidas / siguientes pasos

- Persistencia de la configuración (JSON en `%AppData%`), que hoy no existe.
- Reacción a conectar o desconectar dispositivos (`IMMNotificationClient` vía `MMDeviceEnumerator.RegisterEndpointNotificationCallback`).
- Remuestreo adaptativo contra la deriva de reloj en lugar de vaciar la FIFO.
- RNNoise para el denoiser.
- Iconos de las apps (`SHGetFileInfo` / `Icon.ExtractAssociatedIcon` sobre la ruta del proceso).
