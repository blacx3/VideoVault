@echo off
cd /d "%~dp0"
dotnet run --project "VideoVault.csproj"
if errorlevel 1 pause
