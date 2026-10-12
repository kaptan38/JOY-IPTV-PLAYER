$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet restore IPTVPlayer.csproj -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore basarisiz oldu.' }
$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$package = Join-Path $cache 'videolan.libvlc.windows\3.0.21'
$native = Get-ChildItem -LiteralPath $package -Recurse -Filter 'libvlc.dll' -File |
    Where-Object { $_.FullName -match 'win-x64|x64' } | Select-Object -First 1
if (-not $native) { throw 'x64 VLC kitapligi bulunamadi.' }
if (-not (Test-Path (Join-Path $native.DirectoryName 'plugins'))) { throw 'VLC eklentileri eksik.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$embedded = Join-Path $PSScriptRoot 'LibVLC.zip'
if (Test-Path $embedded) { Remove-Item $embedded -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($native.DirectoryName, $embedded,
    [IO.Compression.CompressionLevel]::Optimal, $false)
if (Test-Path 'yayin') { Remove-Item 'yayin' -Recurse -Force }
dotnet publish IPTVPlayer.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o yayin
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish basarisiz oldu.' }
Get-ChildItem 'yayin' | Where-Object { $_.Name -ne 'JOY_IPTV_Player.exe' } |
    Remove-Item -Recurse -Force
if (-not (Test-Path 'yayin\JOY_IPTV_Player.exe')) { throw 'EXE olusturulamadi.' }
Write-Host 'Tek EXE hazir: yayin\JOY_IPTV_Player.exe'
