param(
    [Parameter(Mandatory=$true)][int]$Revision,
    [Parameter(Mandatory=$true)][string]$MinimumClient,
    [Parameter(Mandatory=$true)][string]$MinimumServer,
    [Parameter(Mandatory=$true)][string]$Reason,
    [string]$KeyPath = "$PSScriptRoot/../.local/security/policy-private.xml"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
foreach ($v in @($MinimumClient,$MinimumServer)) {
    if ($v -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or ([version]$v).Major -gt 65534 -or ([version]$v).Minor -gt 65534 -or ([version]$v).Build -gt 65534) { throw 'Invalid version' }
}
if ($Revision -lt 1 -or $Reason.Length -gt 1000) { throw 'Invalid policy' }
$path = Join-Path $root 'security/policy.json'
if (Test-Path $path) {
    $oldEnvelope = Get-Content $path -Raw | ConvertFrom-Json
    $old = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($oldEnvelope.payload)) | ConvertFrom-Json
    if ($Revision -le $old.revision -or [version]$MinimumClient -lt [version]$old.minimumClientVersion -or [version]$MinimumServer -lt [version]$old.minimumServerVersion) { throw 'Revision must increase; safety floors cannot decrease' }
}
$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new()
$rsa.PersistKeyInCsp = $false
try {
    $rsa.FromXmlString([IO.File]::ReadAllText((Resolve-Path $KeyPath)))
    $key = Get-Content (Join-Path $root 'reservation-service/policy-key.json') -Raw | ConvertFrom-Json
    if ([Convert]::ToBase64String($rsa.ExportParameters($false).Modulus) -ne $key.modulus) { throw 'Wrong signing key' }
    $payload = [Text.Encoding]::UTF8.GetBytes((@{schema=1; revision=$Revision; minimumClientVersion=$MinimumClient; minimumServerVersion=$MinimumServer; reason=$Reason} | ConvertTo-Json -Compress))
    $signature = $rsa.SignData($payload, [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, (@{payload=[Convert]::ToBase64String($payload);signature=[Convert]::ToBase64String($signature)} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
} finally { $rsa.Dispose() }
Write-Output 'Signed policy prepared locally. Consumers activate it only when the required releases exist. Review and publish separately.'
