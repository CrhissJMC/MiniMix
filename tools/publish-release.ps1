# Publica en GitHub (Releases) la versión generada por tools\build-release.ps1.
# Usa la sesión de GitHub que ya guardó Git (Git Credential Manager); el token no se muestra en pantalla.
#
# Uso: powershell -ExecutionPolicy Bypass -File tools\publish-release.ps1
# Antes: commit + push del código, y tools\build-release.ps1.
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'CrhissJMC/MiniMix'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dist = Join-Path $root 'dist'
$project = Join-Path $root 'src\MiniMixer\MiniMixer.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$tag = "v$version"

$git = 'C:\Program Files\Git\cmd\git.exe'
if (-not (Test-Path $git)) { $git = 'git' }

$assets = @(
    (Join-Path $dist "MiniMixer-Setup-$version.exe"),
    (Join-Path $dist 'MiniMixer.exe')
)
foreach ($a in $assets) { if (-not (Test-Path $a)) { throw "Falta $a. Ejecuta antes tools\build-release.ps1" } }

# Token de la sesión de GitHub guardada por Git (no se imprime)
$cred = "protocol=https`nhost=github.com`n`n" | & $git credential fill
$token = ($cred | Where-Object { $_ -like 'password=*' } | Select-Object -First 1) -replace '^password=', ''
if (-not $token) { throw 'No hay sesión de GitHub guardada. Haz un "git push" primero para iniciar sesión.' }
$headers = @{ Authorization = "token $token"; 'User-Agent' = 'MiniMixer-release'; Accept = 'application/vnd.github+json' }

$body = @"
## Mini-Mixer $version

**¿Primera vez o reinstalando?** Sigue la guía paso a paso: [INSTALAR.md](https://github.com/$repo/blob/main/INSTALAR.md)

### Descargas
- **MiniMixer-Setup-$version.exe**: instalador (recomendado). Sin administrador, crea el acceso directo, el inicio con Windows y la desinstalación.
- **MiniMixer.exe**: versión portable, un solo archivo; se abre sin instalar.

No hace falta instalar .NET (va incluido). Requisitos: Windows 10 2004 o posterior (x64), [VB-Cable](https://vb-audio.com/Cable/) para el micrófono virtual **MinMix-B1** y [Voicemeeter Potato](https://vb-audio.com/Voicemeeter/potato.htm) para la salida virtual **MixSalidas** (no hace falta abrir Voicemeeter).

Si Windows muestra "Windows protegió su PC": **Más información › Ejecutar de todas formas** (el instalador no tiene firma digital de pago).
"@

# Crear la Release (o reutilizarla si ya existe ese tag)
try {
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/tags/$tag" -Headers $headers
    Write-Host "La Release $tag ya existe: se actualizarán sus archivos."
}
catch {
    $payload = @{ tag_name = $tag; target_commitish = 'main'; name = "Mini-Mixer $version"; body = $body; draft = $false; prerelease = $false } | ConvertTo-Json
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases" -Method Post -Headers $headers `
        -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -ContentType 'application/json; charset=utf-8'
    Write-Host "Release $tag creada."
}

foreach ($file in $assets) {
    $name = Split-Path $file -Leaf
    $old = $release.assets | Where-Object { $_.name -eq $name }
    if ($old) { Invoke-RestMethod $old.url -Method Delete -Headers $headers | Out-Null }
    Write-Host ("Subiendo {0} ({1:0.0} MB)..." -f $name, ((Get-Item $file).Length / 1MB))
    $uploadUrl = "https://uploads.github.com/repos/$repo/releases/$($release.id)/assets?name=$([uri]::EscapeDataString($name))"
    Invoke-RestMethod $uploadUrl -Method Post -Headers $headers -InFile $file -ContentType 'application/octet-stream' | Out-Null
}

Write-Host "`nPublicado: $($release.html_url)"
