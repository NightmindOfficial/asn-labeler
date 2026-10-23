# Installs and removes a temporary client; explicit opt-in required outside CI.
param([ValidateSet('user', 'machine')][string]$Scope = 'user', [switch]$AllowLocalInstallationTest)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -and !$AllowLocalInstallationTest) { throw 'Use CI or explicitly opt into a local installation test.' }
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$setup = Join-Path $root "dist\asn-labeler-$version-setup.exe"
$destination = Join-Path ([IO.Path]::GetTempPath()) ('asn-installer-' + [Guid]::NewGuid().ToString('N'))
$registry = if ($Scope -eq 'user') { 'HKCU:\Software\nightmind.dev\AsnLabeler' } else { 'HKLM:\Software\nightmind.dev\AsnLabeler' }
$scopeFlag = if ($Scope -eq 'user') { '/CURRENTUSER' } else { '/ALLUSERS' }
if ((Test-Path 'HKCU:\Software\nightmind.dev\AsnLabeler') -or (Test-Path 'HKLM:\Software\nightmind.dev\AsnLabeler')) { throw 'Refusing to modify an existing installation.' }
function Run-Setup {
    $p = Start-Process $setup -ArgumentList @($scopeFlag, '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/DIR="' + $destination + '"')) -WindowStyle Hidden -PassThru -Wait
    return $p.ExitCode
}
try {
    if ((Run-Setup) -ne 0) { throw 'Fresh installation failed.' }
    $exe = Join-Path $destination 'asn-labeler.exe'
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$version.0") { throw 'Installed file version mismatch.' }
    if ((Get-ItemProperty $registry).InstallPath -ne $destination) { throw 'Installation registration missing.' }
    if ((Run-Setup) -ne 0) { throw 'Same-version reinstall failed.' }
    $mutex = [Threading.Mutex]::new($false, 'Global\AsnLabelerRunning')
    try { if ((Run-Setup) -eq 0) { throw 'Setup ignored the running-application guard.' } }
    finally { $mutex.Dispose() }
    Write-Host 'Installer install, reinstall, version and running-application guard passed.'
} finally {
    $uninstaller = Join-Path $destination 'unins000.exe'
    if (Test-Path $uninstaller) {
        $p = Start-Process $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
        if ($p.ExitCode -ne 0) { throw 'Uninstall failed.' }
    }
}
if (Test-Path $registry) { throw 'Uninstall registration was not removed.' }
