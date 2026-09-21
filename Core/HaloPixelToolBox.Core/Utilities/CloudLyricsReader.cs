using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HaloPixelToolBox.Core.Models.Bar;
using XFEExtension.NetCore.MemoryEditor;
using XFEExtension.NetCore.StringExtension;

namespace HaloPixelToolBox.Core.Utilities;

public class CloudMusicLyricsReader
{
    private const string CachedLyricsVersion = "3.1.41";
    private const long PlayerStateBaseAddress = 0x02214AB0;
    private const int PlaybackPositionOffset = 0xB8;
    private const long MaximumPlaybackPositionMilliseconds = 24 * 60 * 60 * 1000;
    private static readonly nint[] PlayerStateOffsets = [0x0];
    private static readonly HttpClient LyricsHttpClient = CreateLyricsHttpClient();

    private readonly object _timedLyricsLock = new();
    private TimedLyricLine[] _timedLyrics = [];
    private string _timedLyricsTrackIdentity = string.Empty;
    private bool _lyricsLoadInProgress;
    private bool _lyricsLoadCompleted;
    private int _lyricsLoadGeneration;
    private DateTime _nextLyricsLoadAttemptUtc = DateTime.MinValue;

    public nint Address { get; set; }
    public bool UseInputedAddress { get; set; }
    public FileVersionInfo? VersionInfo { get; set; }
    public Version Version { get; set; } = new();
    public MemoryEditor Editor { get; set; } = new();
    public static ConcurrentDictionary<string, AddressResolverModel> VersionResolverDictionary { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool UsesTimedLyrics => !UseInputedAddress && Version.Major > 0 &&
                                   string.Equals(Version.ToString(3), CachedLyricsVersion, StringComparison.OrdinalIgnoreCase);

    public static void SetAddressResolver(AddressResolverModel resolver)
    {
        if (!string.IsNullOrWhiteSpace(resolver.Version))
            VersionResolverDictionary[resolver.Version] = resolver;
    }

    public bool Initialize()
    {
        if (GetCloudMusicLyricsProcess() is not Process process)
            return false;

        Console.WriteLine($"[DEBUG]已找到进程：{process.ProcessName}({process.Id}|{process.Id:X}) - {process.MainWindowTitle}");
        Editor.CurrentProcess = process;
        VersionInfo = FileVersionInfo.GetVersionInfo(process.MainModule?.FileName ?? string.Empty);
        Version = new Version(VersionInfo?.FileVersion ?? "0.0.0.0");
        try
        {
            Console.WriteLine($"[DEBUG]版本信息：{VersionInfo?.FileVersion}");
            Console.WriteLine($"[DEBUG]版本信息缩略：{Version.ToString(3)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR]版本输出异常：{ex.Message}");
            Console.WriteLine($"[TRACE]{ex.StackTrace}");
        }
        return ReresolveAddress();
    }

    public bool TryReadLyrics(out string lyrics)
    {
        lyrics = "无法读取歌词";
        try
        {
            if (Editor.ReadMemory(Address, 200, out var buffer))
            {
                lyrics = Encoding.Unicode.GetString(buffer, 0, GetValidLength(buffer));
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR]读取歌词异常：{ex.Message}");
            Console.WriteLine($"[TRACE]{ex.StackTrace}");
        }
        return false;
    }

    public bool TryReadLyrics(string? trackIdentity, out string lyrics)
    {
        if (!UsesTimedLyrics)
            return TryReadLyrics(out lyrics);

        var identity = trackIdentity ?? string.Empty;
        try
        {
            EnsureTimedLyricsLoading(identity);
            lyrics = BuildTrackFallback(identity);
            if (!TryReadPlaybackPosition(out var playbackPositionMilliseconds))
                return !string.IsNullOrWhiteSpace(lyrics);

            TimedLyricLine[] lines;
            lock (_timedLyricsLock)
                lines = _timedLyrics;
            if (lines.Length == 0)
                return !string.IsNullOrWhiteSpace(lyrics);

            TimedLyricLine? currentLine = null;
            foreach (var line in lines)
            {
                if (line.StartMilliseconds > playbackPositionMilliseconds)
                    break;
                currentLine = line;
            }

            if (currentLine is null)
                return !string.IsNullOrWhiteSpace(lyrics);

            lyrics = currentLine.Text;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR]读取网易云逐行歌词异常：{ex.Message}");
            Console.WriteLine($"[TRACE]{ex.StackTrace}");
            lyrics = BuildTrackFallback(identity);
            return !string.IsNullOrWhiteSpace(lyrics);
        }
    }

