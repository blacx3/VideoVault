VideoVault V8 Desktop - Language Labels
======================================
Windows 10/11 desktop app, WinForms + WebView2. Uses the working V8 browser
backend and frontend, including TikTok and URL-derived audio-language labels.

BUILD
1. Install .NET 10 SDK (only needed for building).
2. Copy your working ffmpeg.exe to bin\ffmpeg.exe.
3. Double-click BUILD_EXE.cmd.
4. Open publish\VideoVault.exe. Keep the publish folder intact.

The EXE is self-contained for .NET; Microsoft Edge WebView2 Runtime must be
installed (normally included with Windows 11 and many Windows 10 systems).
Internet is required for video lookups/downloads and first-time NuGet restore.

Desktop UI is an embedded WebView2 window. Backend listens only on
http://127.0.0.1:3000. Close your browser version before launching desktop.

NOTES
- Preserve the entire publish directory; wwwroot is needed for the UI.
- The original browser V8 was not modified.
- Audio language names come from URL tags; unsupported tracks may say Unknown.
- Download only content you have permission to save.
- This source archive has NOT been compiled in this environment.
