param([string]$OutputDirectory = "dist")

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Split-Path -Parent $root
$licenseResources = @(
    "/resource:$project\LICENSE,AsnLabeler.LICENSE",
    "/resource:$project\NOTICE,AsnLabeler.NOTICE",
    "/resource:$project\LICENSE-BPAC-EXCEPTION.txt,AsnLabeler.BpacPermission",
    "/resource:$project\THIRD-PARTY-NOTICES.md,AsnLabeler.ThirdPartyNotices"
    "/resource:$root\vendor\InnoSetup\LICENSE.txt,AsnLabeler.InnoSetup.LICENSE"
)
$dist = Join-Path $root $OutputDirectory
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (!(Test-Path $csc)) {
    throw "C# compiler not found at $csc"
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$version = (Get-Content -LiteralPath (Join-Path $project 'windows-app/VERSION') -Raw).Trim()
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
    @($version.Split('.') | Where-Object { [long]$_ -gt 65534 }).Count -ne 0) {
    throw 'VERSION must be X.Y.Z with each component between 0 and 65534.'
}
$versionSource = Join-Path $dist 'AppRelease.g.cs'
$versionCode = "namespace AsnLabeler { static class AppRelease { public const string NumericVersion = `"$version.0`"; public const string Version = `"$version`"; } }"
[IO.File]::WriteAllText($versionSource, $versionCode, [Text.Encoding]::UTF8)
$out = Join-Path $dist "asn-labeler.exe"
$testOut = Join-Path $dist "asn-labeler-test.exe"
$src = Join-Path $root "src\Program.cs"
$backends = Join-Path $root "src\PrinterBackends.cs"
$matrix = Join-Path $root "src\DataMatrixLabels.cs"
$copyLabels = Join-Path $root "src\CopyLabelForm.cs"
$updates = Join-Path $root "src\Updates.cs"
$cli = Join-Path $root "src\HeadlessCli.cs"
$localization = Join-Path $root "src\Localization.cs"
$english = Join-Path $root "src\en.json"
$zxing = Join-Path $root "vendor\ZXing.Net\zxing.dll"
$zxingLicense = Join-Path $root "vendor\ZXing.Net\LICENSE"
if ((Get-FileHash $zxing -Algorithm SHA256).Hash -ne "643A5A3DB0AE02998B507BEB82DBC362A2D5593B429963DB17EB78089AABB95A") {
    throw "Unexpected ZXing.Net binary. Check vendor provenance before building."
}
$manifest = Join-Path $root "src\app.manifest"
$icon = Join-Path $root "assets\app.ico"
$material = Join-Path $root "vendor\MaterialIcons\MaterialIcons-Regular.ttf"
$materialLicense = Join-Path $root "vendor\MaterialIcons\LICENSE"
if ((Get-FileHash $material -Algorithm SHA256).Hash -ne "EF149F08BDD2FF09A4E2C8573476B7B0F3FBB15B623954ADE59899E7175BEDDA") {
    throw "Unexpected Material Icons font. Check vendor provenance."
}
$iconArg = if (Test-Path $icon) { "/win32icon:$icon" } else { $null }

& $csc `
  /nologo `
  /target:winexe `
  /codepage:65001 `
  "/win32manifest:$manifest" `
  /optimize+ `
  /platform:x64 `
  "/out:$out" `
  $iconArg `
  $licenseResources `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Drawing.dll `
  /reference:System.Management.dll `
  /reference:Microsoft.CSharp.dll `
  /reference:System.Xml.dll `
  /reference:System.Xml.Linq.dll `
  /reference:System.IO.Compression.dll `
  /reference:System.Web.Extensions.dll `
  /reference:System.Windows.Forms.dll `
  "/reference:$zxing" `
  "/resource:$zxing,AsnLabeler.zxing.dll" `
  "/resource:$zxingLicense,AsnLabeler.ZXing.LICENSE" `
  "/resource:$material,AsnLabeler.MaterialIcons.ttf" `
  "/resource:$materialLicense,AsnLabeler.MaterialIcons.LICENSE" `
  "/resource:$english,AsnLabeler.en.json" `
  "$src" "$backends" "$matrix" "$copyLabels" "$localization" "$versionSource" "$updates" "$cli"

if ($LASTEXITCODE -ne 0) {
    throw "C# compiler failed with exit code $LASTEXITCODE"
}

& $csc `
  /nologo `
  /target:exe `
  /define:DEVELOPMENT `
  /codepage:65001 `
  "/win32manifest:$manifest" `
  /optimize+ `
  /platform:x64 `
  "/out:$testOut" `
  $iconArg `
  $licenseResources `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Drawing.dll `
  /reference:System.Management.dll `
  /reference:Microsoft.CSharp.dll `
  /reference:System.Xml.dll `
  /reference:System.Xml.Linq.dll `
  /reference:System.IO.Compression.dll `
  /reference:System.Web.Extensions.dll `
  /reference:System.Windows.Forms.dll `
  "/reference:$zxing" `
  "/resource:$zxing,AsnLabeler.zxing.dll" `
  "/resource:$zxingLicense,AsnLabeler.ZXing.LICENSE" `
  "/resource:$material,AsnLabeler.MaterialIcons.ttf" `
  "/resource:$materialLicense,AsnLabeler.MaterialIcons.LICENSE" `
  "/resource:$english,AsnLabeler.en.json" `
  "$src" "$backends" "$matrix" "$copyLabels" "$localization" "$versionSource" "$updates" "$cli"

if ($LASTEXITCODE -ne 0) {
    throw "C# test compiler failed with exit code $LASTEXITCODE"
}

if ([Diagnostics.FileVersionInfo]::GetVersionInfo($out).FileVersion -ne "$version.0") {
    throw 'Executable version differs from VERSION.'
}
Write-Host "Built $out ($version)"
Write-Host "Built $testOut"

& $csc /nologo /target:exe /define:CLI /codepage:65001 /optimize+ /platform:x64 `
  "/win32manifest:$manifest" "/out:$dist\asn-labeler-cli.exe" $iconArg $licenseResources `
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
  /reference:System.Management.dll /reference:Microsoft.CSharp.dll /reference:System.Xml.dll `
  /reference:System.Xml.Linq.dll /reference:System.IO.Compression.dll `
  /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll `
  "/reference:$zxing" "/resource:$zxing,AsnLabeler.zxing.dll" `
  "/resource:$zxingLicense,AsnLabeler.ZXing.LICENSE" `
  "/resource:$material,AsnLabeler.MaterialIcons.ttf" `
  "/resource:$materialLicense,AsnLabeler.MaterialIcons.LICENSE" `
  "/resource:$english,AsnLabeler.en.json" `
  "$src" "$backends" "$matrix" "$copyLabels" "$localization" "$versionSource" "$updates" "$cli"
if ($LASTEXITCODE -ne 0) { throw 'CLI compiler failed' }
