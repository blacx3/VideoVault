@echo off
setlocal
cd /d "%~dp0"
echo.
echo =======================================
echo   VideoVault V8 - Desktop EXE Builder
echo =======================================
echo.
where dotnet >nul 2>nul
if errorlevel 1 (
 echo ERROR: .NET 10 SDK not found. Install the .NET 10 SDK first.
 pause
 exit /b 1
)
if not exist "bin\ffmpeg.exe" (
 echo WARNING: bin\ffmpeg.exe missing. High-quality YouTube merging needs it.
 echo Copy ffmpeg.exe into bin before building if you want merging.
 echo.
)
if not exist "bin\yt-dlp.exe" (
 echo WARNING: bin\yt-dlp.exe missing. Instagram downloads will not work.
 echo Download it from https://github.com/yt-dlp/yt-dlp/releases/latest
 echo.
)
dotnet publish "VideoVault.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "publish"
if errorlevel 1 (
 echo.
 echo BUILD FAILED. Read the errors above.
 pause
 exit /b 1
)
echo.
echo BUILD COMPLETE: %~dp0publish\VideoVault.exe
echo Keep all files in publish together, including wwwroot and bin.
pause
