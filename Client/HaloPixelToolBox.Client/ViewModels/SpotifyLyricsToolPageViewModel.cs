using CommunityToolkit.Mvvm.ComponentModel;
using HaloPixelToolBox.Client.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Client.Utilities;
using HaloPixelToolBox.Client.Views;
using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Utilities;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using XFEExtension.NetCore.StringExtension;
using XFEExtension.NetCore.WinUIHelper.Implements;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;

namespace HaloPixelToolBox.Client.ViewModels;

public partial class SpotifyLyricsToolPageViewModel : ServiceBaseViewModelBase<string>
{
    [ObservableProperty]
    private bool deviceReady;
    [ObservableProperty]
    private bool spotifyReady;
    [ObservableProperty]
    private bool enableSpotifyLyrics = SpotifyLyricsProfile.EnableSpotifyLyrics;
    [ObservableProperty]
    private LyricDisplayProtocol displayProtocol = SpotifyLyricsProfile.LyricDisplayProtocol;
    [ObservableProperty]
    private LyricTransitionPreset lyricTransitionPreset = SpotifyLyricsProfile.LyricTransitionPreset;
    [ObservableProperty]
    private bool switchBackWhenPause = SpotifyLyricsProfile.SwitchBackWhenPause;
    [ObservableProperty]
    private int switchBackTimeout = SpotifyLyricsProfile.SwitchBackTimeout;
    [ObservableProperty]
    private bool enableScreenColorSync = SpotifyLyricsProfile.EnableScreenColorSync;
    [ObservableProperty]
    private bool enableAmbientColorSync = SpotifyLyricsProfile.EnableAmbientColorSync;
    [ObservableProperty]
    private Core.Models.Lighting.AmbientLightEffect syncAmbientLightEffect = SpotifyLyricsProfile.SyncAmbientLightEffect;
    [ObservableProperty]
    private Core.Models.Lighting.AmbientLightEffect defaultAmbientLightEffect = SpotifyLyricsProfile.DefaultAmbientLightEffect;
    [ObservableProperty]
    private Core.Models.Lighting.AmbientLightBrightness syncAmbientLightBrightness = SpotifyLyricsProfile.SyncAmbientLightBrightness;
    [ObservableProperty]
    private int syncAmbientLightSpeed = SpotifyLyricsProfile.SyncAmbientLightSpeed;
    [ObservableProperty]
    private Core.Models.Lighting.AmbientLightBrightness defaultAmbientLightBrightness = SpotifyLyricsProfile.DefaultAmbientLightBrightness;
    [ObservableProperty]
    private int defaultAmbientLightSpeed = SpotifyLyricsProfile.DefaultAmbientLightSpeed;
    [ObservableProperty]
    private string defaultAmbientLightColor = SpotifyLyricsProfile.DefaultAmbientLightColor;
    [ObservableProperty]
    private string defaultScreenColor = SpotifyLyricsProfile.DefaultScreenColor;
    [ObservableProperty]
    private string spotifyTrackInfo = "未检测到播放中的歌曲";

    private HaloPixelDevice Device => DeviceCoordinator.Device;
    public SpotifyLyricsReader Reader { get; set; }

    public ISettingService SettingService { get; } = ServiceManager.GetService<ISettingService>();
    public bool IsFirmwareLyricProtocol => DisplayProtocol == LyricDisplayProtocol.FirmwareLyric;
    public bool IsCustomTextProtocol => DisplayProtocol == LyricDisplayProtocol.CustomText;

    private volatile bool _forceColorRefresh;
    private volatile bool _forceLyricRefresh;
    private bool _forcePauseLightRefresh;
    private bool _forcePauseScreenRefresh;
    private bool _forcePauseUIModelRefresh;
    private bool _isClockUI;
    private long _lyricSessionGeneration;

