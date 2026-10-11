@echo off
cd /d "%~dp0"
dotnet run --project src\LanDrop -c Release --no-launch-profile
pause
