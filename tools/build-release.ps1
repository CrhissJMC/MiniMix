# Genera los archivos para publicar una versión en dist\:
#   - MiniMixer.exe              → portable, un solo archivo, .NET incluido (no hay que instalar nada)
#   - MiniMixer-Setup-<ver>.exe  → instalador (necesita Inno Setup 6: winget install JRSoftware.InnoSetup)
#
# Uso: powershell -ExecutionPolicy Bypass -File tools\build-release.ps1
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\MiniMixer\MiniMixer.csproj'
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'portable'

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "Versión: $version"

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

# 1) Portable: un solo .exe autocontenido (incluye el runtime de .NET y WPF)
Write-Host 'Compilando MiniMixer.exe portable...'
& $dotnet publish $project -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $stage -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'La compilación falló' }
Copy-Item (Join-Path $stage 'MiniMixer.exe') (Join-Path $dist 'MiniMixer.exe')

# 2) Instalador con Inno Setup
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    Write-Host 'Creando instalador...'
    & $iscc /Q "/DAppVersion=$version" "/DSourceExe=$(Join-Path $stage 'MiniMixer.exe')" "/O$dist" (Join-Path $root 'installer\MiniMixer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup falló' }
}
else {
    Write-Warning 'Inno Setup 6 no está instalado: sólo se generó el .exe portable (winget install JRSoftware.InnoSetup).'
}

Remove-Item $stage -Recurse -Force
Write-Host "`nListo en $dist :"
Get-ChildItem $dist | ForEach-Object { '  {0}  ({1:0.0} MB)' -f $_.Name, ($_.Length / 1MB) }
