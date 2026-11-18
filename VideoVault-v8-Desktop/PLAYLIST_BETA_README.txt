VIDEOVAULT V8 PLAYLIST BETA
Based on the user-tested Smart Audio Options desktop build.
New: YouTube playlist discovery, selection, sequential MP4/MP3 queue,
per-video format selection, failure skipping and stop-after-current.
Maximum 200 playlist items per lookup.
Individual downloads use the existing endpoints and temp cleanup.

LIMITATIONS
- Browser/WebView2 can restrict multiple automatic downloads. Allow them
  if prompted; otherwise use individual downloads.
- Windows may rename duplicate files. The existing backend still names
  downloads videovault-video.mp4 / videovault-audio.mp3.
- The queue does not persist after app restart; stop is between videos.
- Private/unavailable videos may be skipped.
- No Windows compilation or live YouTube test performed in this environment.

BUILD
Copy ffmpeg.exe into bin, run BUILD_EXE.cmd, then publish/VideoVault.exe.
Keep your tested Smart Audio Options build as a backup.