    public static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        try
        {
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return (r, g, b);
            }
        }
        catch { }
        return (0xf0, 0xb4, 0xc8); // default fallback color
    }

    public void OnNavigatedTo()
    {
        _forceColorRefresh = true;
        _forceLyricRefresh = true;
    }

    public void StopLyrics() => StopLyricSession(false);

    private readonly BackgroundTaskScope _background = new();
    private Task? _stopTask;
    public Task StopAsync() => _stopTask ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        StopLyrics();
        await _background.StopAsync();
        await Reader.DisposeAsync();
    }


    partial void OnEnableSpotifyLyricsChanged(bool value)
    {
        SpotifyLyricsProfile.EnableSpotifyLyrics = value;
        if (value)
        {
            DisableCloudMusicLyricsSource();
            Interlocked.Exchange(
                ref _lyricSessionGeneration,
                DeviceCoordinator.Activate(LyricSourceKind.Spotify));

            _forceColorRefresh = true;
            _forceLyricRefresh = true;
            ConfigureLyricTransition(true);
        }
        else
        {
            StopLyricSession(true);
        }
    }
    private static void DisableCloudMusicLyricsSource()
    {
        if (CloudMusicLyricsToolPage.Current?.ViewModel is { EnableCloudMusicLyrics: true } cloudMusicViewModel)
            cloudMusicViewModel.EnableCloudMusicLyrics = false;
        else
            CloudMusicLyricsProfile.EnableCloudMusicLyrics = false;
    }
    partial void OnDisplayProtocolChanged(LyricDisplayProtocol value)
    {
        SpotifyLyricsProfile.LyricDisplayProtocol = value;
        OnPropertyChanged(nameof(IsFirmwareLyricProtocol));
        OnPropertyChanged(nameof(IsCustomTextProtocol));
        if (!EnableSpotifyLyrics || _isClockUI)
            return;

        ExecuteAsLyricOwner(() =>
        {
            if (value == LyricDisplayProtocol.FirmwareLyric)
            {
                ConfigureLyricTransitionCore(true, true);
            }
            else
            {
                ConfigureLyricTransitionCore(false, true);
                Device.ClearLyricReplayCache();
                Device.SetTextLayout(SpotifyLyricsProfile.DefaultHaloPixelTextLayout);
            }
        });
        _forceLyricRefresh = true;
        Console.WriteLine($"[INFO]Spotify 歌词显示方式已切换为：{value}");
    }
    partial void OnLyricTransitionPresetChanged(LyricTransitionPreset value)
    {
        SpotifyLyricsProfile.LyricTransitionPreset = value;
        if (EnableSpotifyLyrics && !_isClockUI)
        {
            ConfigureLyricTransition(true, true);
            _forceLyricRefresh = true;
        }
    }
    private void ConfigureLyricTransition(
        bool enabled,
        bool force = false,
        long? sessionGeneration = null)
    {
        var generation = sessionGeneration ?? Interlocked.Read(ref _lyricSessionGeneration);
        ExecuteAsLyricOwner(generation, () => ConfigureLyricTransitionCore(enabled, force));
    }

    private void ConfigureLyricTransitionCore(bool enabled, bool force = false)
    {
        if (!DeviceReady)
            return;
        if (enabled && DisplayProtocol != LyricDisplayProtocol.FirmwareLyric)
            return;

        try
        {
            var result = Device.ConfigureLyricTransition(LyricTransitionPreset, enabled, force);
            if (result.Status is LyricWriteStatus.DeviceUnavailable or LyricWriteStatus.IoError)
            {
                _forceLyricRefresh = enabled;
                return;
            }

            if (result.TransitionWritten)
                Console.WriteLine($"[INFO]Spotify 歌词动画：{(enabled ? $"已请求 {LyricTransitionPreset}" : "已请求停用")}");
        }
        catch (OperationCanceledException) when (_background.Token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _forceLyricRefresh = enabled;
            Console.WriteLine($"[ERROR]设置 Spotify 歌词动画失败：{ex.Message}");
        }
    }

    private bool TryShowLyricText(string text)
    {
        if (DisplayProtocol == LyricDisplayProtocol.CustomText)
        {
            Device.SetTextLayout(SpotifyLyricsProfile.DefaultHaloPixelTextLayout);
            return Device.ShowLegacyText(text);
        }

        var result = Device.ShowLyricText(text, LyricTransitionPreset);
        if (result.Status != LyricWriteStatus.UnsupportedDevice)
            return result.TextWritten;

        Device.SetTextLayout(SpotifyLyricsProfile.DefaultHaloPixelTextLayout);
        return Device.ShowLegacyText(text);
    }

    private bool ExecuteAsLyricOwner(Action action)
        => ExecuteAsLyricOwner(Interlocked.Read(ref _lyricSessionGeneration), action);

    private bool ExecuteAsLyricOwner(long sessionGeneration, Action action)
        => DeviceCoordinator.ExecuteIfOwner(
            LyricSourceKind.Spotify,
            sessionGeneration,
            action);

    private void StopLyricSession(bool restoreDefaultDisplay)
    {
        var generation = Interlocked.Read(ref _lyricSessionGeneration);
        DeviceCoordinator.Deactivate(
            LyricSourceKind.Spotify,
            generation,
            () =>
            {
                ConfigureLyricTransitionCore(false);
                Device.ClearLyricReplayCache();
                if (restoreDefaultDisplay)
                    RestoreDefaultDisplayCore();
            });
        Interlocked.CompareExchange(ref _lyricSessionGeneration, 0, generation);
    }

    private void RestoreDefaultDisplayCore()
    {
        if (!DeviceReady)
            return;

        _isClockUI = true;
        var screenColor = ParseHexColor(SpotifyLyricsProfile.DefaultScreenColor);
        var ambientColor = ParseHexColor(SpotifyLyricsProfile.DefaultAmbientLightColor);
        HidPacketBuilder.CurrentColor = screenColor;
        try
        {
            Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
            {
                Effect = SpotifyLyricsProfile.DefaultAmbientLightEffect,
                Color = new Core.Models.Display.HaloPixelColor(ambientColor.R, ambientColor.G, ambientColor.B),
                Brightness = SpotifyLyricsProfile.DefaultAmbientLightBrightness,
                Speed = (byte)SpotifyLyricsProfile.DefaultAmbientLightSpeed
            });
        }
        catch { }
        try
        {
            Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(screenColor.R, screenColor.G, screenColor.B));
        }
        catch { }
        try
        {
            Device.SetUIModel(SpotifyLyricsProfile.DefaultHaloPixelUIModel);
        }
        catch { }
    }
    partial void OnSwitchBackWhenPauseChanged(bool value) => SpotifyLyricsProfile.SwitchBackWhenPause = value;
    partial void OnSwitchBackTimeoutChanged(int value) => SpotifyLyricsProfile.SwitchBackTimeout = value;
    partial void OnEnableScreenColorSyncChanged(bool value)
    {
        SpotifyLyricsProfile.EnableScreenColorSync = value;
        if (!_isClockUI)
            _forceColorRefresh = true;
    }
    partial void OnEnableAmbientColorSyncChanged(bool value)
    {
        SpotifyLyricsProfile.EnableAmbientColorSync = value;
        if (!_isClockUI)
            _forceColorRefresh = true;
    }
    partial void OnSyncAmbientLightEffectChanged(Core.Models.Lighting.AmbientLightEffect value)
    {
        SpotifyLyricsProfile.SyncAmbientLightEffect = value;
        if (!_isClockUI)
            _forceColorRefresh = true;
    }
    partial void OnDefaultAmbientLightEffectChanged(Core.Models.Lighting.AmbientLightEffect value)
    {
        SpotifyLyricsProfile.DefaultAmbientLightEffect = value;
        if (_isClockUI)
            _forcePauseLightRefresh = true;
    }
    partial void OnSyncAmbientLightBrightnessChanged(Core.Models.Lighting.AmbientLightBrightness value)
    {
        SpotifyLyricsProfile.SyncAmbientLightBrightness = value;
        if (!_isClockUI)
            _forceColorRefresh = true;
    }
    partial void OnSyncAmbientLightSpeedChanged(int value)
    {
        SpotifyLyricsProfile.SyncAmbientLightSpeed = value;
        if (!_isClockUI)
            _forceColorRefresh = true;
    }
    partial void OnDefaultAmbientLightBrightnessChanged(Core.Models.Lighting.AmbientLightBrightness value)
    {
        SpotifyLyricsProfile.DefaultAmbientLightBrightness = value;
        OnPropertyChanged(nameof(DefaultAmbientLightBrightnessIndex));
        if (_isClockUI)
            _forcePauseLightRefresh = true;
    }
    partial void OnDefaultAmbientLightSpeedChanged(int value)
    {
        SpotifyLyricsProfile.DefaultAmbientLightSpeed = value;
        if (_isClockUI)
            _forcePauseLightRefresh = true;
    }
    partial void OnDefaultAmbientLightColorChanged(string value)
    {
        Console.WriteLine($"[DEBUG] VM OnDefaultAmbientLightColorChanged called, value={value}, _isClockUI={_isClockUI}");
        SpotifyLyricsProfile.DefaultAmbientLightColor = value;
        if (_isClockUI)
        {
            _forcePauseLightRefresh = true;
            Console.WriteLine("[DEBUG] Set _forcePauseLightRefresh = true");
        }
    }
    partial void OnDefaultScreenColorChanged(string value)
    {
        Console.WriteLine($"[DEBUG] VM OnDefaultScreenColorChanged called, value={value}, _isClockUI={_isClockUI}");
        SpotifyLyricsProfile.DefaultScreenColor = value;
        if (_isClockUI)
        {
            _forcePauseScreenRefresh = true;
            Console.WriteLine("[DEBUG] Set _forcePauseScreenRefresh = true");
        }
    }

    public int DefaultAmbientLightBrightnessIndex
    {
        get => (int)DefaultAmbientLightBrightness - 1;
        set
        {
            if (value >= 0 && value <= 2)
            {
                DefaultAmbientLightBrightness = (Core.Models.Lighting.AmbientLightBrightness)(value + 1);
            }
        }
    }

    public void ForcePauseLightRefresh()
    {
        if (_isClockUI)
        {
            _forcePauseLightRefresh = true;
            Console.WriteLine("[DEBUG] ForcePauseLightRefresh invoked");
        }
    }

    public void ForcePauseScreenRefresh()
    {
        if (_isClockUI)
        {
            _forcePauseScreenRefresh = true;
            Console.WriteLine("[DEBUG] ForcePauseScreenRefresh invoked");
        }
    }

    public void ForcePauseUIModelRefresh()
    {
        if (_isClockUI)
        {
            _forcePauseUIModelRefresh = true;
            Console.WriteLine("[DEBUG] ForcePauseUIModelRefresh invoked");
        }
    }

    public SpotifyLyricsToolPageViewModel()
    {
        if (EnableSpotifyLyrics)
        {
            DisableCloudMusicLyricsSource();
            _lyricSessionGeneration = DeviceCoordinator.Activate(LyricSourceKind.Spotify);
        }

        Console.WriteLine("初始化Spotify歌词读取器");
        Reader = new SpotifyLyricsReader();

        Console.WriteLine("准备启动Spotify后台线程");
        _background.Run(async () =>
        {
            while (!_background.Token.IsCancellationRequested)
            {
                var ready = Device.Initialize();
                AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_background.Token.IsCancellationRequested) DeviceReady = ready;
                });
                await Task.Delay(500, _background.Token);
            }
        });
        _background.Run(async () =>
        {
            while (!_background.Token.IsCancellationRequested)
            {
                var ready = false;
                try
                {
                    ready = await Reader.InitializeAsync(_background.Token);
                }
                catch (OperationCanceledException) when (_background.Token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN]检测音乐播放器失败：{ex.Message}");
                }
                AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_background.Token.IsCancellationRequested) return;
                    SpotifyReady = ready;
                    SpotifyTrackInfo = !string.IsNullOrEmpty(Reader.CurrentTitle) ? $"{Reader.CurrentArtist} - {Reader.CurrentTitle}" : "未检测到播放中的歌曲";
                });
                await Task.Delay(500, _background.Token);
            }
        });
        _background.Run(async () =>
        {
            Console.WriteLine("启动Spotify歌词主线程");
            Console.WriteLine("等待花再设备...");
            while (!_background.Token.IsCancellationRequested && !DeviceReady)
                await Task.Delay(500, _background.Token);

            if (DeviceReady && EnableSpotifyLyrics)
            {
                Console.WriteLine("花再设备已就绪，显示启动信息");
                var startupGeneration = Interlocked.Read(ref _lyricSessionGeneration);
                ConfigureLyricTransition(true, sessionGeneration: startupGeneration);
                ExecuteAsLyricOwner(startupGeneration, () => TryShowLyricText("Spotify歌词同步已就绪"));
                await Task.Delay(3000, _background.Token);
            }

            while (!_background.Token.IsCancellationRequested)
            {
                _isClockUI = false;
                int time = 0;
                try
                {
                    if (DeviceReady && SpotifyReady && EnableSpotifyLyrics)
                    {
                        var sessionGeneration = Interlocked.Read(ref _lyricSessionGeneration);
                        Console.WriteLine("[DEBUG]设备均在线，准备进入主循环");
                        string lastRead = string.Empty;
                        string stalePreviousTrackLyric = string.Empty;
                        string lastTrackTitle = string.Empty;
                        string lastTrackArtist = string.Empty;
                        bool wasPlaying = false;
                        (byte R, byte G, byte B) lastScreenColor = (0, 0, 0);
                        (byte R, byte G, byte B) lastAmbientColor = (0, 0, 0);
                        var lastAmbientLightEffect = SpotifyLyricsProfile.SyncAmbientLightEffect;
                        var lastAmbientLightBrightness = SpotifyLyricsProfile.SyncAmbientLightBrightness;
                        int lastAmbientLightSpeed = SpotifyLyricsProfile.SyncAmbientLightSpeed;
                        while (!_background.Token.IsCancellationRequested)
                        {
                            try
                            {
                                if (!DeviceReady || !SpotifyReady || !EnableSpotifyLyrics ||
                                    !DeviceCoordinator.IsOwner(LyricSourceKind.Spotify, sessionGeneration))
                                    break;

                                var playbackStatus = Reader.PlaybackStatus;
                                bool isPlaying = playbackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                                bool isExplicitlyPaused = playbackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
                                bool trackChanged = lastTrackTitle != Reader.CurrentTitle || lastTrackArtist != Reader.CurrentArtist;
                                if (trackChanged)
                                {
                                    if (!string.IsNullOrEmpty(lastRead))
                                        stalePreviousTrackLyric = lastRead;
                                    lastRead = string.Empty;
                                    ExecuteAsLyricOwner(sessionGeneration, Device.ClearLyricReplayCache);
                                }
                                var lyricsRead = Reader.TryReadLyrics(out var lyrics);
                                if (lyricsRead && !LyricTextPolicy.HasVisibleContent(lyrics))
                                    lyricsRead = false;
                                if (trackChanged)
                                    lyricsRead = false;
                                else if (lyricsRead && !string.IsNullOrEmpty(stalePreviousTrackLyric))
                                {
                                    if (string.Equals(lyrics, stalePreviousTrackLyric, StringComparison.Ordinal))
                                        lyricsRead = false;
                                    else if (!string.IsNullOrEmpty(lyrics))
                                        stalePreviousTrackLyric = string.Empty;
                                }
                                bool lyricsChanged = lyricsRead && lastRead != lyrics;

                                var resumeLyricMode = isPlaying && _isClockUI;
                                if (isPlaying && (_isClockUI || trackChanged))
                                {
                                    if (!wasPlaying && isPlaying)
                                    {
                                        _isClockUI = false;
                                        _forceColorRefresh = true;
                                        _forceLyricRefresh = true;
                                        time = 0;
                                    }
                                    else if (trackChanged)
                                    {
                                        _isClockUI = false;
                                        _forceColorRefresh = true;
                                        _forceLyricRefresh = true;
                                        time = 0;
                                    }
                                }
                                wasPlaying = isPlaying;
                                lastTrackTitle = Reader.CurrentTitle ?? string.Empty;
                                lastTrackArtist = Reader.CurrentArtist ?? string.Empty;

                                if (resumeLyricMode)
                                    ConfigureLyricTransition(
                                        true,
                                        sessionGeneration: sessionGeneration);

                                // Update track info text in UI
                                AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() =>
                                {
                                    SpotifyTrackInfo = !string.IsNullOrEmpty(Reader.CurrentTitle) ? $"{Reader.CurrentArtist} - {Reader.CurrentTitle}" : "无播放中的歌曲";
                                });

                                var albumColor = Reader.CurrentAlbumColor;

                                // 1. Determine screen text color
                                (byte R, byte G, byte B) screenColor = ((byte)0xf0, (byte)0xb4, (byte)0xc8);
                                if (EnableScreenColorSync && albumColor.HasValue)
                                {
                                    screenColor = albumColor.Value;
                                }

                                // 2. Determine ambient backlight color
                                (byte R, byte G, byte B) ambientColor = ((byte)0xf0, (byte)0xb4, (byte)0xc8);
                                if (EnableAmbientColorSync && albumColor.HasValue)
                                {
                                    ambientColor = albumColor.Value;
                                }

                                var ambientEffect = SpotifyLyricsProfile.SyncAmbientLightEffect;
                                var ambientBrightness = SpotifyLyricsProfile.SyncAmbientLightBrightness;
                                int ambientSpeed = SpotifyLyricsProfile.SyncAmbientLightSpeed;

                                bool colorChanged = lastScreenColor != screenColor || lastAmbientColor != ambientColor;
                                bool effectChanged = lastAmbientLightEffect != ambientEffect || lastAmbientLightBrightness != ambientBrightness || lastAmbientLightSpeed != ambientSpeed;

                                if (!_isClockUI && (colorChanged || effectChanged || _forceColorRefresh))
                                {
                                    _forceColorRefresh = false;
                                    lastScreenColor = screenColor;
                                    lastAmbientColor = ambientColor;
                                    lastAmbientLightEffect = ambientEffect;
                                    lastAmbientLightBrightness = ambientBrightness;
                                    lastAmbientLightSpeed = ambientSpeed;
                                    ExecuteAsLyricOwner(sessionGeneration, () => HidPacketBuilder.CurrentColor = screenColor);

                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = ambientEffect,
                                            Color = new Core.Models.Display.HaloPixelColor(ambientColor.R, ambientColor.G, ambientColor.B),
                                            Brightness = ambientBrightness,
                                            Speed = (byte)ambientSpeed
                                        }));
                                    }
                                    catch (OperationCanceledException) when (_background.Token.IsCancellationRequested) { throw; }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[ERROR] SetAmbientLight failed: {ex.Message}");
                                    }

                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(screenColor.R, screenColor.G, screenColor.B)));
                                    }
                                    catch (OperationCanceledException) when (_background.Token.IsCancellationRequested) { throw; }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[ERROR] SetPixelScreenColor failed: {ex.Message}");
                                    }
                                }

                                if (!_isClockUI && (lyricsChanged || _forceLyricRefresh))
                                {
                                    var lyricToSend = lyricsChanged ? lyrics : lastRead;
                                    if (LyricTextPolicy.HasVisibleContent(lyricToSend))
                                    {
                                        var lyricWriteSucceeded = false;
                                        var writeWasOwned = ExecuteAsLyricOwner(
                                            sessionGeneration,
                                            () => lyricWriteSucceeded = TryShowLyricText(lyricToSend));

                                        if (writeWasOwned && lyricWriteSucceeded)
                                        {
                                            _forceLyricRefresh = false;
                                            if (lyricsChanged)
                                            {
                                                Console.WriteLine($"已读取到歌词：{lyrics}");
                                                lastRead = lyrics;
                                                time = 0;

                                                if (DisplayProtocol == LyricDisplayProtocol.CustomText &&
                                                    lyrics.DisplayLength() > 30)
                                                {
                                                    await Task.Delay(500, _background.Token);
                                                    if (DisplayProtocol == LyricDisplayProtocol.CustomText)
                                                    {
                                                        ExecuteAsLyricOwner(
                                                            sessionGeneration,
                                                            () => Device.SetTextLayout(HaloPixelTextLayout.ScrollRightToLeft));
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            _forceLyricRefresh = true;
                                            await Task.Delay(200, _background.Token);
                                        }
                                    }
                                }
                                await Task.Delay(50, _background.Token);
                                time = isExplicitlyPaused ? time + 50 : 0;
                                if (SwitchBackWhenPause && isExplicitlyPaused && !_isClockUI && time >= SpotifyLyricsProfile.SwitchBackTimeout * 1000)
                                {
                                    _isClockUI = true;
                                    ConfigureLyricTransition(false, sessionGeneration: sessionGeneration);
                                    (byte R, byte G, byte B) finalScreenColor = ParseHexColor(SpotifyLyricsProfile.DefaultScreenColor);
                                    (byte R, byte G, byte B) finalAmbientColor = ParseHexColor(SpotifyLyricsProfile.DefaultAmbientLightColor);
                                    ExecuteAsLyricOwner(sessionGeneration, () => HidPacketBuilder.CurrentColor = finalScreenColor);
                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = SpotifyLyricsProfile.DefaultAmbientLightEffect,
                                            Color = new Core.Models.Display.HaloPixelColor(finalAmbientColor.R, finalAmbientColor.G, finalAmbientColor.B),
                                            Brightness = SpotifyLyricsProfile.DefaultAmbientLightBrightness,
                                            Speed = (byte)SpotifyLyricsProfile.DefaultAmbientLightSpeed
                                        }));
                                    }
                                    catch {}
                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(finalScreenColor.R, finalScreenColor.G, finalScreenColor.B)));
                                    }
                                    catch {}
                                    ExecuteAsLyricOwner(sessionGeneration, () => Device.SetUIModel(SpotifyLyricsProfile.DefaultHaloPixelUIModel));
                                    Console.WriteLine("已切换至时钟界面");
                                }

                                if (_isClockUI && _forcePauseLightRefresh)
                                {
                                    _forcePauseLightRefresh = false;
                                    (byte R, byte G, byte B) finalAmbientColor = ParseHexColor(SpotifyLyricsProfile.DefaultAmbientLightColor);
                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = SpotifyLyricsProfile.DefaultAmbientLightEffect,
                                            Color = new Core.Models.Display.HaloPixelColor(finalAmbientColor.R, finalAmbientColor.G, finalAmbientColor.B),
                                            Brightness = SpotifyLyricsProfile.DefaultAmbientLightBrightness,
                                            Speed = (byte)SpotifyLyricsProfile.DefaultAmbientLightSpeed
                                        }));
                                    }
                                    catch {}
                                }

                                if (_isClockUI && _forcePauseScreenRefresh)
                                {
                                    _forcePauseScreenRefresh = false;
                                    (byte R, byte G, byte B) finalScreenColor = ParseHexColor(SpotifyLyricsProfile.DefaultScreenColor);
                                    ExecuteAsLyricOwner(sessionGeneration, () => HidPacketBuilder.CurrentColor = finalScreenColor);
                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(finalScreenColor.R, finalScreenColor.G, finalScreenColor.B)));
                                    }
                                    catch {}
                                }

                                if (_isClockUI && _forcePauseUIModelRefresh)
                                {
                                    _forcePauseUIModelRefresh = false;
                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetUIModel(SpotifyLyricsProfile.DefaultHaloPixelUIModel));
                                    }
                                    catch {}
                                }
                            }
                            catch (OperationCanceledException) when (_background.Token.IsCancellationRequested) { throw; }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR]Spotify歌词主循环发生错误：{ex.Message}");
                                Console.WriteLine($"[TRACE]{ex.StackTrace}");
                                await Task.Delay(500, _background.Token);
                            }
                        }
                        ConfigureLyricTransition(false, sessionGeneration: sessionGeneration);
                    }
                    await Task.Delay(500, _background.Token);
                }
                catch (OperationCanceledException) when (_background.Token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]Spotify歌词主线程发生错误：{ex.Message}");
                    Console.WriteLine($"[TRACE]{ex.StackTrace}");
                    await Task.Delay(500, _background.Token);
                }
            }
        });
        Console.WriteLine("Spotify后台线程启动完成");
    }
}
