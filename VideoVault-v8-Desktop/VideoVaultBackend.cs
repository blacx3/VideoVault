using System.Collections.Concurrent;
using System.Diagnostics;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using System.Text.Json;


internal static class VideoVaultBackend
{
    private static async Task ConvertToMp3(string ffmpeg, string input, string output)
    {
        var start = new ProcessStartInfo(ffmpeg) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = false
        };
        foreach (var arg in new[] {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
            "-i", input, "-vn", "-map", "0:a:0",
            "-codec:a", "libmp3lame", "-q:a", "2", output
        }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("FFmpeg could not start.");
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("MP3 conversion failed: " + error[..Math.Min(error.Length, 250)]);
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
            throw new InvalidOperationException("FFmpeg produced an empty MP3.");
    }

    internal static WebApplication Create()
    {
var builder = WebApplication.CreateBuilder(Array.Empty<string>());
builder.WebHost.UseUrls("http://127.0.0.1:3000");
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
var youtube = new YoutubeClient();
var jobs = new ConcurrentDictionary<string, DownloadJob>();
var tikTok = new TikTokProvider();
var insta = new InstagramYtDlpProvider(app.Environment.ContentRootPath);
var universal = new UniversalYtDlpProvider(app.Environment.ContentRootPath);
var ffmpeg = Path.Combine(app.Environment.ContentRootPath, "bin", OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

static string? VideoId(string? raw) {
    if (string.IsNullOrWhiteSpace(raw) || raw.Length > 2048 || !Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
    if (uri.Scheme != "https" && uri.Scheme != "http") return null;
    if (!(new[] { "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be", "music.youtube.com" }).Contains(uri.Host.ToLowerInvariant())) return null;
    var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    string? id = uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ? parts.FirstOrDefault() :
        uri.AbsolutePath == "/watch" ? uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p[0] == "v")?.ElementAtOrDefault(1) :
        parts.Length > 1 && (parts[0] == "shorts" || parts[0] == "live") ? parts[1] : null;
    return id is { Length: 11 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-') ? id : null;
}
static string Message(Exception ex) => ex.GetBaseException().Message[..Math.Min(240, ex.GetBaseException().Message.Length)];
static string AudioLabel(AudioOnlyStreamInfo audio, int index) {
    // Some YouTube stream URLs include xtags=acont=original:lang=en or
    // xtags=acont=dubbed:lang=fr. Only label tracks when a language is
    // explicitly present. Never assume the track index indicates language.
    string? url = audio.Url;
    string? languageCode = null;
    bool original = false;
    if (!string.IsNullOrWhiteSpace(url)) {
        // URL query parameters can be percent-encoded once or twice.
        var decoded = url;
        for (int pass = 0; pass < 3; pass++) {
            var match = System.Text.RegularExpressions.Regex.Match(
                decoded,
                @"(?:^|[?&:,])lang=([a-zA-Z]{2,3}(?:[-_][a-zA-Z0-9]{2,8})*)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success) {
                languageCode = match.Groups[1].Value.Replace('_', '-');
                original = System.Text.RegularExpressions.Regex.IsMatch(decoded,
                    @"(?:^|[?&:,])acont=original(?:[&:,]|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                break;
            }
            var next = Uri.UnescapeDataString(decoded);
            if (next == decoded) break;
            decoded = next;
        }
    }
    string? languageName = null;
    if (languageCode is not null) {
        try {
            languageName = System.Globalization.CultureInfo.GetCultureInfo(languageCode).EnglishName;
        } catch (System.Globalization.CultureNotFoundException) {
            languageName = languageCode;
        }
    }
    var label = languageName is null ? $"Unknown language · Track {index + 1}"
        : languageName + (original ? " (Original)" : "");
    return $"{label} ({audio.Bitrate.BitsPerSecond / 1000.0:0} kbps)";
}
static string? AudioLanguageCode(AudioOnlyStreamInfo audio) {
    var decoded = audio.Url ?? "";
    for (int pass = 0; pass < 3; pass++) {
        var match = System.Text.RegularExpressions.Regex.Match(decoded,
            @"(?:^|[?&:,])lang=([a-zA-Z]{2,3}(?:[-_][a-zA-Z0-9]{2,8})*)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value.Replace('_', '-').ToLowerInvariant();
        var next = Uri.UnescapeDataString(decoded);
        if (next == decoded) break;
        decoded = next;
    }
    return null;
}
static object[] Formats(StreamManifest manifest, bool merge) {
    var muxed = manifest.GetMuxedStreams().Where(s => s.Container.Name == "mp4").ToArray();
    var videos = manifest.GetVideoOnlyStreams().Where(s => s.Container.Name == "mp4").ToArray();
    var audios = manifest.GetAudioOnlyStreams().Where(s => s.Container.Name == "mp4").ToArray();
    // Language tags are trustworthy when present; missing tags are not proof of a single language.
    var languageCodes = audios.Select(AudioLanguageCode).ToArray();
    var distinctLanguages = languageCodes.Where(c => c is not null).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    var hasUnlabeled = languageCodes.Any(c => c is null);
    // If the metadata clearly identifies one language and every stream is labeled,
    // select the highest-bitrate track. If no streams have language metadata,
    // use best-bitrate fallback as requested, but label it as unverified.
    var singleLanguage = distinctLanguages == 1 && !hasUnlabeled;
    var unknownFallback = distinctLanguages == 0;
    var collapse = singleLanguage || unknownFallback;
    var bestAudioIndex = audios.Length == 0 ? -1 : Enumerable.Range(0, audios.Length)
        .OrderByDescending(i => audios[i].Bitrate.BitsPerSecond).First();
    string BestLabel() => unknownFallback ? $"Best audio · language unverified ({audios[bestAudioIndex].Bitrate.BitsPerSecond / 1000.0:0} kbps)"
        : $"{AudioLabel(audios[bestAudioIndex], bestAudioIndex)} · best bitrate";

    // Deduplicate video resolutions in single-language/fallback mode. Preserve the
    // original video index in the key, so the existing download parser still works.
    var videoIndices = collapse
        ? Enumerable.Range(0, videos.Length).GroupBy(i => videos[i].VideoQuality.MaxHeight)
            .Select(g => g.OrderByDescending(i => videos[i].Bitrate.BitsPerSecond).First()).ToArray()
        : Enumerable.Range(0, videos.Length).ToArray();
    IEnumerable<object> mux = muxed.Select((stream, index) => new {
        key = $"mux-{index}", height = stream.VideoQuality.MaxHeight,
        label = $"{stream.VideoQuality.MaxHeight}p MP4 · direct (audio fixed)"
    });
    IEnumerable<object> separate = merge && audios.Length > 0
        ? videoIndices.SelectMany(videoIndex =>
            (collapse ? new[] { bestAudioIndex } : Enumerable.Range(0, audios.Length))
            .Select(audioIndex => new {
                key = $"sep-{videoIndex}-{audioIndex}",
                height = videos[videoIndex].VideoQuality.MaxHeight,
                label = $"{videos[videoIndex].VideoQuality.MaxHeight}p MP4 · " +
                    (collapse ? BestLabel() : AudioLabel(audios[audioIndex], audioIndex))
            }))
        : Enumerable.Empty<object>();
    IEnumerable<object> mp3 = merge && audios.Length > 0
        ? (collapse ? new[] { bestAudioIndex } : Enumerable.Range(0, audios.Length))
            .Select(audioIndex => new {
                key = $"mp3-{audioIndex}", height = 0,
                label = "MP3 · " + (collapse ? BestLabel() : AudioLabel(audios[audioIndex], audioIndex))
            })
        : Enumerable.Empty<object>();
    return mux.Concat(separate).Concat(mp3).ToArray();
}
app.MapGet("/api/health", () => Results.Ok(new { ok = true, backend = "YoutubeExplode", ffmpeg = File.Exists(ffmpeg) }));

// Playlist discovery is bounded to avoid accidentally queuing thousands of videos.
app.MapPost("/api/playlist", async (PlaylistRequest input) => {
    if (string.IsNullOrWhiteSpace(input.Url) || input.Url.Length > 2048 ||
        !Uri.TryCreate(input.Url, UriKind.Absolute, out var uri) ||
        uri.Scheme != "https" ||
        !(new[] { "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com" }).Contains(uri.Host.ToLowerInvariant()))
        return Results.BadRequest(new { error = "Enter a YouTube playlist URL." });
    var playlistId = uri.Query.TrimStart('?').Split('&')
        .Select(x => x.Split('=', 2)).FirstOrDefault(x => x[0] == "list")?.ElementAtOrDefault(1);
    if (string.IsNullOrWhiteSpace(playlistId) || playlistId.Length > 100 ||
        !playlistId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        return Results.BadRequest(new { error = "The link has no valid playlist ID." });
    try {
        var items = new List<object>();
        await foreach (var video in youtube.Playlists.GetVideosAsync(playlistId)) {
            var id = video.Id.Value;
            items.Add(new { id, title = video.Title, author = video.Author.ChannelTitle,
                thumbnail = $"https://i.ytimg.com/vi/{id}/mqdefault.jpg",
                url = $"https://www.youtube.com/watch?v={id}" });
            if (items.Count >= 200) break;
        }
        return Results.Ok(new { videos = items, truncated = items.Count >= 200 });
    } catch (Exception ex) {
        Console.Error.WriteLine(ex);
        return Results.Json(new { error = "Playlist lookup failed: " + Message(ex) }, statusCode: 502);
    }
});
// Universal tab uses its own endpoints; it cannot intercept the legacy providers.
app.MapPost("/api/universal/info", async (InfoRequest input) => {
    if (!UniversalYtDlpProvider.IsUniversalUrl(input.Url))
        return Results.BadRequest(new { error = "Use a public HTTPS media URL. YouTube and TikTok belong in Downloader." });
    try {
        var media = await universal.GetInfoAsync(input.Url);
        return Results.Ok(new {
            id = media.Id, title = media.Title, author = media.Author, thumbnail = media.Thumbnail,
            formats = new[] {
                new { key = "universal-mp4", label = "MP4 · best available" },
                new { key = "universal-mp3", label = "MP3 · audio only" }
            }
        });
    } catch (Exception ex) {
        Console.Error.WriteLine(ex);
        return Results.Json(new { error = "Universal lookup failed: " + Message(ex) }, statusCode: 502);
    }
});
app.MapPost("/api/universal/download", (DownloadRequest input) => {
    if (!UniversalYtDlpProvider.IsUniversalUrl(input.Url) || !input.Permission ||
        input.Key is not ("universal-mp4" or "universal-mp3"))
        return Results.BadRequest(new { error = "Invalid URL, format, or permission not confirmed." });
    if (!File.Exists(ffmpeg))
        return Results.BadRequest(new { error = "FFmpeg is required. Place ffmpeg.exe in the bin folder." });
    var job = new DownloadJob();
    var token = Guid.NewGuid().ToString("N");
    jobs[token] = job;
    _ = Task.Run(async () => {
        var folder = Path.Combine(Path.GetTempPath(), "videovault-" + token);
        try {
            Directory.CreateDirectory(folder);
            job.State = "Starting universal download";
            var file = await universal.DownloadAsync(input.Url, folder, ffmpeg,
                input.Key == "universal-mp3", (percent, stage) => {
                    job.Progress = percent; job.State = stage;
                });
            job.File = file;
            job.Progress = 100;
            job.State = "Ready";
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            job.Error = Message(ex);
            job.State = "Failed";
            try { Directory.Delete(folder, true); } catch { }
        }
    });
    return Results.Ok(new { token });
});
app.MapPost("/api/info", async (InfoRequest input) => {
    if (InstagramYtDlpProvider.IsInstagramUrl(input.Url)) {
        try {
            var media = await insta.GetInfoAsync(input.Url);
            return Results.Ok(new {
                id = media.Id, title = media.Title, author = media.Author, thumbnail = media.Thumbnail,
                formats = File.Exists(ffmpeg)
                    ? new[] { new { key = "ig-mp4", height = 0, label = "Instagram MP4 · best available" },
                              new { key = "ig-mp3", height = 0, label = "Instagram MP3 · audio only" } }
                    : new[] { new { key = "ig-mp4", height = 0, label = "Instagram MP4 · best available" } },
                notice = "Public Instagram media found."
            });
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            return Results.Json(new { error = "Instagram lookup failed: " + Message(ex) }, statusCode: 502);
        }
    }
    if (TikTokProvider.IsTikTokUrl(input.Url)) {
        try {
            var item = await tikTok.ResolveAsync(input.Url);
            return Results.Ok(new { id = item.Id, title = item.Title, author = item.Author,
                thumbnail = item.Thumbnail, formats = File.Exists(ffmpeg)
                    ? new[] { new { key = "tt-mp4", height = 0, label = "TikTok MP4" }, new { key = "tt-mp3", height = 0, label = "TikTok MP3 · audio only" } }
                    : new[] { new { key = "tt-mp4", height = 0, label = "TikTok MP4" } },
                notice = "TikTok video available." });
        } catch (Exception ex) { Console.Error.WriteLine(ex); return Results.Json(new { error = "TikTok lookup failed: " + Message(ex) }, statusCode: 502); }
    }
    var id = VideoId(input.Url);
    if (id is null) return Results.BadRequest(new { error = "Enter a valid YouTube, TikTok or Instagram video URL." });
    try {
        var video = await youtube.Videos.GetAsync(id);
        var manifest = await youtube.Videos.Streams.GetManifestAsync(id);
        var formats = Formats(manifest, File.Exists(ffmpeg));
        return Results.Ok(new { id, title = video.Title, author = video.Author.ChannelTitle, thumbnail = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg", formats, notice = formats.Length == 0 ? "No compatible streams found." : "Choose a quality to download." });
    } catch (Exception ex) { Console.Error.WriteLine(ex); return Results.Json(new { error = Message(ex) }, statusCode: 502); }
});
app.MapPost("/api/download", async (DownloadRequest input) => {
    var id = VideoId(input.Url);
    if ((id is null && !TikTokProvider.IsTikTokUrl(input.Url) && !InstagramYtDlpProvider.IsInstagramUrl(input.Url)) || !input.Permission || string.IsNullOrWhiteSpace(input.Key)) return Results.BadRequest(new { error = "Invalid request or permission not confirmed." });
    var job = new DownloadJob();
    var token = Guid.NewGuid().ToString("N");
    jobs[token] = job;
    _ = Task.Run(async () => {
        var folder = Path.Combine(Path.GetTempPath(), "videovault-" + token);
        try {
            Directory.CreateDirectory(folder);
            job.State = "Fetching streams";
            if (InstagramYtDlpProvider.IsInstagramUrl(input.Url)) {
                if (input.Key != "ig-mp4" && input.Key != "ig-mp3")
                    throw new InvalidOperationException("Invalid Instagram format.");
                if (!File.Exists(ffmpeg))
                    throw new InvalidOperationException("FFmpeg is required for Instagram video and MP3.");
                var mp3 = input.Key == "ig-mp3";
                job.State = "Downloading Instagram media";
                var result = await insta.DownloadAsync(input.Url, folder, ffmpeg, mp3,
                    (percent, stage) => { job.Progress = percent; job.State = stage; });
                job.File = result;
                job.Progress = 100;
                job.State = "Ready";
                return;
            }
            if (TikTokProvider.IsTikTokUrl(input.Url))
            {
                if (input.Key != "tt-mp4" && input.Key != "tt-mp3")
                    throw new InvalidOperationException("Invalid TikTok format selection.");
                if (input.Key == "tt-mp3" && !File.Exists(ffmpeg))
                    throw new InvalidOperationException("FFmpeg not found in bin folder.");
                var tiktokFile = Path.Combine(folder, "result.mp4");
                job.State = "Downloading TikTok video";
                await tikTok.DownloadAsync(input.Url, tiktokFile,
                    value => job.Progress = Math.Round(value * (input.Key == "tt-mp3" ? 85 : 100)));
                if (input.Key == "tt-mp3") {
                    var mp3File = Path.Combine(folder, "result.mp3");
                    job.State = "Converting to MP3";
                    await ConvertToMp3(ffmpeg, tiktokFile, mp3File);
                    File.Delete(tiktokFile);
                    job.File = mp3File;
                } else job.File = tiktokFile;
                job.State = "Ready";
                job.Progress = 100;
                return;
            }
            var manifest = await youtube.Videos.Streams.GetManifestAsync(id);
            var parts = input.Key.Split('-');
            if (parts.Length < 2 ||
                !int.TryParse(parts[1], out var streamIndex) || streamIndex < 0 || streamIndex > 1000 ||
                !((parts[0] == "mux" && parts.Length == 2) ||
                  (parts[0] == "sep" && parts.Length == 3) ||
                  (parts[0] == "mp3" && parts.Length == 2)))
                throw new InvalidOperationException("Invalid quality selection.");
            var file = Path.Combine(folder, parts[0] == "mp3" ? "result.mp3" : "result.mp4");
            if (parts[0] == "mp3") {
                if (!File.Exists(ffmpeg)) throw new InvalidOperationException("FFmpeg not found in bin folder.");
                var audio = manifest.GetAudioOnlyStreams().Where(s => s.Container.Name == "mp4")
                    .Skip(streamIndex).FirstOrDefault()
                    ?? throw new InvalidOperationException("Selected audio track is no longer available.");
                var audioFile = Path.Combine(folder, "audio.m4a");
                job.State = "Downloading audio";
                await youtube.Videos.Streams.DownloadAsync(audio, audioFile,
                    new Progress<double>(v => job.Progress = Math.Round(v * 85)));
                job.State = "Converting to MP3";
                await ConvertToMp3(ffmpeg, audioFile, file);
                File.Delete(audioFile);
            } else if (parts[0] == "mux") {
                var stream = manifest.GetMuxedStreams().Where(s => s.Container.Name == "mp4").Skip(streamIndex).FirstOrDefault() ?? throw new InvalidOperationException("Selected format unavailable.");
                await youtube.Videos.Streams.DownloadAsync(stream, file, new Progress<double>(v => job.Progress = Math.Round(v * 100)));
            } else if (parts[0] == "sep") {
                if (!File.Exists(ffmpeg)) throw new InvalidOperationException("FFmpeg not found in bin folder.");
                var video = manifest.GetVideoOnlyStreams().Where(s => s.Container.Name == "mp4").Skip(streamIndex).FirstOrDefault() ?? throw new InvalidOperationException("Selected video stream unavailable.");
                if (parts.Length != 3 || !int.TryParse(parts[2], out var audioIndex) || audioIndex < 0 || audioIndex > 1000)
                    throw new InvalidOperationException("Invalid audio track selection.");
                var audio = manifest.GetAudioOnlyStreams().Where(s => s.Container.Name == "mp4").Skip(audioIndex).FirstOrDefault()
                    ?? throw new InvalidOperationException("Selected audio track is no longer available.");
                var videoFile = Path.Combine(folder, "video.mp4"); var audioFile = Path.Combine(folder, "audio.m4a");
                job.State = "Downloading video";
                await youtube.Videos.Streams.DownloadAsync(video, videoFile, new Progress<double>(v => job.Progress = Math.Round(v * 45)));
                job.State = "Downloading audio";
                await youtube.Videos.Streams.DownloadAsync(audio, audioFile, new Progress<double>(v => job.Progress = 45 + Math.Round(v * 45)));
                job.State = "Merging MP4";
                var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-nostdin", "-y", "-i", videoFile, "-i", audioFile, "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", "-movflags", "+faststart", file }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg could not start.");
                var stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new InvalidOperationException("FFmpeg: " + (await stderr)[..Math.Min(250, (await stderr).Length)]);
                // Intermediate tracks are no longer needed after a successful merge.
                try { File.Delete(videoFile); File.Delete(audioFile); }
                catch (IOException ex) { Console.Error.WriteLine("Intermediate cleanup: " + ex.Message); }
            } else throw new InvalidOperationException("Invalid quality selection.");
            job.File = file; job.State = "Ready"; job.Progress = 100;
        } catch (Exception ex) { Console.Error.WriteLine(ex); job.State = "Failed"; job.Error = Message(ex); try { Directory.Delete(folder, true); } catch {} }
    });
    return Results.Ok(new { token });
});
app.MapGet("/api/status/{token}", (string token) => jobs.TryGetValue(token, out var job) ? Results.Ok(new { state = job.State, progress = job.Progress, error = job.Error }) : Results.NotFound());
app.MapGet("/api/file/{token}", (HttpContext context, string token) => {
    if (!jobs.TryGetValue(token, out var job) || job.State != "Ready" || job.File is null || !File.Exists(job.File))
        return Results.NotFound();
    var file = job.File;
    var folder = Path.GetDirectoryName(file)!;
    // Delete only after ASP.NET has finished sending the file response.
    // A failed/interrupted transfer leaves the file available for retry.
    context.Response.OnCompleted(() => {
        if (context.RequestAborted.IsCancellationRequested) return Task.CompletedTask;
        try {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            jobs.TryRemove(token, out _);
        } catch (Exception ex) {
            Console.Error.WriteLine("Temp cleanup failed: " + ex.Message);
        }
        return Task.CompletedTask;
    });
    var isMp3 = Path.GetExtension(file).Equals(".mp3", StringComparison.OrdinalIgnoreCase);
    return Results.File(file, isMp3 ? "audio/mpeg" : "video/mp4",
        isMp3 ? "videovault-audio.mp3" : "videovault-video.mp4");
});
return app;

    }
}

record InfoRequest(string Url);
record DownloadRequest(string Url, string Key, bool Permission);
sealed class DownloadJob { public volatile string State = "Starting"; public volatile string? Error; public volatile string? File; public double Progress; }

record PlaylistRequest(string Url);
