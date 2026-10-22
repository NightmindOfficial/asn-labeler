# Copyright (c) 2026 nightmind.dev. SPDX-License-Identifier: AGPL-3.0-only
$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot '../vendor/ZXing.Net/source-v0.16.11.0.zip'
$expected = '71645FB22540334081D74ED5BB51C65037A8A774BF5B7D505F9CF7C3C8DE80BF'
if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $expected) {
    Write-Host 'Verified ZXing source is already available.'
    exit 0
}
$temporary = "$target.download"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -UseBasicParsing -Uri 'https://codeload.github.com/micjahn/ZXing.Net/zip/refs/tags/v0.16.11.0' -OutFile $temporary
if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $expected) {
    throw 'ZXing source checksum mismatch. Do not package this download; investigate upstream before changing the expected hash.'
}
Move-Item -LiteralPath $temporary -Destination $target -Force
Write-Host 'ZXing source downloaded and verified.'
