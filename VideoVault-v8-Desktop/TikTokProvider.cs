using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

// Separate from the proven YoutubeExplode implementation.
// Third-party Tikwm service may change or stop working.
internal sealed record TikTokVideo(string Id, string Title, string Author, string Thumbnail, Uri MediaUrl);

internal sealed class TikTokProvider
{
    private static readonly HttpClient Api = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly HttpClient Media = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromMinutes(10) };
    private const long MaxBytes = 250L * 1024 * 1024;

    internal static bool IsTikTokUrl(string? raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == "https" && (uri.Host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase));
    }

    internal async Task<TikTokVideo> ResolveAsync(string? url)
    {
        if (!IsTikTokUrl(url) || url!.Length > 2048)
            throw new InvalidOperationException("Enter a valid HTTPS TikTok video URL.");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["url"] = url, ["hd"] = "1" });
        using var response = await Api.PostAsync("https://www.tikwm.com/api/", form);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("TikTok provider unavailable (HTTP " + (int)response.StatusCode + ").");
        using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;
        if (!root.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The provider could not resolve this public video.");
        var link = Get(data, "hdplay");
        if (string.IsNullOrEmpty(link)) link = Get(data, "play");
        if (!Uri.TryCreate(link, UriKind.Absolute, out var mediaUrl) || mediaUrl.Scheme != "https")
            throw new InvalidOperationException("Provider returned no valid HTTPS video link.");
        var author = data.TryGetProperty("author", out var authorObj) && authorObj.ValueKind == JsonValueKind.Object
            ? Get(authorObj, "nickname") : "TikTok";
        return new TikTokVideo(Get(data, "id") ?? "tiktok", Get(data, "title") ?? "TikTok video",
            author ?? "TikTok", Get(data, "cover") ?? "", mediaUrl);
    }

    internal async Task DownloadAsync(string? url, string path, Action<double> progress)
    {
        var item = await ResolveAsync(url);
        // The API chooses the media URL. Only HTTPS, public DNS, and non-local IPs are allowed.
        // Redirects are checked individually, preventing redirects to local services.
        Uri current = item.MediaUrl;
        for (int redirects = 0; redirects < 5; redirects++)
        {
            await ValidateRemoteAsync(current);
            using var response = await Media.GetAsync(current, HttpCompletionOption.ResponseHeadersRead);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                current = response.Headers.Location is { } location ? new Uri(current, location)
                    : throw new InvalidOperationException("Video redirect missing destination.");
                continue;
            }
            response.EnsureSuccessStatusCode();
            var size = response.Content.Headers.ContentLength;
            if (size > MaxBytes) throw new InvalidOperationException("TikTok video exceeds 250 MB limit.");
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Provider returned a webpage instead of video.");
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(path);
            byte[] buffer = new byte[128 * 1024]; long total = 0; int count;
            while ((count = await input.ReadAsync(buffer)) != 0)
            {
                total += count;
                if (total > MaxBytes) throw new InvalidOperationException("TikTok video exceeds 250 MB limit.");
                await output.WriteAsync(buffer.AsMemory(0, count));
                if (size is > 0) progress(Math.Min(1, (double)total / size.Value));
            }
            if (total == 0) throw new InvalidOperationException("Downloaded video was empty.");
            progress(1);
            return;
        }
        throw new InvalidOperationException("Too many video redirects.");
    }

    private static async Task ValidateRemoteAsync(Uri uri)
    {
        if (uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Port != 443 ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsafe video destination.");
        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost);
        if (addresses.Length == 0 || addresses.Any(ip => !IsPublic(ip)))
            throw new InvalidOperationException("Video destination is not public.");
    }

    private static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return !ip.GetAddressBytes().Take(2).SequenceEqual(new byte[] { 0xfc, 0x00 }) &&
                   (ip.GetAddressBytes()[0] & 0xfe) != 0xfc;
        var b = ip.GetAddressBytes();
        return b[0] != 0 && b[0] != 10 && b[0] != 127 &&
            !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
            !(b[0] == 192 && (b[1] == 168 || b[1] == 0)) &&
            !(b[0] == 169 && b[1] == 254) &&
            !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) &&
            !(b[0] == 198 && (b[1] == 18 || b[1] == 19)) &&
            b[0] < 224;
    }

    private static string? Get(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
