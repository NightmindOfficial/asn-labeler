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
$setupLog = Join-Path $root "dist\installer-test-$Scope.log"
if ((Test-Path 'HKCU:\Software\nightmind.dev\AsnLabeler') -or (Test-Path 'HKLM:\Software\nightmind.dev\AsnLabeler')) { throw 'Refusing to modify an existing installation.' }
function Read-Path {
    $hive = if ($Scope -eq 'user') { [Microsoft.Win32.Registry]::CurrentUser } else { [Microsoft.Win32.Registry]::LocalMachine }
    $subkey = if ($Scope -eq 'user') { 'Environment' } else { 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment' }
    $key = $hive.OpenSubKey($subkey)
    try { return [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) }
    finally { $key.Dispose() }
}
$pathBefore = Read-Path
$cliFolder = Join-Path $destination 'cli'
function Run-Setup([string]$Tasks = '__previous__') {
    $arguments = @($scopeFlag, '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/LOG="' + $setupLog + '"'), ('/DIR="' + $destination + '"'))
    if ($Tasks -ne '__previous__') { $arguments += '/TASKS="' + $Tasks + '"' }
    $p = Start-Process $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    return $p.ExitCode
}
try {
    $result = Run-Setup
    if ($result -ne 0) { throw "Fresh installation failed (exit $result). See $setupLog" }
    $exe = Join-Path $destination 'asn-labeler.exe'
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$version.0") { throw 'Installed file version mismatch.' }
    $cli = Join-Path $cliFolder 'asnlbl.exe'
    if (Test-Path $cli) { throw 'CLI must be optional and unchecked by default.' }
    if ((Read-Path) -cne $pathBefore) { throw 'GUI-only setup modified PATH.' }
    if ((Run-Setup -Tasks 'cli') -ne 0) { throw 'CLI installation failed.' }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($cli).FileVersion -ne "$version.0") { throw 'Installed CLI version mismatch.' }
    $cliVersion = & $cli -v
    if ($LASTEXITCODE -ne 0 -or $cliVersion -ne $version) { throw 'Installed CLI version command failed.' }
    $help = (& $cli help) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $help -notmatch 'asnlbl print' -or $help -notmatch '-c NUMBER' -or $help -notmatch '-s') { throw 'CLI help missing syntax or flags.' }
    & $cli print -c 0
    if ($LASTEXITCODE -ne 2) { throw 'Installed CLI did not reject invalid quantity.' }
    if ((Get-ItemProperty $registry).InstallPath -ne $destination) { throw 'Installation registration missing.' }
    if ((Run-Setup) -ne 0) { throw 'Same-version reinstall failed.' }
    if (!(Test-Path $cli)) { throw 'Reinstall forgot CLI selection.' }
    if (@((Read-Path).Split(';') | Where-Object { $_ -eq $cliFolder }).Count -ne 1) { throw 'CLI PATH entry missing or duplicated.' }
    if ((Run-Setup -Tasks '') -ne 0) { throw 'CLI deselection failed.' }
    if ((Test-Path $cli) -or (Read-Path).Split(';') -contains $cliFolder) { throw 'CLI deselection left executable or PATH entry.' }
    if ((Read-Path).TrimEnd(';') -cne $pathBefore.TrimEnd(';')) { throw 'CLI deselection altered unrelated PATH entries.' }
    if ((Run-Setup -Tasks 'cli') -ne 0) { throw 'CLI reinstallation failed.' }
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
if ((Read-Path).TrimEnd(';') -cne $pathBefore.TrimEnd(';')) { throw 'Uninstall altered unrelated PATH entries.' }
