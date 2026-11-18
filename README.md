
# 🎬 VideoVault

**Save Videos, Keep the Moments.**

VideoVault is a free video and audio downloader available for **Windows Desktop** and **Web**. Download content from YouTube, TikTok, Instagram, and many other supported websites through a clean, modern interface.

## ✨ Features

- 🎥 Download videos in MP4 format
- 🎵 Extract and download audio as MP3
- 📺 Choose from available video resolutions
- 📋 Playlist and download queue support
- 🌐 Universal downloader powered by yt-dlp
- 🖥️ Modern Windows desktop interface
- 🌍 Browser-based version with no installation required
- ⚡ FFmpeg-powered media processing

## 🚀 Try VideoVault Web

**[Launch VideoVault Web](https://videovault.gahhhwebs.workers.dev/)**

VideoVault Web runs directly in your browser using WebAssembly technology.

No desktop installation required!

> **Note:** Browser-based downloads may be slower than desktop downloads, especially when processing large video files.

## 🖥️ VideoVault Desktop V8

The Windows desktop edition uses native download and processing tools for better performance.

### Download Engines

| Platform | Engine |
|----------|--------|
| YouTube | YoutubeExplode |
| TikTok | Tikwm API |
| Instagram | yt-dlp |
| Universal | yt-dlp |
| Media Processing | FFmpeg |

### Requirements

- Windows 10 or 11
- WebView2 Runtime
- Internet connection

### Installation

1. Download the latest available Windows release.
2. Extract the release archive if necessary.
3. Run `VideoVault.exe`.
4. Paste a supported video URL.
5. Select your preferred format and resolution.
6. Start downloading!

## 🌐 VideoVault Web

The web edition uses:

- **yt-dlp.wasm** by Project516
- **Pyodide**
- **FFmpeg WebAssembly**
- **Cloudflare Workers** for proxy requests
- **Vite** for frontend development

### Build from Source

Install Node.js and pnpm, then run:

```bash
pnpm install
pnpm --dir packages/yt-dlp-wasm build
pnpm --dir apps/demo build
```

The production website will be generated inside:

```text
apps/demo/dist/
```

For local development:

```bash
pnpm --dir apps/demo dev
```

> The web edition may require a configured proxy for certain download requests.

## 🌍 Supported Websites

VideoVault's Universal downloader uses yt-dlp, which supports extractors for numerous websites.

Support varies depending on the platform, video availability, authentication requirements, and download restrictions.

See the official yt-dlp extractor list:

https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md

## ⚠️ Known Limitations

- Some websites may restrict automated downloads.
- Certain videos may require authentication.
- YouTube may temporarily rate-limit requests.
- Browser-based processing can be slower than native FFmpeg.
- Not every yt-dlp-supported site is guaranteed to work through VideoVault Web.

## 🛠️ Technologies

**Desktop**
- C#
- .NET
- Windows Forms
- WebView2
- YoutubeExplode
- yt-dlp
- FFmpeg

**Web**
- JavaScript
- HTML & CSS
- Vite
- WebAssembly
- Pyodide
- yt-dlp.wasm
- FFmpeg.wasm
- Cloudflare Workers

## 📜 Disclaimer

VideoVault is intended for downloading content you own, have permission to download, or are otherwise legally allowed to save.

Users are responsible for respecting copyright laws, creators' rights, and applicable website terms of service.

VideoVault is not affiliated with YouTube, TikTok, Instagram, or their parent companies.

## 🙏 Credits

- [yt-dlp](https://github.com/yt-dlp/yt-dlp)
- [Project516 yt-dlp.wasm](https://github.com/Project516/yt-dlp-wasm)
- [FFmpeg](https://ffmpeg.org/)
- [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode)
- [Pyodide](https://pyodide.org/)

---

**Made with dedication, debugging, and probably too many failed builds. 💜**
