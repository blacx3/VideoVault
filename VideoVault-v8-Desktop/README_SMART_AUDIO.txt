SMART AUDIO OPTIONS - VIDEOVAULT V8 DESKTOP

Based on VideoVault-v8-Desktop-MP3-UI-FIX.zip. Only VideoVaultBackend.cs
is modified. All other original files are byte-identical.

- Two or more known language codes: show all audio choices as before.
- One known language and no unknown tracks: show the highest-bitrate
  audio stream once per resolution and one MP3 option.
- Zero known language codes: show highest-bitrate audio once per resolution
  and one MP3 option, labeled LANGUAGE UNVERIFIED.
- Mixed known and unknown codes: keep all choices to avoid concealing
  potentially different languages.
- Duplicate video-only streams at the same resolution are collapsed in
  the single-language/unknown-fallback case, preferring video bitrate.
- Direct muxed MP4 formats are left unchanged, since their audio is fixed.

IMPORTANT: Without language metadata, the application CANNOT reliably tell
whether a video has one or several languages. The zero-known-language
fallback is a user-requested convenience heuristic, NOT true detection.

Download keys retain original stream indices. FFmpeg, MP3, TikTok, temp
cleanup, and desktop shutdown logic are untouched.

BUILD: Extract; copy ffmpeg.exe to bin; run BUILD_EXE.cmd.
This ZIP passed integrity checks; not compiled/tested on Windows here.
