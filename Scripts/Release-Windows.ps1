# Genera el instalador de Windows en local (misma receta que .github/workflows/release-windows.yml).
# Uso:  .\Scripts\Release-Windows.ps1            (toma la versión del archivo VERSION)
#       .\Scripts\Release-Windows.ps1 -Version 1.0.1
# Salida: <repo>\Releases\  (Iris-Setup.exe, paquetes .nupkg y releases.win.json)
# Plan: docs/plans/12-distribucion-y-actualizaciones/README.md
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = (Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'VERSION') -Raw).Trim(),
    [string]$FeedUrl = 'https://iris.atmosferast.com/downloads/windows/',
    [switch]$SkipDownload
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { dotnet tool install -g vpk }

    Remove-Item publish -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish Iris.csproj -c Release -r win-x64 --self-contained -p:Platform=x64 -p:IrisInstaller=true -p:Version=$Version -o publish
    if ($LASTEXITCODE -ne 0) { throw 'Fallo dotnet publish.' }

    if (-not $SkipDownload) {
        # Versión anterior, para generar el parche. Falla sin problema la primera vez.
        vpk download http --url $FeedUrl --outputDir Releases
    }

    vpk pack --packId IrisConsole --packVersion $Version --packDir publish --mainExe Iris.exe `
        --packTitle Iris --icon Assets/Iris.ico --outputDir Releases
    if ($LASTEXITCODE -ne 0) { throw 'Fallo vpk pack.' }

    $setup = Get-ChildItem Releases -Filter '*Setup.exe' | Select-Object -First 1
    Copy-Item $setup.FullName (Join-Path Releases 'Iris-Setup.exe') -Force
    Write-Host "Listo: $root\Releases (sube todo a la carpeta de descargas; releases.win.json al final)."
}
finally { Pop-Location }
