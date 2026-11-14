# Compila Mini-Mixer en Release y lo instala en %LOCALAPPDATA%\Programs\MiniMixer
# (carpeta del usuario, sin administrador). Crea el acceso directo del menú Inicio.
# Si Mini-Mixer está abierto, lo cierra antes de copiar y lo vuelve a abrir al final.
#
# Uso: powershell -ExecutionPolicy Bypass -File tools\install.ps1
$ErrorActionPreference = 'Stop'

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$project = Join-Path $PSScriptRoot '..\src\MiniMixer\MiniMixer.csproj'
$target = Join-Path $env:LOCALAPPDATA 'Programs\MiniMixer'
$exe = Join-Path $target 'MiniMixer.exe'

# 1) Cerrar la instancia en marcha (de forma limpia) si la hay
$running = Get-Process MiniMixer -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Cerrando Mini-Mixer...'
    $first = $running | Select-Object -First 1
    Start-Process $first.Path -ArgumentList '--exit' | Out-Null
    foreach ($p in $running) { if (-not $p.WaitForExit(8000)) { $p.Kill() } }
}

# 2) Publicar (depende del runtime .NET 8 ya instalado)
Write-Host "Compilando e instalando en $target ..."
& $dotnet publish $project -c Release -p:Platform=x64 -r win-x64 --self-contained false -o $target -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'La compilación falló' }

# 3) Acceso directo en el menú Inicio
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Mini-Mixer.lnk'
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($startMenu)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $target
$lnk.IconLocation = "$exe,0"
$lnk.Description = 'Mini-Mixer'
$lnk.Save()

# 4) Abrir (el primer arranque registra el inicio automático con esta ruta)
Start-Process $exe | Out-Null
Write-Host "Listo: $exe"
