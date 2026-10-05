# Builds a signed Release x64 MSIX for sideloading.
# Output: <repo>\dist\Iris_<version>_x64\  (copy this whole folder to the target PC)
param(
    [string]$Pfx = (Join-Path $env:USERPROFILE 'IrisSigning\Iris.pfx'),
    [string]$Platform = 'x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
if (-not (Test-Path $Pfx)) { throw "No existe $Pfx. Corre Scripts\New-IrisCert.ps1 primero." }

$secure = Read-Host 'Contrasena del .pfx' -AsSecureString
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
dotnet build (Join-Path $root 'Iris.csproj') -c Release -p:Platform=$Platform `
    -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never `
    -p:AppxPackageSigningEnabled=true -p:PackageCertificateKeyFile=$Pfx `
    -p:PackageCertificatePassword=$plain -p:AppxPackageDir="$dist\"
if ($LASTEXITCODE -ne 0) { throw 'Fallo el build del MSIX.' }

Copy-Item (Join-Path (Split-Path $Pfx) 'Iris.cer') (Get-ChildItem $dist -Directory | Select-Object -First 1).FullName -ErrorAction SilentlyContinue
Write-Host "MSIX en $dist"