    public bool ReresolveAddress()
    {
        try
        {
            if (Version.Major == 0)
                return false;
            if (UseInputedAddress)
                return true;

            nint address = 0;
            if (UsesTimedLyrics)
            {
                address = Editor.ResolvePointerAddress(
                    "cloudmusic.dll",
                    checked((nint)PlayerStateBaseAddress),
                    PlayerStateOffsets);
            }
            else if (VersionResolverDictionary.TryGetValue(Version.ToString(3), out var resolver))
            {
                address = Editor.ResolvePointerAddress(
                    resolver.ModuleName,
                    checked((nint)resolver.BaseAddress),
                    resolver.Offsets.Select(static offset => checked((nint)offset)).ToArray());
            }
            else
            {
                Console.WriteLine($"[WARN]未找到匹配的版本解析器，当前版本：{Version}");
            }

            if (address != Address)
            {
                Console.WriteLine($"[DEBUG]读取到新的地址：{address}({address:X})");
                InvalidateTimedLyrics();
            }
            if (address == 0)
                return false;

            Address = address;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR]解析地址异常：{ex.Message}");
            Console.WriteLine($"[TRACE]{ex.StackTrace}");
            return false;
        }
    }

    private void EnsureTimedLyricsLoading(string trackIdentity)
    {
        if (string.IsNullOrWhiteSpace(trackIdentity))
            return;

        int generation;
        lock (_timedLyricsLock)
        {
            if (!string.Equals(_timedLyricsTrackIdentity, trackIdentity, StringComparison.Ordinal))
            {
                _timedLyricsTrackIdentity = trackIdentity;
                _timedLyrics = [];
                _lyricsLoadInProgress = false;
                _lyricsLoadCompleted = false;
                _nextLyricsLoadAttemptUtc = DateTime.MinValue;
                _lyricsLoadGeneration++;
            }

            if (_lyricsLoadCompleted || _lyricsLoadInProgress || DateTime.UtcNow < _nextLyricsLoadAttemptUtc)
                return;

            _lyricsLoadInProgress = true;
            generation = _lyricsLoadGeneration;
        }

        _ = Task.Run(async () =>
        {
            var (loaded, lines, source, songId) = await LoadTimedLyricsAsync(trackIdentity);
            lock (_timedLyricsLock)
            {
                if (generation != _lyricsLoadGeneration ||
                    !string.Equals(_timedLyricsTrackIdentity, trackIdentity, StringComparison.Ordinal))
                    return;

                _lyricsLoadInProgress = false;
                _lyricsLoadCompleted = loaded;
                if (loaded)
                {
                    _timedLyrics = lines;
                    Console.WriteLine($"[INFO]已从{source}加载网易云歌词：{songId}");
                }
                else
                {
                    _nextLyricsLoadAttemptUtc = DateTime.UtcNow.AddSeconds(2);
                }
            }
        });
    }

    private static async Task<(bool Loaded, TimedLyricLine[] Lines, string Source, string SongId)> LoadTimedLyricsAsync(string trackIdentity)
    {
        if (!TryResolveSongId(trackIdentity, out var songId))
            return (false, [], string.Empty, string.Empty);

        var cachePath = GetLyricsCachePath(songId);
        if (File.Exists(cachePath) && TryParseLyricsCache(cachePath, out var cachedLines))
            return (true, cachedLines, "本地缓存", songId);

        try
        {
            var requestUri = $"https://music.163.com/api/song/lyric?id={Uri.EscapeDataString(songId)}&lv=1&kv=1&tv=-1";
            using var response = await LyricsHttpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return (false, [], string.Empty, songId);

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (!TryParseLyricsDocument(document.RootElement, out var lines))
                return (false, [], string.Empty, songId);

            return (true, lines, "网易云接口", songId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN]获取网易云歌词失败：{ex.Message}");
            return (false, [], string.Empty, songId);
        }
    }

    private bool TryReadPlaybackPosition(out long playbackPositionMilliseconds)
    {
        playbackPositionMilliseconds = 0;
        var root = Address;
        if (root == 0 || !Editor.ReadMemory(root + PlaybackPositionOffset, sizeof(ulong), out var buffer) || buffer.Length < sizeof(ulong))
            return false;

        var value = BitConverter.ToUInt64(buffer, 0);
        if (value > MaximumPlaybackPositionMilliseconds)
            return false;

        playbackPositionMilliseconds = checked((long)value);
        return true;
    }

    private static bool TryResolveSongId(string trackIdentity, out string songId)
    {
        songId = string.Empty;
        var parts = trackIdentity.Split('\n', 2);
        var title = NormalizeForMatch(parts.ElementAtOrDefault(0));
        var artist = NormalizeArtistForMatch(parts.ElementAtOrDefault(1));
        if (title.Length == 0)
            return false;

        var playingListPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetEase",
            "CloudMusic",
            "webdata",
            "file",
            "playingList");
        if (!File.Exists(playingListPath))
            return false;

        try
        {
            using var stream = new FileStream(
                playingListPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
                return false;

            var bestScore = -1;
            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("track", out var track) ||
                    !track.TryGetProperty("name", out var nameElement) ||
                    nameElement.ValueKind != JsonValueKind.String)
                    continue;

                var score = NormalizeForMatch(nameElement.GetString()) == title ? 100 : -1;
                if (score < 0 && track.TryGetProperty("transNames", out var translatedNames) && translatedNames.ValueKind == JsonValueKind.Array)
                {
                    foreach (var translatedName in translatedNames.EnumerateArray())
                    {
                        if (translatedName.ValueKind == JsonValueKind.String && NormalizeForMatch(translatedName.GetString()) == title)
                        {
                            score = 80;
                            break;
                        }
                    }
                }
                if (score < 0)
                    continue;

                if (artist.Length > 0 && track.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                {
                    var artistNames = artists.EnumerateArray()
                        .Select(static value => value.TryGetProperty("name", out var name) ? NormalizeArtistForMatch(name.GetString()) : string.Empty)
                        .Where(static value => value.Length > 0)
                        .ToArray();
                    if (artistNames.Any(value => value == artist))
                        score += 30;
                    else
                    {
                        var combined = string.Concat(artistNames);
                        if (combined.Contains(artist, StringComparison.Ordinal) || artist.Contains(combined, StringComparison.Ordinal))
                            score += 15;
                    }
                }

                if (score <= bestScore)
                    continue;
                if (!track.TryGetProperty("id", out var idElement))
                    continue;

                var id = idElement.ValueKind switch
                {
                    JsonValueKind.String => idElement.GetString(),
                    JsonValueKind.Number => idElement.GetInt64().ToString(CultureInfo.InvariantCulture),
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                bestScore = score;
                songId = id;
            }

            return songId.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return string.Concat(value.Normalize(NormalizationForm.FormKC).Where(static character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
    }

    private static string NormalizeArtistForMatch(string? value) =>
        string.Concat(NormalizeForMatch(value).Where(static character => char.IsLetterOrDigit(character)));

    private static string GetLyricsCachePath(string songId)
    {
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(songId))).ToLowerInvariant();
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetEase",
            "CloudMusic",
            "Temp",
            hash);
    }

    private static bool TryParseLyricsCache(string path, out TimedLyricLine[] lines)
    {
        lines = [];
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return TryParseLyricsDocument(document.RootElement, out lines);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseLyricsDocument(JsonElement root, out TimedLyricLine[] lines)
    {
        lines = [];
        if (!root.TryGetProperty("code", out var code) || code.GetInt32() != 200)
            return false;

        if ((root.TryGetProperty("noLyric", out var noLyric) || root.TryGetProperty("nolyric", out noLyric)) &&
            noLyric.ValueKind == JsonValueKind.True)
            return true;
        if (!root.TryGetProperty("lrc", out var lrc) ||
            !lrc.TryGetProperty("lyric", out var lyric) ||
            lyric.ValueKind != JsonValueKind.String)
            return true;

        lines = ParseStandardLyrics(lyric.GetString() ?? string.Empty);
        return true;
    }

    private static TimedLyricLine[] ParseStandardLyrics(string lyrics)
    {
        var sourceLines = lyrics.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var offsetMilliseconds = 0L;
        foreach (var rawLine in sourceLines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']') &&
                long.TryParse(line[8..^1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedOffset))
            {
                offsetMilliseconds = parsedOffset;
                break;
            }
        }

        var result = new List<TimedLyricLine>();
        foreach (var rawLine in sourceLines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line[0] == '{' && TryParseJsonLyricLine(line, offsetMilliseconds, out var jsonLine))
            {
                if (jsonLine.Text.Length > 0)
                    result.Add(jsonLine);
                continue;
            }

            var timestamps = new List<long>(1);
            var cursor = 0;
            while (cursor < line.Length && line[cursor] == '[')
            {
                var closeBracket = line.IndexOf(']', cursor + 1);
                if (closeBracket < 0)
                    break;
                if (TryParseLrcTimestamp(line[(cursor + 1)..closeBracket], out var timestamp))
                    timestamps.Add(Math.Max(0, timestamp + offsetMilliseconds));
                cursor = closeBracket + 1;
            }

            var text = line[cursor..].Trim().TrimStart('\uFEFF');
            if (timestamps.Count == 0 || text.Length == 0)
                continue;
            foreach (var timestamp in timestamps)
                result.Add(new TimedLyricLine(timestamp, text));
        }

        return [.. result.OrderBy(static line => line.StartMilliseconds)];
    }

    private static bool TryParseJsonLyricLine(string line, long offsetMilliseconds, out TimedLyricLine lyricLine)
    {
        lyricLine = new TimedLyricLine(0, string.Empty);
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("t", out var time) || !root.TryGetProperty("c", out var content) ||
                time.ValueKind != JsonValueKind.Number || content.ValueKind != JsonValueKind.Array)
                return false;

            var text = new StringBuilder();
            foreach (var segment in content.EnumerateArray())
            {
                if (segment.TryGetProperty("tx", out var value) && value.ValueKind == JsonValueKind.String)
                    text.Append(value.GetString());
            }

            lyricLine = new TimedLyricLine(Math.Max(0, time.GetInt64() + offsetMilliseconds), text.ToString().Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseLrcTimestamp(string value, out long milliseconds)
    {
        milliseconds = 0;
        var colon = value.IndexOf(':');
        if (colon <= 0 ||
            !int.TryParse(value[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !double.TryParse(value[(colon + 1)..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) ||
            minutes < 0 || seconds < 0 || seconds >= 60)
            return false;

        milliseconds = checked(minutes * 60_000L + (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero));
        return true;
    }

    private static string BuildTrackFallback(string trackIdentity)
    {
        var parts = trackIdentity.Split('\n', 2);
        var title = parts.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
        var artist = parts.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
        if (title.Length == 0)
            return string.Empty;
        return artist.Length == 0 ? title : $"{title} - {artist}";
    }

    private void InvalidateTimedLyrics()
    {
        lock (_timedLyricsLock)
        {
            _timedLyrics = [];
            _timedLyricsTrackIdentity = string.Empty;
            _lyricsLoadInProgress = false;
            _lyricsLoadCompleted = false;
            _nextLyricsLoadAttemptUtc = DateTime.MinValue;
            _lyricsLoadGeneration++;
        }
    }

    private static HttpClient CreateLyricsHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 HaloPixelToolBox/1.2.7");
        return client;
    }

    public static int GetValidLength(byte[] buffer)
    {
        var length = 0;
        for (var i = 0; i < buffer.Length - 1; i += 2)
        {
            if (buffer[i] == 0 && buffer[i + 1] == 0)
                break;
            length += 2;
        }
        return length;
    }

    public static Process? GetCloudMusicLyricsProcess()
    {
        foreach (var process in Process.GetProcesses())
        {
            if (process.ProcessName == "cloudmusic" &&
                (process.MainWindowTitle == "桌面歌词" || !process.MainWindowTitle.IsNullOrWhiteSpace()))
                return process;
        }
        return null;
    }

    private sealed record TimedLyricLine(long StartMilliseconds, string Text);
}
