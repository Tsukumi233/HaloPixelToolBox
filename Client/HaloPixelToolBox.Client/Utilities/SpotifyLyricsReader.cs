using HaloPixelToolBox.Core.Utilities;
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

public class SpotifyLyricsReader : IAsyncDisposable
{
    private const int DominantColorSampleSize = 24;
    private const int DominantColorClusterCount = 5;
    private const int DominantColorIterationCount = 6;

    private readonly record struct OklabSample(double L, double A, double B, double Weight);

    private static readonly double[] SrgbToLinearLookup = CreateSrgbToLinearLookup();
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
    private readonly BackgroundTaskScope _background = new();
    private Task? _disposeTask;
    private readonly object _trackGate = new();
    private readonly object _sessionGate = new();

    public ValueTask DisposeAsync() => new(_disposeTask ??= StopCoreAsync());

    private async Task StopCoreAsync()
    {
        await _background.StopAsync().ConfigureAwait(false);
        if (_manager is not null)
            _manager.SessionsChanged -= OnSessionsChanged;
        if (_currentSession is not null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        _currentSession = null;
        _manager = null;
    }

    private static bool IsSpotifyRunning()
    {
        var processes = Process.GetProcessesByName("Spotify");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }


    private string _currentTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private double _currentDuration = 0;
    private List<LyricLine> _lyricLines = new();

    private DateTimeOffset _songStartTime = DateTimeOffset.Now;
    private bool _isSmtcSynced = false;
    private string _lastColorTitle = string.Empty;

    public (byte R, byte G, byte B)? CurrentAlbumColor { get; private set; }

    public GlobalSystemMediaTransportControlsSessionPlaybackStatus? PlaybackStatus
    {
        get
        {
            if (_currentSession != null)
            {
                try
                {
                    var playback = _currentSession.GetPlaybackInfo();
                    return playback.PlaybackStatus;
                }
                catch { }
            }
            return null;
        }
    }

    public bool IsPlaying => PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    public static async Task<(byte R, byte G, byte B)?> GetDominantColorAsync(Windows.Storage.Streams.IRandomAccessStreamReference thumbnail, CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = await thumbnail.OpenReadAsync().AsTask(cancellationToken);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
            var transform = new Windows.Graphics.Imaging.BitmapTransform
            {
                ScaledWidth = DominantColorSampleSize,
                ScaledHeight = DominantColorSampleSize,
                InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Linear
            };
            var pixelData = await decoder.GetPixelDataAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Rgba8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
                transform,
                Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                Windows.Graphics.Imaging.ColorManagementMode.ColorManageToSRgb
            ).AsTask(cancellationToken);
            byte[] pixels = pixelData.DetachPixelData();
            var samples = new List<OklabSample>(pixels.Length / 4);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                var alpha = pixels[i + 3] / 255d;
                if (alpha < 0.05)
                    continue;

                var pixelIndex = i / 4;
                var x = pixelIndex % DominantColorSampleSize;
                var y = pixelIndex / DominantColorSampleSize;
                var normalizedX = ((x + 0.5) / DominantColorSampleSize * 2) - 1;
                var normalizedY = ((y + 0.5) / DominantColorSampleSize * 2) - 1;
                var centerDistance = Math.Min(
                    1,
                    Math.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY) * 0.7071067811865476);
                var spatialWeight = (1 + 0.2 * (1 - centerDistance)) * alpha;

