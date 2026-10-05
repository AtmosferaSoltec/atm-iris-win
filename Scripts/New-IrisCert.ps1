# Creates the self-signed code-signing certificate used to sideload Iris.
# The .pfx/.cer are written OUTSIDE the repo (%USERPROFILE%\IrisSigning). Never commit them.
# Publisher must match Package.appxmanifest <Identity Publisher="...">.
param(
    [string]$Subject = 'CN=Joel',
    [string]$OutDir = (Join-Path $env:USERPROFILE 'IrisSigning'),
    [int]$Years = 5
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature `
    -FriendlyName 'Iris (sideload)' -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears($Years) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

$password = Read-Host 'Contrasena para el .pfx' -AsSecureString
Export-PfxCertificate -Cert $cert -FilePath (Join-Path $OutDir 'Iris.pfx') -Password $password | Out-Null
Export-Certificate -Cert $cert -FilePath (Join-Path $OutDir 'Iris.cer') | Out-Null

Write-Host "Listo. Thumbprint: $($cert.Thumbprint)"
Write-Host "Archivos en $OutDir (Iris.pfx = privado, Iris.cer = publico para la PC de la iglesia)"
