$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet publish IPTVPlayer.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o yayin
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish basarisiz oldu.' }

$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$package = Join-Path $cache 'videolan.libvlc.windows\3.0.21'
if (-not (Test-Path $package)) { throw "LibVLC NuGet paketi bulunamadi: $package" }
$native = Get-ChildItem -LiteralPath $package -Recurse -Filter 'libvlc.dll' -File |
    Where-Object { $_.FullName -match 'win-x64|x64' } | Select-Object -First 1
if (-not $native) { throw "Pakette x64 libvlc.dll bulunamadi: $package" }
$destination = Join-Path $PSScriptRoot 'yayin\libvlc\win-x64'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $native.DirectoryName '*') -Destination $destination -Recurse -Force
if (-not (Test-Path (Join-Path $destination 'libvlc.dll'))) { throw 'libvlc.dll kopyalanamadi.' }
if (-not (Test-Path (Join-Path $destination 'libvlccore.dll'))) { throw 'libvlccore.dll kopyalanamadi.' }
Write-Host "VLC dosyalari hazir: $destination"