                samples.Add(ToOklab(pixels[i], pixels[i + 1], pixels[i + 2], spatialWeight));
            }

            if (samples.Count > 0)
                return FindPerceptualDominantColor(samples);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetDominantColorAsync failed: {ex.Message}\n{ex.StackTrace}");
        }
        return null;
    }

    private static (byte R, byte G, byte B) FindPerceptualDominantColor(IReadOnlyList<OklabSample> samples)
    {
        var maximumClusterCount = Math.Min(DominantColorClusterCount, samples.Count);
        var centerL = new double[maximumClusterCount];
        var centerA = new double[maximumClusterCount];
        var centerB = new double[maximumClusterCount];

        var totalWeight = 0d;
        var meanL = 0d;
        var meanA = 0d;
        var meanB = 0d;
        foreach (var sample in samples)
        {
            totalWeight += sample.Weight;
            meanL += sample.L * sample.Weight;
            meanA += sample.A * sample.Weight;
            meanB += sample.B * sample.Weight;
        }
        meanL /= totalWeight;
        meanA /= totalWeight;
        meanB /= totalWeight;

        var firstSeed = samples[0];
        var firstSeedDistance = double.MaxValue;
        foreach (var sample in samples)
        {
            var distance = OklabDistanceSquared(sample.L, sample.A, sample.B, meanL, meanA, meanB);
            if (distance < firstSeedDistance)
            {
                firstSeedDistance = distance;
                firstSeed = sample;
            }
        }
        centerL[0] = firstSeed.L;
        centerA[0] = firstSeed.A;
        centerB[0] = firstSeed.B;

        // Deterministic farthest-point seeding gives k-means a useful spread across
        // the cover palette without allocating a random generator or histogram.
        var clusterCount = 1;
        while (clusterCount < maximumClusterCount)
        {
            var bestSampleIndex = -1;
            var bestSeedScore = 0d;
            for (var sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
            {
                var sample = samples[sampleIndex];
                var minimumDistance = double.MaxValue;
                for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
                {
                    var distance = OklabDistanceSquared(
                        sample.L,
                        sample.A,
                        sample.B,
                        centerL[clusterIndex],
                        centerA[clusterIndex],
                        centerB[clusterIndex]);
                    minimumDistance = Math.Min(minimumDistance, distance);
                }

                var seedScore = minimumDistance * sample.Weight;
                if (seedScore > bestSeedScore)
                {
                    bestSeedScore = seedScore;
                    bestSampleIndex = sampleIndex;
                }
            }

            if (bestSampleIndex < 0 || bestSeedScore < 1e-10)
                break;

            var seed = samples[bestSampleIndex];
            centerL[clusterCount] = seed.L;
            centerA[clusterCount] = seed.A;
            centerB[clusterCount] = seed.B;
            clusterCount++;
        }

        var clusterWeights = new double[clusterCount];
        for (var iteration = 0; iteration < DominantColorIterationCount; iteration++)
        {
            Array.Clear(clusterWeights);
            var sumL = new double[clusterCount];
            var sumA = new double[clusterCount];
            var sumB = new double[clusterCount];

            foreach (var sample in samples)
            {
                var nearestCluster = FindNearestCluster(sample, centerL, centerA, centerB, clusterCount);
                clusterWeights[nearestCluster] += sample.Weight;
                sumL[nearestCluster] += sample.L * sample.Weight;
                sumA[nearestCluster] += sample.A * sample.Weight;
                sumB[nearestCluster] += sample.B * sample.Weight;
            }

            var movement = 0d;
            for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
            {
                if (clusterWeights[clusterIndex] <= 0)
                    continue;

                var newL = sumL[clusterIndex] / clusterWeights[clusterIndex];
                var newA = sumA[clusterIndex] / clusterWeights[clusterIndex];
                var newB = sumB[clusterIndex] / clusterWeights[clusterIndex];
                movement += OklabDistanceSquared(
                    centerL[clusterIndex],
                    centerA[clusterIndex],
                    centerB[clusterIndex],
                    newL,
                    newA,
                    newB);
                centerL[clusterIndex] = newL;
                centerA[clusterIndex] = newA;
                centerB[clusterIndex] = newB;
            }

            if (movement < 1e-8)
                break;
        }

        Array.Clear(clusterWeights);
        foreach (var sample in samples)
        {
            var nearestCluster = FindNearestCluster(sample, centerL, centerA, centerB, clusterCount);
            clusterWeights[nearestCluster] += sample.Weight;
        }

        var hasChromaticCluster = false;
        for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
        {
            var share = clusterWeights[clusterIndex] / totalWeight;
            var chroma = Math.Sqrt(centerA[clusterIndex] * centerA[clusterIndex] + centerB[clusterIndex] * centerB[clusterIndex]);
            if (share >= 0.025 && chroma >= 0.04)
            {
                hasChromaticCluster = true;
                break;
            }
        }

        var selectedCluster = 0;
        var selectedScore = double.MinValue;
        for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
        {
            if (clusterWeights[clusterIndex] <= 0)
                continue;

            var share = clusterWeights[clusterIndex] / totalWeight;
            var lightness = centerL[clusterIndex];
            var chroma = Math.Sqrt(centerA[clusterIndex] * centerA[clusterIndex] + centerB[clusterIndex] * centerB[clusterIndex]);
            var normalizedChroma = Math.Clamp(chroma / 0.22, 0, 1);
            var populationScore = Math.Pow(share, 0.72);
            var chromaScore = hasChromaticCluster ? 0.6 + 1.1 * normalizedChroma : 1;
            if (hasChromaticCluster && chroma < 0.025)
                chromaScore *= 0.65;

            var middleLightness = Math.Clamp(1 - Math.Abs(lightness - 0.58) / 0.58, 0, 1);
            var lightnessScore = 0.55 + 0.45 * middleLightness;
            if (lightness < 0.06 || lightness > 0.96)
                lightnessScore *= 0.2;
            else if (lightness < 0.16)
                lightnessScore *= 0.55;
            else if (lightness > 0.9)
                lightnessScore *= 0.6;

            var score = populationScore * chromaScore * lightnessScore;
            if (score > selectedScore)
            {
                selectedScore = score;
                selectedCluster = clusterIndex;
            }
        }

        var color = OklabToSrgb(centerL[selectedCluster], centerA[selectedCluster], centerB[selectedCluster]);
        return NormalizeAlbumColorBrightness(color);
    }

    private static (byte R, byte G, byte B) NormalizeAlbumColorBrightness((byte R, byte G, byte B) color)
    {
        // HSV 的 V 最低为 50%（8 位通道向上取整为 128），亮色保持原样。
        // 暗色等比例缩放 RGB，保留色相和饱和度；纯黑使用最低亮度的中性灰。
        const byte minimumBrightness = 128;
        var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
        if (maximum >= minimumBrightness)
            return color;
        if (maximum == 0)
            return (minimumBrightness, minimumBrightness, minimumBrightness);

        var scale = (double)minimumBrightness / maximum;
        return (
            (byte)Math.Round(color.R * scale),
            (byte)Math.Round(color.G * scale),
            (byte)Math.Round(color.B * scale));
    }

    private static int FindNearestCluster(
        OklabSample sample,
        IReadOnlyList<double> centerL,
        IReadOnlyList<double> centerA,
        IReadOnlyList<double> centerB,
        int clusterCount)
    {
        var nearestCluster = 0;
        var nearestDistance = double.MaxValue;
        for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
        {
            var distance = OklabDistanceSquared(
                sample.L,
                sample.A,
                sample.B,
                centerL[clusterIndex],
                centerA[clusterIndex],
                centerB[clusterIndex]);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestCluster = clusterIndex;
            }
        }
        return nearestCluster;
    }

    private static double OklabDistanceSquared(
        double firstL,
        double firstA,
        double firstB,
        double secondL,
        double secondA,
        double secondB)
    {
        var deltaL = firstL - secondL;
        var deltaA = firstA - secondA;
        var deltaB = firstB - secondB;
        return deltaL * deltaL + deltaA * deltaA + deltaB * deltaB;
    }

    private static OklabSample ToOklab(byte red, byte green, byte blue, double weight)
    {
        var linearRed = SrgbToLinearLookup[red];
        var linearGreen = SrgbToLinearLookup[green];
        var linearBlue = SrgbToLinearLookup[blue];

        var l = 0.4122214708 * linearRed + 0.5363325363 * linearGreen + 0.0514459929 * linearBlue;
        var m = 0.2119034982 * linearRed + 0.6806995451 * linearGreen + 0.1073969566 * linearBlue;
        var s = 0.0883024619 * linearRed + 0.2817188376 * linearGreen + 0.6299787005 * linearBlue;
        var cubeRootL = Math.Cbrt(l);
        var cubeRootM = Math.Cbrt(m);
        var cubeRootS = Math.Cbrt(s);

        return new OklabSample(
            0.2104542553 * cubeRootL + 0.7936177850 * cubeRootM - 0.0040720468 * cubeRootS,
            1.9779984951 * cubeRootL - 2.4285922050 * cubeRootM + 0.4505937099 * cubeRootS,
            0.0259040371 * cubeRootL + 0.7827717662 * cubeRootM - 0.8086757660 * cubeRootS,
            weight);
    }

    private static (byte R, byte G, byte B) OklabToSrgb(double lightness, double a, double b)
    {
        var cubeRootL = lightness + 0.3963377774 * a + 0.2158037573 * b;
        var cubeRootM = lightness - 0.1055613458 * a - 0.0638541728 * b;
        var cubeRootS = lightness - 0.0894841775 * a - 1.2914855480 * b;
        var l = cubeRootL * cubeRootL * cubeRootL;
        var m = cubeRootM * cubeRootM * cubeRootM;
        var s = cubeRootS * cubeRootS * cubeRootS;
        var linearRed = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        var linearGreen = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        var linearBlue = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

        return (ToSrgbByte(linearRed), ToSrgbByte(linearGreen), ToSrgbByte(linearBlue));
    }

    private static byte ToSrgbByte(double linearColor)
    {
        var srgb = linearColor <= 0.0031308
            ? 12.92 * linearColor
            : 1.055 * Math.Pow(linearColor, 1 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(srgb, 0, 1) * 255);
    }

    private static double[] CreateSrgbToLinearLookup()
    {
        var lookup = new double[256];
        for (var value = 0; value < lookup.Length; value++)
        {
            var srgb = value / 255d;
            lookup[value] = srgb <= 0.04045
                ? srgb / 12.92
                : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }
        return lookup;
    }

    public string CurrentTitle => _currentTitle;
    public string CurrentArtist => _currentArtist;

    private static HttpClient CreateHttpClient()
    {
        // Use OS certificate validation and system proxy configuration.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HaloPixelToolBox/1.2.7");
        return client;
    }

    private async Task<T?> RunWithTimeout<T>(Windows.Foundation.IAsyncOperation<T> asyncOp, int timeoutMs)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_background.Token);
        try
        {
            cts.CancelAfter(timeoutMs);
            return await asyncOp.AsTask(cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RunWithTimeout failed: {ex.Message}");
        }
        return default;
    }

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isInitialized)
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _manager.SessionsChanged += OnSessionsChanged;
            _isInitialized = true;
            StartWindowTitlePolling();
        }
        UpdateCurrentSession();
        return _currentSession != null || IsSpotifyRunning();
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        Console.WriteLine("OnSessionsChanged event fired.");
        _background.Run(() =>
        {
            UpdateCurrentSession();
            return Task.CompletedTask;
        });
    }

    private void UpdateCurrentSession()
    {
        lock (_sessionGate)
        {
            if (_background.Token.IsCancellationRequested || _manager == null) return;

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
                    _background.Run(SyncWithSmtcAsync);
                }
            }
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        Console.WriteLine("OnMediaPropertiesChanged event fired.");
        _background.Run(SyncWithSmtcAsync);
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        Console.WriteLine("OnPlaybackInfoChanged event fired.");
        _background.Run(SyncWithSmtcAsync);
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        Console.WriteLine("OnTimelinePropertiesChanged event fired.");
        _background.Run(SyncWithSmtcAsync);
    }

    private async Task SyncWithSmtcAsync()
    {
        if (_background.Token.IsCancellationRequested || _currentSession == null) return;

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
                             var color = await GetDominantColorAsync(media.Thumbnail, _background.Token);
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
        _background.Run(async () =>
        {
            while (!_background.Token.IsCancellationRequested)
            {
                try
                {
                    var trackInfo = await GetTrackInfoAsync();

                    if (!string.IsNullOrEmpty(trackInfo.Title))
                    {
                        if (trackInfo.Artist != _currentArtist || trackInfo.Title != _currentTitle)
                        {
                            Console.WriteLine($"Track change detected via polling: {trackInfo.Artist} - {trackInfo.Title}");
                            _background.Run(() => HandleTrackChangeAsync(trackInfo.Artist, trackInfo.Title));
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(_currentTitle))
                        {
                            Console.WriteLine("No active track detected, clearing lyrics.");
                            _currentTitle = string.Empty;
                            _currentArtist = string.Empty;
                            _lyricLines = [];
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Track polling loop error: {ex.Message}");
                }
                await Task.Delay(250, _background.Token);
            }
        });
    }

    private static string GetSpotifyWindowTitle()
    {
        var processes = Process.GetProcessesByName("Spotify");
        try
        {
            foreach (var p in processes)
            {
                try
                {
                    if (!string.IsNullOrEmpty(p.MainWindowTitle))
                        return p.MainWindowTitle;
                }
                catch (InvalidOperationException) { /* Process exited during enumeration. */ }
                catch (System.ComponentModel.Win32Exception) { /* Process access denied. */ }
            }
            return string.Empty;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
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
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_background.Token);
        lock (_trackGate)
        {
            _cts?.Cancel();
            _cts = requestCancellation;
        }
        var token = requestCancellation.Token;

        try
        {
            _currentTitle = track;
            _currentArtist = artist;
            _songStartTime = DateTimeOffset.Now;
            _currentDuration = 0;
            _isSmtcSynced = false;
            _lyricLines = [];

            // Await SMTC synchronization to get the duration before requesting lyrics
            await SyncWithSmtcAsync();
            token.ThrowIfCancellationRequested();

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
                        token.ThrowIfCancellationRequested();
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

            // 2. Local cache miss: query LRCLIB database (using system proxy settings)
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
        finally
        {
            lock (_trackGate)
            {
                if (ReferenceEquals(_cts, requestCancellation))
                    _cts = null;
            }
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
                    using var getResponse = await HttpClient.GetAsync(getUrl, getCts.Token);
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
                    using var response = await HttpClient.GetAsync(cachedUrl, cachedCts.Token);
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
                    using var searchResponse = await HttpClient.GetAsync(searchUrl, searchCts.Token);
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
                    using var fallbackResponse = await HttpClient.GetAsync(fallbackUrl, fallbackCts.Token);
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
