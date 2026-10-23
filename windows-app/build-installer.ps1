param([string]$Compiler = (Join-Path $PSScriptRoot '..\.local\tools\inno\ISCC.exe'))
$ErrorActionPreference = 'Stop'
if (!(Test-Path $Compiler)) { throw 'Run windows-app/tools/setup-inno.ps1 first.' }
$version = (Get-Content (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
$exe = Join-Path $PSScriptRoot 'dist\asn-labeler.exe'
if (!(Test-Path $exe) -or [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$version.0") { throw 'Build the matching client first.' }
& $Compiler "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer\asn-labeler.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
