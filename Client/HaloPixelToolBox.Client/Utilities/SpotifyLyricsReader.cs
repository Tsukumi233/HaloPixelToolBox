using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Client.Utilities;

public class LyricLine
{
    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;
}

public static class LrcParser
{
    private static readonly Regex LrcTimeRegex = new Regex(@"\[(\d+):(\d+)(?:[:\.](\d+))?\]", RegexOptions.Compiled);
    private static readonly Regex CleanTimecodeRegex = new Regex(@"\[\d+:\d+(?:[:\.]\d+)?\]", RegexOptions.Compiled);

    public static List<LyricLine> Parse(string lrcContent)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrcContent))
            return lines;

        var rawLines = lrcContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in rawLines)
        {
            var match = LrcTimeRegex.Match(line);
            if (match.Success)
            {
                var minutes = int.Parse(match.Groups[1].Value);
                var seconds = int.Parse(match.Groups[2].Value);
                var fractionStr = match.Groups[3].Value;
                var milliseconds = 0;
                if (!string.IsNullOrEmpty(fractionStr))
                {
                    if (fractionStr.Length == 2)
                        milliseconds = int.Parse(fractionStr) * 10;
                    else if (fractionStr.Length == 3)
                        milliseconds = int.Parse(fractionStr);
                    else
                        milliseconds = int.Parse(fractionStr.Substring(0, 3));
                }

                var timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                var text = line.Substring(match.Length).Trim();

                // Strip any remaining timecodes from the lyric text (e.g. "[00:00.00]" or "[00:00:000]")
                text = CleanTimecodeRegex.Replace(text, "").Trim();

                if (!string.IsNullOrEmpty(text))
                {
                    lines.Add(new LyricLine { Timestamp = timestamp, Text = text });
                }
            }
        }
        lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return lines;
    }
}

public class LrclibResponse
{
    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; set; }

    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; set; }

    [JsonPropertyName("trackName")]
    public string? TrackName { get; set; }

    [JsonPropertyName("artistName")]
    public string? ArtistName { get; set; }
}

public class SpotifyLyricsReader
{
    private static HttpClient? _httpClient;
    private static readonly object HttpLock = new object();

    private static HttpClient HttpClient
    {
        get
        {
            if (_httpClient == null)
            {
                lock (HttpLock)
                {
                    if (_httpClient == null)
                    {
                        _httpClient = CreateHttpClient();
                    }
                }
            }
            return _httpClient;
        }
    }

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private bool _isInitialized = false;
    private CancellationTokenSource? _cts;

    private string _currentTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private double _currentDuration = 0;
    private List<LyricLine> _lyricLines = new();

    private DateTimeOffset _songStartTime = DateTimeOffset.Now;
    private bool _isSmtcSynced = false;
    private string _lastColorTitle = string.Empty;

    public (byte R, byte G, byte B)? CurrentAlbumColor { get; private set; }

    public bool IsPlaying
    {
        get
        {
            if (_currentSession != null)
            {
                try
                {
                    var playback = _currentSession.GetPlaybackInfo();
                    return playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                }
                catch { }
            }
            return false;
        }
    }

