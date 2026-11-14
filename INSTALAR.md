# Cómo instalar Mini-Mixer desde cero

Guía para reinstalar Mini-Mixer en un PC nuevo (o tras formatear) y dejarlo funcionando igual que antes.
Tiempo aproximado: 15 minutos.

---

## 1. Descargar e instalar Mini-Mixer

1. Entra en **https://github.com/CrhissJMC/MiniMix/releases** y abre la versión más reciente.
2. Descarga **`MiniMixer-Setup-x.y.z.exe`** (el instalador).
   - Alternativa sin instalar nada: **`MiniMixer.exe`** (portable). Se abre con doble clic; guárdalo en una carpeta fija, porque el inicio con Windows apunta a esa ruta.
3. Ejecuta el instalador.
   - Si Windows muestra **"Windows protegió su PC"**: pulsa **Más información › Ejecutar de todas formas**. Aparece porque el instalador no tiene firma digital de pago, no porque tenga nada malo.
   - No pide permisos de administrador. Se instala en `%LOCALAPPDATA%\Programs\MiniMixer`.
   - Deja marcado **"Iniciar Mini-Mixer con Windows"**.
4. No necesitas instalar .NET: ya va incluido.

## 2. Instalar los dispositivos virtuales (una sola vez)

Mini-Mixer usa dos dispositivos de audio virtuales que hay que instalar aparte (son gratis):

| Para qué | Programa | Descarga |
|---|---|---|
| **MixSalidas**: la salida de Windows por la que pasan todas las apps | **Voicemeeter Potato** (aporta "Voicemeeter In 1…5") | https://vb-audio.com/Voicemeeter/potato.htm |
| **MinMix-B1**: el micrófono virtual para Discord, Meet, etc. | **VB-Audio Virtual Cable** | https://vb-audio.com/Cable/ |

- Instala los dos y **reinicia el PC**.
- **No hace falta abrir Voicemeeter**: solo se usan sus dispositivos. Mini-Mixer lo sustituye.

## 3. Configurar Windows (una sola vez)

Abre el panel de sonido clásico: tecla **Windows + R**, escribe `mmsys.cpl` y pulsa Enter.

**Pestaña Reproducción**
1. Doble clic en **Voicemeeter In 5** › pestaña General › cambia el nombre a **`MixSalidas`** › Aceptar.
2. Selecciona **MixSalidas** › **Establecer como predeterminado**.

**Pestaña Grabación**
3. Doble clic en **CABLE Output** › cambia el nombre a **`MinMix-B1`** › Aceptar.
4. Deja tu micrófono real como dispositivo predeterminado (no MinMix-B1).

## 4. Configurar Mini-Mixer (una sola vez)

Abre Mini-Mixer desde el menú Inicio.

1. **MIXSALIDAS**: debe mostrar "MixSalidas" y el mensaje "✓ Es la salida predeterminada de Windows". Si no, elígelo en la lista.
2. **SALIDAS FÍSICAS**: en **A1** elige tus altavoces; en **A2** y **A3**, otros dispositivos si los usas (por ejemplo auriculares).
3. **MICRÓFONO VIRTUAL (B1 · MinMix-B1)**: debe tener **CABLE Input**.
4. **MICRÓFONO** (izquierda): elige tu micrófono real y deja encendido **B1**.
5. Pestaña **Configuración**: nivel máximo de cada salida, frecuencia de mezcla (48 kHz recomendado) e inicio con Windows.

Todo esto se guarda solo: no hay que volver a hacerlo.

## 5. En Discord, Meet, OBS…

- **Micrófono / dispositivo de entrada**: **MinMix-B1** (en Discord aparece como "VB-Audio Virtual Cable – MinMix-B1").
- **Salida**: "Predeterminado" (que es MixSalidas).

## 6. Uso diario

- Al encender el PC, Mini-Mixer arranca solo y **abre su ventana** (espera a que el audio de Windows esté listo). Si prefieres que se quede solo en la bandeja con un aviso: Configuración › Inicio.
- La **X** solo oculta la ventana: Mini-Mixer sigue funcionando en la **bandeja del sistema** (icono de tres faders verdes, junto al reloj; si no lo ves, está dentro de la flecha **^**).
- **Clic** en el icono: abre la ventana. **Clic derecho**: Iniciar con Windows / **Salir**.
- Cada aplicación recuerda su **volumen, mute y salidas (A1/A2/A3/B1)** de la última vez.
- Cambiar el volumen de Windows (icono del altavoz) mueve el fader de MixSalidas, y al revés.

## 7. Guardar y recuperar tu configuración

Toda la configuración está en un solo archivo:

```
%APPDATA%\MiniMixer\settings.json
```

Cópialo antes de formatear. Al reinstalar, cierra Mini-Mixer (clic derecho en el icono › Salir), pon el archivo en esa carpeta y vuelve a abrirlo.
En otro PC, los dispositivos tienen otros identificadores, así que habrá que elegirlos de nuevo (paso 4). Los volúmenes por aplicación y los niveles sí se conservan.

## 8. Problemas frecuentes

| Problema | Solución |
|---|---|
| No se oye nada de las apps | MixSalidas debe ser la salida predeterminada de Windows y **A1** debe tener tus altavoces. En la fila de la app, al menos un botón A1/A2/A3 encendido. |
| Una app se oye **doble** | Esa app está sonando directamente en tus altavoces y además por Mini-Mixer. En Windows › Sonido › Mezclador de volumen, pon su salida en "Predeterminado" o MixSalidas. |
| En Discord no me oyen | Micrófono de Discord = **MinMix-B1**; en Mini-Mixer, tira del micrófono con **B1** encendido y fila B1 con **CABLE Input**. |
| No arranca con Windows | Administrador de tareas › Aplicaciones de arranque › **Mini-Mixer** › Habilitar. O en Mini-Mixer › Configuración › "Iniciar Mini-Mixer al encender el PC". Qué pasó en cada arranque: `%APPDATA%MiniMixermini-mixer.log`. |
| La app consume mucha CPU | Configuración › Frecuencia de mezcla › **48 kHz**. |

## 9. Desinstalar

Configuración de Windows › Aplicaciones › Aplicaciones instaladas › **Mini-Mixer** › Desinstalar.
Tu `settings.json` se conserva por si vuelves a instalarlo; bórralo a mano si no lo quieres.

---

## Para desarrollar (compilar desde el código)

Requisitos: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Git y, para el instalador, Inno Setup 6.

```powershell
winget install Microsoft.DotNet.SDK.8
winget install Git.Git
winget install JRSoftware.InnoSetup
git clone https://github.com/CrhissJMC/MiniMix.git
cd MiniMix
```

| Tarea | Comando |
|---|---|
| Compilar e instalar en este PC | `powershell -ExecutionPolicy Bypass -File tools\install.ps1` |
| Generar el instalador y el portable en `dist\` | `powershell -ExecutionPolicy Bypass -File tools\build-release.ps1` |
| Publicar una versión en GitHub (Releases) | `powershell -ExecutionPolicy Bypass -File tools\publish-release.ps1` |

Para sacar una versión nueva: sube el número en `src\MiniMixer\MiniMixer.csproj` (`<Version>`), haz commit y push, y ejecuta `build-release.ps1` y luego `publish-release.ps1`.
