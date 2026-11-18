using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

internal sealed class InstagramYtDlpProvider
{
    private readonly string _executable;
    internal InstagramYtDlpProvider(string contentRoot) =>
        _executable = Path.Combine(contentRoot, "bin", "yt-dlp.exe");

    internal static bool IsInstagramUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.Host is not ("instagram.com" or "www.instagram.com")) return false;
        var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return path.Length >= 2 && (path[0] is "reel" or "reels" or "p" or "tv")
            && Regex.IsMatch(path[1], @"^[A-Za-z0-9_-]{5,30}$");
    }

    private void CheckInstalled()
    {
        if (!File.Exists(_executable))
            throw new InvalidOperationException("yt-dlp.exe missing. Download the official Windows standalone release and put it in the bin folder.");
    }

    private async Task<(int exitCode, string output, string error)> RunAsync(
        IEnumerable<string> args, Action<string>? onLine = null)
    {
        CheckInstalled();
        var psi = new ProcessStartInfo(_executable) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not launch yt-dlp.");
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        async Task ReadAsync(StreamReader reader, System.Text.StringBuilder buffer, bool report) {
            while (await reader.ReadLineAsync() is { } line) {
                if (buffer.Length < 250_000) buffer.AppendLine(line);
                if (report) onLine?.Invoke(line);
            }
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(180));
        try {
            var readOut = ReadAsync(process.StandardOutput, stdout, true);
            var readErr = ReadAsync(process.StandardError, stderr, true);
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(readOut, readErr);
        } catch {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    internal async Task<(string Id, string Title, string Author, string Thumbnail)> GetInfoAsync(string url)
    {
        if (!IsInstagramUrl(url)) throw new InvalidOperationException("Invalid Instagram post or Reel URL.");
        var (code, output, error) = await RunAsync(new[] {
            "--ignore-config", "--no-playlist", "--skip-download",
            "--dump-single-json", "--no-warnings", url
        });
        if (code != 0) throw new InvalidOperationException(TrimError(error));
        using var doc = JsonDocument.Parse(output);
        var item = doc.RootElement;
        string Get(string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
        return (Get("id"), Get("title"), Get("uploader"), Get("thumbnail"));
    }

    internal async Task<string> DownloadAsync(string url, string folder, string ffmpeg, bool mp3,
        Action<double, string> progress)
    {
        if (!IsInstagramUrl(url)) throw new InvalidOperationException("Invalid Instagram URL.");
        var output = Path.Combine(folder, "result.%(ext)s");
        var args = new List<string> {
            "--ignore-config", "--no-playlist", "--no-warnings", "--newline",
            "--ffmpeg-location", Path.GetDirectoryName(ffmpeg)!,
            "--output", output
        };
        if (mp3) {
            args.AddRange(new[] { "--extract-audio", "--audio-format", "mp3", "--audio-quality", "2" });
        } else {
            args.AddRange(new[] { "--format", "bv*+ba/b", "--merge-output-format", "mp4",
                "--recode-video", "mp4" });
        }
        args.Add(url);
        var pattern = new Regex(@"(?<p>\d+(?:\.\d+)?)%", RegexOptions.Compiled);
        var (code, _, error) = await RunAsync(args, line => {
            var match = pattern.Match(line);
            if (match.Success && double.TryParse(match.Groups["p"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value)) {
                progress(Math.Clamp(value * 0.9, 0, 90), "Downloading Instagram media");
            } else if (line.Contains("[ExtractAudio]") || line.Contains("[VideoConvertor]") ||
                       line.Contains("[Merger]")) {
                progress(92, "Processing with FFmpeg");
            }
        });
        if (code != 0) throw new InvalidOperationException(TrimError(error));
        var ext = mp3 ? ".mp3" : ".mp4";
        var result = Path.Combine(folder, "result" + ext);
        if (!File.Exists(result) || new FileInfo(result).Length == 0)
            throw new InvalidOperationException("yt-dlp finished but no " + ext + " file was produced.");
        foreach (var file in Directory.GetFiles(folder))
            if (file != result) try { File.Delete(file); } catch { }
        return result;
    }

    private static string TrimError(string error)
    {
        var line = error.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(s => s.Contains("ERROR:", StringComparison.OrdinalIgnoreCase))
            ?? error.Trim();
        return line.Length > 300 ? line[..300] : line;
    }
}