    public static async Task<(byte R, byte G, byte B)?> GetDominantColorAsync(Windows.Storage.Streams.IRandomAccessStreamReference thumbnail)
    {
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var transform = new Windows.Graphics.Imaging.BitmapTransform
            {
                ScaledWidth = 16,
                ScaledHeight = 16,
                InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Linear
            };
            var pixelData = await decoder.GetPixelDataAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Rgba8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
                transform,
                Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                Windows.Graphics.Imaging.ColorManagementMode.ColorManageToSRgb
            );
            byte[] pixels = pixelData.DetachPixelData();
            long rSum = 0, gSum = 0, bSum = 0;
            int pixelCount = pixels.Length / 4;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                rSum += pixels[i];
                gSum += pixels[i + 1];
                bSum += pixels[i + 2];
            }
            if (pixelCount > 0)
            {
                return ((byte)(rSum / pixelCount), (byte)(gSum / pixelCount), (byte)(bSum / pixelCount));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetDominantColorAsync failed: {ex.Message}\n{ex.StackTrace}");
        }
        return null;
    }

    public string CurrentTitle => _currentTitle;
    public string CurrentArtist => _currentArtist;

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            // Ignore certificate errors to bypass any MITM issues caused by local proxy clients decrypting HTTPS
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };

        // List of common local proxy ports in China:
        // 7890: Clash / Mihomo
        // 10809: v2rayN / Xray
        // 10808: Shadowsocks / SSR
        // 7898: Clash alternative
        int[] commonPorts = { 7890, 10809, 10808, 7898 };
        bool proxyConfigured = false;

        foreach (var port in commonPorts)
        {
            if (IsPortOpen(port))
            {
                handler.Proxy = new System.Net.WebProxy($"http://127.0.0.1:{port}");
                handler.UseProxy = true;
                proxyConfigured = true;
                break;
            }
        }

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        if (proxyConfigured)
        {
            Console.WriteLine("Auto-configured HttpClient to use local proxy for accelerated connection.");
        }
        else
        {
            Console.WriteLine("No local proxy detected, using direct system default connection.");
        }

        return client;
    }

    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
            if (connectTask.Wait(250)) // Wait up to 250ms to ensure JIT doesn't miss the handshake
            {
                return client.Connected;
            }
        }
        catch { }
        return false;
    }

    private static async Task<T?> RunWithTimeout<T>(Windows.Foundation.IAsyncOperation<T> asyncOp, int timeoutMs)
    {
        using var cts = new CancellationTokenSource();
        try
        {
            var task = asyncOp.AsTask(cts.Token);
            var delayTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(task, delayTask);
            if (completedTask == task)
            {
                return await task;
            }
            else
            {
                cts.Cancel(); // Cancel the WinRT operation
                Console.WriteLine("WinRT async operation timed out.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RunWithTimeout failed: {ex.Message}");
        }
        return default;
    }

    public bool Initialize()
    {
        Console.WriteLine($"Initialize called. isInitialized={_isInitialized}");
        if (_isInitialized)
        {
            UpdateCurrentSession();
            return _currentSession != null || Process.GetProcessesByName("Spotify").Length > 0;
        }

        try
        {
            var task = Task.Run(async () =>
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.SessionsChanged += OnSessionsChanged;
                _isInitialized = true;
                UpdateCurrentSession();
                StartWindowTitlePolling();
                return _currentSession != null || Process.GetProcessesByName("Spotify").Length > 0;
            });
            return task.Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SpotifyLyricsReader Initialize failed: {ex.Message}");
            return false;
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        Console.WriteLine("OnSessionsChanged event fired.");
        UpdateCurrentSession();
    }

    private void UpdateCurrentSession()
    {
        if (_manager == null) return;

        var sessions = _manager.GetSessions();
        GlobalSystemMediaTransportControlsSession? spotifySession = null;
        foreach (var s in sessions)
        {
            if (s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
            {
                spotifySession = s;
                break;
            }
        }

        if (spotifySession != _currentSession)
        {
            Console.WriteLine($"Active session changed. Old: {_currentSession?.SourceAppUserModelId ?? "null"}, New: {spotifySession?.SourceAppUserModelId ?? "null"}");
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            _currentSession = spotifySession;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                _ = SyncWithSmtcAsync();
            }
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        Console.WriteLine("OnMediaPropertiesChanged event fired.");
        _ = SyncWithSmtcAsync();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        Console.WriteLine("OnPlaybackInfoChanged event fired.");
        _ = SyncWithSmtcAsync();
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        Console.WriteLine("OnTimelinePropertiesChanged event fired.");
        _ = SyncWithSmtcAsync();
    }

    private async Task SyncWithSmtcAsync()
    {
        if (_currentSession == null) return;

        try
        {
            var media = await RunWithTimeout(_currentSession.TryGetMediaPropertiesAsync(), 500);
            if (media != null && !string.IsNullOrEmpty(media.Title))
            {
                // Check if SMTC matches our window-title song
                if (media.Title.Contains(_currentTitle, StringComparison.OrdinalIgnoreCase) ||
                    _currentTitle.Contains(media.Title, StringComparison.OrdinalIgnoreCase))
                {
                    var timeline = _currentSession.GetTimelineProperties();
                    _currentDuration = timeline?.EndTime.TotalSeconds ?? 0;
                    _isSmtcSynced = true;
                    Console.WriteLine($"SMTC Sync Achieved for: {_currentArtist} - {_currentTitle} (Duration: {_currentDuration}s)");

                     if (media.Thumbnail != null)
                     {
                         if (_lastColorTitle != media.Title)
                         {
                             _lastColorTitle = media.Title;
                             var color = await GetDominantColorAsync(media.Thumbnail);
                             if (color.HasValue)
                             {
                                 CurrentAlbumColor = color.Value;
                                 Console.WriteLine($"Extracted album dominant color: RGB({color.Value.R}, {color.Value.G}, {color.Value.B})");
                             }
                             else
                             {
                                 Console.WriteLine("GetDominantColorAsync returned null for the thumbnail.");
                             }
                         }
                     }
                     else
                     {
                         Console.WriteLine("media.Thumbnail is null for this track.");
                     }
                }
                else
                {
                    Console.WriteLine($"SMTC song mismatch. SMTC Title: {media.Title}, Current Title: {_currentTitle}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SyncWithSmtcAsync failed: {ex.Message}");
        }
    }

    private void StartWindowTitlePolling()
    {
        Console.WriteLine("Starting window title polling loop.");
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var trackInfo = await GetTrackInfoAsync();

                    if (!string.IsNullOrEmpty(trackInfo.Title))
                    {
                        if (trackInfo.Artist != _currentArtist || trackInfo.Title != _currentTitle)
                        {
                            Console.WriteLine($"Track change detected via polling: {trackInfo.Artist} - {trackInfo.Title}");
                            _ = HandleTrackChangeAsync(trackInfo.Artist, trackInfo.Title);
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(_currentTitle))
                        {
                            Console.WriteLine("No active track detected, clearing lyrics.");
                            _currentTitle = string.Empty;
                            _currentArtist = string.Empty;
                            _lyricLines.Clear();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Track polling loop error: {ex.Message}");
                }
                await Task.Delay(250);
            }
        });
    }

    private static string GetSpotifyWindowTitle()
    {
        var processes = Process.GetProcessesByName("Spotify");
        foreach (var p in processes)
        {
            try
            {
                if (!string.IsNullOrEmpty(p.MainWindowTitle))
                {
                    return p.MainWindowTitle;
                }
            }
            catch { /* Ignore process access errors */ }
        }
        return string.Empty;
    }

    private async Task<(string Artist, string Title)> GetTrackInfoAsync()
    {
        string artist = string.Empty;
        string title = string.Empty;

        // 1. Try Window Title for instant song metadata
        string winTitle = GetSpotifyWindowTitle();
        if (!string.IsNullOrEmpty(winTitle) && winTitle != "Spotify" && winTitle != "Spotify Premium" && winTitle != "Spotify Free")
        {
            int dashIndex = winTitle.IndexOf(" - ");
            if (dashIndex > 0)
            {
                artist = winTitle.Substring(0, dashIndex).Trim();
                title = winTitle.Substring(dashIndex + 3).Trim();
            }
        }

        // 2. Fallback to SMTC metadata if window title is empty (e.g. minimized to tray)
        if (string.IsNullOrEmpty(title) && _currentSession != null)
        {
            try
            {
                var media = await RunWithTimeout(_currentSession.TryGetMediaPropertiesAsync(), 500);
                if (media != null && !string.IsNullOrEmpty(media.Title))
                {
                    artist = media.Artist;
                    title = media.Title;
                }
            }
            catch { }
        }

        return (artist, title);
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleanName = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return cleanName;
    }

    private async Task HandleTrackChangeAsync(string artist, string track)
    {
        Console.WriteLine($"HandleTrackChangeAsync started. Target: {artist} - {track}");
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            _currentTitle = track;
            _currentArtist = artist;
            _songStartTime = DateTimeOffset.Now;
            _currentDuration = 0;
            _isSmtcSynced = false;
            _lyricLines.Clear();

            // Await SMTC synchronization to get the duration before requesting lyrics
            await SyncWithSmtcAsync();

            Console.WriteLine($"Fetching lyrics for: {_currentArtist} - {_currentTitle} (Duration: {_currentDuration}s)");

            // 1. Check local cache first
            string lyricsCacheDir = Path.Combine(AppPathHelper.AppCache, "Lyrics");
            if (!Directory.Exists(lyricsCacheDir))
            {
                Directory.CreateDirectory(lyricsCacheDir);
            }
            string cacheFilePath = Path.Combine(lyricsCacheDir, $"{SanitizeFileName(_currentArtist)} - {SanitizeFileName(_currentTitle)}.lrc");

            if (File.Exists(cacheFilePath))
            {
                try
                {
                    string cachedLrc = await File.ReadAllTextAsync(cacheFilePath, token);
                    if (!string.IsNullOrEmpty(cachedLrc))
                    {
                        _lyricLines = LrcParser.Parse(cachedLrc);
                        Console.WriteLine($"Loaded lyrics from local cache file: '{cacheFilePath}'");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to read cached lyrics file: {ex.Message}");
                }
            }

            // 2. Local cache miss: query LRCLIB database (with local proxy acceleration enabled)
            var lyricsRes = await FetchLyricsAsync(_currentTitle, _currentArtist, _currentDuration, token);

            if (token.IsCancellationRequested)
            {
                Console.WriteLine($"Lyrics fetch cancelled for: {artist} - {track}");
                return;
            }

            if (lyricsRes != null && !string.IsNullOrEmpty(lyricsRes.SyncedLyrics))
            {
                _lyricLines = LrcParser.Parse(lyricsRes.SyncedLyrics);
                Console.WriteLine($"Loaded {_lyricLines.Count} synced lines from LRCLIB.");

                // Save to local cache asynchronously
                try
                {
                    await File.WriteAllTextAsync(cacheFilePath, lyricsRes.SyncedLyrics, token);
                    Console.WriteLine($"Saved fetched lyrics to local cache: '{cacheFilePath}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save lyrics to local cache: {ex.Message}");
                }
            }
            else if (lyricsRes != null && !string.IsNullOrEmpty(lyricsRes.PlainLyrics))
            {
                Console.WriteLine("Synced lyrics not found, plain lyrics available.");
            }
            else
            {
                Console.WriteLine("No lyrics found.");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Fetch cancelled (OperationCanceledException).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HandleTrackChangeAsync failed: {ex.Message}");
        }
    }

    private static HashSet<string> GetSignificantWords(string input)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(input)) return words;

        string cleaned = Regex.Replace(input, @"[^\w\s]", " ");
        var rawWords = cleaned.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "with", "by",
            "feat", "ft", "version", "mix", "remix", "radio", "edit", "acoustic", "live", "original",
            "sped", "up", "slowed", "down"
        };

        foreach (var w in rawWords)
        {
            if (w.Length > 1 && !stopWords.Contains(w))
            {
                words.Add(w);
            }
        }
        return words;
    }

    private bool IsLooseMatch(string returnedTitle, string returnedArtist, string targetTitle, string targetArtist)
    {
        var targetTitleWords = GetSignificantWords(targetTitle);
        var returnedTitleWords = GetSignificantWords(returnedTitle);

        bool titleMatches = false;
        foreach (var w in targetTitleWords)
        {
            if (returnedTitleWords.Contains(w))
            {
                titleMatches = true;
                break;
            }
        }

        if (targetTitleWords.Count == 0 || !titleMatches)
        {
            titleMatches = returnedTitle.Contains(targetTitle, StringComparison.OrdinalIgnoreCase) ||
                           targetTitle.Contains(returnedTitle, StringComparison.OrdinalIgnoreCase);
        }

        var targetArtistWords = GetSignificantWords(targetArtist);
        var returnedArtistWords = GetSignificantWords(returnedArtist);

        bool artistMatches = false;
        foreach (var w in targetArtistWords)
        {
            if (returnedArtistWords.Contains(w))
            {
                artistMatches = true;
                break;
            }
        }

        if (targetArtistWords.Count == 0 || !artistMatches)
        {
            artistMatches = returnedArtist.Contains(targetArtist, StringComparison.OrdinalIgnoreCase) ||
                            targetArtist.Contains(returnedArtist, StringComparison.OrdinalIgnoreCase);
        }

        return titleMatches && artistMatches;
    }

    private bool IsLrcLibMatch(LrclibResponse result, string targetArtist, string targetTitle)
    {
        if (result == null) return false;
        string trackName = result.TrackName ?? string.Empty;
        string artistName = result.ArtistName ?? string.Empty;

        bool match = IsLooseMatch(trackName, artistName, targetTitle, targetArtist);
        Console.WriteLine($"IsLrcLibMatch: Returned Title: '{trackName}', Artist: '{artistName}' vs Target Title: '{targetTitle}', Artist: '{targetArtist}' => MATCH={match}");
        return match;
    }

    private async Task<LrclibResponse?> FetchLyricsAsync(string title, string artist, double duration, CancellationToken token)
    {
        // LRCLIB is the fallback open-source database.
        // We query each endpoint with a separate 15-second timeout to handle high-latency connections robustly.
        try
        {
            // 1. Try real-time get API first if duration is available
            if (duration > 0)
            {
                using var getCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                getCts.CancelAfter(TimeSpan.FromSeconds(15));

                int durationSeconds = (int)Math.Round(duration);
                string getUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}&duration={durationSeconds}";
                Console.WriteLine($"LRCLIB /api/get lookup URL: '{getUrl}'");

                try
                {
                    var getResponse = await HttpClient.GetAsync(getUrl, getCts.Token);
                    if (getResponse.IsSuccessStatusCode)
                    {
                        var getRes = await getResponse.Content.ReadFromJsonAsync<LrclibResponse>(cancellationToken: getCts.Token);
                        if (getRes != null && (!string.IsNullOrEmpty(getRes.SyncedLyrics) || !string.IsNullOrEmpty(getRes.PlainLyrics)))
                        {
                            Console.WriteLine("LRCLIB /api/get lyrics retrieved successfully.");
                            return getRes;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"LRCLIB /api/get returned status code: {getResponse.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LRCLIB /api/get failed: {ex.Message}");
                }
            }

            if (token.IsCancellationRequested) return null;

            // 2. Try get-cached (precise cache lookup)
            {
                using var cachedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cachedCts.CancelAfter(TimeSpan.FromSeconds(15));

                string cachedUrl = $"https://lrclib.net/api/get-cached?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
                Console.WriteLine($"LRCLIB cached lookup URL: '{cachedUrl}'");

                try
                {
                    var response = await HttpClient.GetAsync(cachedUrl, cachedCts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var cachedRes = await response.Content.ReadFromJsonAsync<LrclibResponse>(cancellationToken: cachedCts.Token);
                        if (cachedRes != null && (!string.IsNullOrEmpty(cachedRes.SyncedLyrics) || !string.IsNullOrEmpty(cachedRes.PlainLyrics)))
                        {
                            Console.WriteLine("LRCLIB cached lyrics retrieved successfully.");
                            return cachedRes;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"LRCLIB cached API returned status code: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LRCLIB cached API failed: {ex.Message}");
                }
            }

            if (token.IsCancellationRequested) return null;

            // 3. Try precise search endpoint (exact metadata match)
            {
                using var searchCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                searchCts.CancelAfter(TimeSpan.FromSeconds(15));

                string searchUrl = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
                Console.WriteLine($"LRCLIB precise search URL: '{searchUrl}'");

                try
                {
                    var searchResponse = await HttpClient.GetAsync(searchUrl, searchCts.Token);
                    if (searchResponse.IsSuccessStatusCode)
                    {
                        var searchResults = await searchResponse.Content.ReadFromJsonAsync<List<LrclibResponse>>(cancellationToken: searchCts.Token);
                        if (searchResults != null && searchResults.Count > 0)
                        {
                            Console.WriteLine($"LRCLIB precise search returned {searchResults.Count} results.");
                            foreach (var result in searchResults)
                            {
                                if (!string.IsNullOrEmpty(result.SyncedLyrics) && IsLrcLibMatch(result, artist, title))
                                {
                                    Console.WriteLine("LRCLIB matched precise search result.");
                                    return result;
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"LRCLIB precise search returned status code: {searchResponse.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LRCLIB precise search failed: {ex.Message}");
                }
            }

            if (token.IsCancellationRequested) return null;

            // 4. Fallback to cleaned metadata search
            string cleanTitle = Regex.Replace(title, @"\s*[\(\[][^\)\]]*[\)\]]", "");
            cleanTitle = Regex.Replace(cleanTitle, @"\s*-\s*.*?(?:radio|edit|remaster|remix|mix|acoustic|live|version|sped|slowed|instrumental).*", "", RegexOptions.IgnoreCase);
            cleanTitle = Regex.Replace(cleanTitle, @"\s+(?:feat|ft)\.?\s+.*", "", RegexOptions.IgnoreCase);

            string cleanArtist = Regex.Replace(artist, @"\s+(?:feat|ft)\.?\s+.*", "", RegexOptions.IgnoreCase);
            int commaIndex = cleanArtist.IndexOfAny(new[] { ',', ';', '/' });
            if (commaIndex > 0)
            {
                cleanArtist = cleanArtist.Substring(0, commaIndex).Trim();
            }

            cleanTitle = cleanTitle.Trim();
            cleanArtist = cleanArtist.Trim();

            if (cleanTitle != title || cleanArtist != artist)
            {
                using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                fallbackCts.CancelAfter(TimeSpan.FromSeconds(15));

                string fallbackUrl = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
                Console.WriteLine($"LRCLIB clean fallback search URL: '{fallbackUrl}'");

                try
                {
                    var fallbackResponse = await HttpClient.GetAsync(fallbackUrl, fallbackCts.Token);
                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        var fallbackResults = await fallbackResponse.Content.ReadFromJsonAsync<List<LrclibResponse>>(cancellationToken: fallbackCts.Token);
                        if (fallbackResults != null && fallbackResults.Count > 0)
                        {
                            Console.WriteLine($"LRCLIB fallback search returned {fallbackResults.Count} results.");
                            foreach (var result in fallbackResults)
                            {
                                if (!string.IsNullOrEmpty(result.SyncedLyrics) && IsLrcLibMatch(result, artist, title))
                                {
                                    Console.WriteLine("LRCLIB matched clean fallback search result.");
                                    return result;
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"LRCLIB fallback search returned status code: {fallbackResponse.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LRCLIB fallback search failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LRCLIB lyric fetch outer error: {ex.Message}");
        }

        return null;
    }

    public bool TryReadLyrics(out string lyrics)
    {
        lyrics = string.Empty;

        if (_lyricLines == null || _lyricLines.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(_currentTitle))
            {
                lyrics = $"{_currentArtist} - {_currentTitle}";
                return true;
            }
            lyrics = "无歌词信息";
            return false;
        }

        TimeSpan currentPosition = TimeSpan.Zero;
        bool positionDetermined = false;

        // Try using SMTC for progress if synced
        if (_isSmtcSynced && _currentSession != null)
        {
            try
            {
                var playback = _currentSession.GetPlaybackInfo();
                var timeline = _currentSession.GetTimelineProperties();
                if (timeline != null && playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    var elapsed = DateTimeOffset.Now - timeline.LastUpdatedTime;
                    if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
                    currentPosition = timeline.Position + elapsed;
                    positionDetermined = true;
                }
                else if (timeline != null && playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                {
                    currentPosition = timeline.Position;
                    positionDetermined = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TryReadLyrics SMTC position error: {ex.Message}");
            }
        }

        // Fallback to local timer if SMTC not synced or failed
        if (!positionDetermined)
        {
            var elapsed = DateTimeOffset.Now - _songStartTime;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            currentPosition = elapsed;
        }

        string currentText = string.Empty;
        foreach (var line in _lyricLines)
        {
            if (line.Timestamp <= currentPosition)
            {
                currentText = line.Text;
            }
            else
            {
                break;
            }
        }

        if (string.IsNullOrEmpty(currentText))
        {
            lyrics = $"{_currentArtist} - {_currentTitle}";
        }
        else
        {
            lyrics = currentText;
        }

        return true;
    }
}
