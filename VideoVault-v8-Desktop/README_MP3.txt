VideoVault V8 Desktop - MP3 Edition
===================================
Based on VideoVault-v8-Desktop-TEMP-CLEANUP-FIX.

NEW
- YouTube: MP3 audio-only options, retaining detected language names.
- TikTok: MP3 audio extraction option.
- MP3 conversion uses existing bin\ffmpeg.exe with libmp3lame (-q:a 2).
- Correct audio/mpeg MIME type and .mp3 filename.
- Temporary source files deleted after successful conversion; final output
  cleaned after download response as in the preceding temp-cleanup version.

EXISTING
- YouTube/TikTok MP4, language names, desktop WebView2, clean shutdown,
  BUILD_EXE.cmd all retained.

BUILD
1. Extract to a NEW folder, keep your known-good build backed up.
2. Copy ffmpeg.exe into bin\ffmpeg.exe.
3. Run BUILD_EXE.cmd.
4. Run publish\VideoVault.exe.

STATUS: ZIP integrity and source transformations verified. Windows .NET
compilation and actual downloads were NOT tested in this environment.
