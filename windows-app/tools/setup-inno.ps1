param([string]$Destination = (Join-Path $PSScriptRoot '..\..\.local\tools\inno'))
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
$compiler = Join-Path $Destination 'ISCC.exe'
if (Test-Path $compiler) {
    if ((Get-Item $compiler).VersionInfo.ProductVersion -notlike '6.7.0*') { throw 'Unexpected Inno Setup compiler version.' }
    Write-Output $compiler
    exit 0
}
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$setup = Join-Path $Destination 'inno-setup.exe'
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_0/innosetup-6.7.0.exe' -OutFile $setup
if ((Get-FileHash $setup -Algorithm SHA256).Hash -ne 'F45C7D68D1E660CF13877EC36738A5179CE72A33414F9959D35E99B68C52A697') { throw 'Inno Setup download integrity check failed.' }
$process = Start-Process -FilePath $setup -ArgumentList @('/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', ('/DIR="' + $Destination + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path $compiler)) { throw 'Inno Setup compiler installation failed.' }
Write-Output $compiler
