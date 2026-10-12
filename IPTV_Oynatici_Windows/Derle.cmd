@echo off
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK gerekli: https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Derle.ps1"
if errorlevel 1 (
  echo Derleme basarisiz. Hata metnini paylasin.
  pause
  exit /b 1
)
echo EXE: %~dp0yayin\JOY_IPTV_Player.exe
echo UYARI: yayin\libvlc klasorunu EXE'den ayirmayin.
pause
