VideoVault V8 Universal Tab (yt-dlp) - experimental source

Build on Windows with .NET 10: run BUILD_EXE.cmd.
Put official yt-dlp.exe and ffmpeg.exe into bin/ before building.

Downloader tab (default): YouTube + Shorts + playlists use YoutubeExplode, TikTok uses Tikwm, Instagram uses yt-dlp (unchanged).
Universal tab: other HTTPS public media URLs use yt-dlp via separate /api/universal/info and /api/universal/download endpoints.
YouTube and TikTok URLs are explicitly rejected in Universal so they cannot override their original engines.
One media item at a time, MP4 or MP3. Public content only; availability varies by website.

This source has not been compiled on Windows or live-tested against sites in this environment.

Security note: Universal accepts general HTTPS domains. Only run this local server on your own machine; avoid untrusted links.
