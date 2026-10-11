@echo off
cd /d "%~dp0"
dotnet build LanDrop.slnx -c Release
if errorlevel 1 goto end
dotnet run --project src\LanDrop.Desktop -c Release --no-build
:end
pause
