using CommunityToolkit.Mvvm.ComponentModel;
using HaloPixelToolBox.Client.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Client.Utilities;
using HaloPixelToolBox.Client.Views;
using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Utilities;
using System.Diagnostics;
using XFEExtension.NetCore.StringExtension;
using XFEExtension.NetCore.WinUIHelper.Implements;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;

namespace HaloPixelToolBox.Client.ViewModels;

public partial class CloudMusicLyricsToolPageViewModel : ServiceBaseViewModelBase<string>
{
    [ObservableProperty] public partial bool DeviceReady { get; set; }
    [ObservableProperty] public partial bool CloudMusicReady { get; set; }
    [ObservableProperty] public partial bool EnableCloudMusicLyrics { get; set; } = CloudMusicLyricsProfile.EnableCloudMusicLyrics;
    [ObservableProperty] public partial LyricDisplayProtocol DisplayProtocol { get; set; } = CloudMusicLyricsProfile.LyricDisplayProtocol;
    [ObservableProperty] public partial LyricTransitionPreset LyricTransitionPreset { get; set; } = CloudMusicLyricsProfile.LyricTransitionPreset;
    [ObservableProperty] public partial bool SwitchBackWhenPause { get; set; } = CloudMusicLyricsProfile.SwitchBackWhenPause;
    [ObservableProperty] public partial bool UseInputedAddress { get; set; } = CloudMusicLyricsProfile.UseInputedAddress;
    [ObservableProperty] public partial string InputedAddress { get; set; } = CloudMusicLyricsProfile.InputedAddress;
    [ObservableProperty] public partial int SwitchBackTimeout { get; set; } = CloudMusicLyricsProfile.SwitchBackTimeout;
    [ObservableProperty] public partial bool EnableScreenColorSync { get; set; } = CloudMusicLyricsProfile.EnableScreenColorSync;
    [ObservableProperty] public partial bool EnableAmbientColorSync { get; set; } = CloudMusicLyricsProfile.EnableAmbientColorSync;
    [ObservableProperty] public partial Core.Models.Lighting.AmbientLightEffect SyncAmbientLightEffect { get; set; } = CloudMusicLyricsProfile.SyncAmbientLightEffect;
    [ObservableProperty] public partial Core.Models.Lighting.AmbientLightBrightness SyncAmbientLightBrightness { get; set; } = CloudMusicLyricsProfile.SyncAmbientLightBrightness;
    [ObservableProperty] public partial int SyncAmbientLightSpeed { get; set; } = CloudMusicLyricsProfile.SyncAmbientLightSpeed;
    [ObservableProperty] public partial string CloudMusicVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupportedVersion { get; set; } = "等待检测";
    public HaloPixelDevice Device { get; set; } = HaloPixelDevice.Shared;
    public CloudMusicLyricsReader Reader { get; set; }

    public ISettingService SettingService { get; } = ServiceManager.GetService<ISettingService>();
    public bool IsFirmwareLyricProtocol => DisplayProtocol == LyricDisplayProtocol.FirmwareLyric;
    public bool IsCustomTextProtocol => DisplayProtocol == LyricDisplayProtocol.CustomText;

    private string _lastResolverAttemptVersion = string.Empty;
    private DateTime _lastResolverAttemptTime = DateTime.MinValue;

    /// <summary>
    /// Safely parse a hexadecimal address string to nint, returning 0 if parsing fails
    /// </summary>
    private static nint ParseHexAddress(string hexAddress)
    {
        if (string.IsNullOrWhiteSpace(hexAddress))
            return 0;

        try
        {
            return new nint(Convert.ToInt64(hexAddress, 16));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            Console.WriteLine($"[WARNING]无法解析内存地址 '{hexAddress}'，使用默认值 0。错误：{ex.Message}");
            return 0;
        }
    }

    private volatile bool _forceColorRefresh;
    private volatile bool _forceLyricRefresh;
    private bool _forcePauseLightRefresh;
    private bool _forcePauseScreenRefresh;
    private bool _forcePauseUIModelRefresh;
    private bool _isClockUI;
    private long _lyricSessionGeneration;

    public void OnNavigatedTo()
    {
        _forceColorRefresh = true;
        _forceLyricRefresh = true;
    }

    public void StopLyrics() => StopLyricSession(false);

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

    public void ForcePauseLightRefresh()
    {
        if (_isClockUI)
        {
            _forcePauseLightRefresh = true;
            Console.WriteLine("[DEBUG] NetEase ForcePauseLightRefresh invoked");
        }
    }

    public void ForcePauseScreenRefresh()
    {
        if (_isClockUI)
        {
            _forcePauseScreenRefresh = true;
            Console.WriteLine("[DEBUG] NetEase ForcePauseScreenRefresh invoked");
        }
    }

    public void ForcePauseUIModelRefresh()
    {
        if (_isClockUI)
        {
            _forcePauseUIModelRefresh = true;
            Console.WriteLine("[DEBUG] NetEase ForcePauseUIModelRefresh invoked");
        }
    }

    partial void OnEnableCloudMusicLyricsChanged(bool value)
    {
        CloudMusicLyricsProfile.EnableCloudMusicLyrics = value;
        if (value)
        {
            DisableSpotifyLyricsSource();
            Interlocked.Exchange(
                ref _lyricSessionGeneration,
                LyricSessionCoordinator.Activate(LyricSourceKind.CloudMusic));

            _forceColorRefresh = true;
            _forceLyricRefresh = true;
            ConfigureLyricTransition(true);
        }
        else
        {
            StopLyricSession(true);
        }
    }

    private static void DisableSpotifyLyricsSource()
    {
        if (SpotifyLyricsToolPage.Current?.ViewModel is { EnableSpotifyLyrics: true } spotifyViewModel)
            spotifyViewModel.EnableSpotifyLyrics = false;
        else
            SpotifyLyricsProfile.EnableSpotifyLyrics = false;
    }

    partial void OnDisplayProtocolChanged(LyricDisplayProtocol value)
    {
        CloudMusicLyricsProfile.LyricDisplayProtocol = value;
        OnPropertyChanged(nameof(IsFirmwareLyricProtocol));
        OnPropertyChanged(nameof(IsCustomTextProtocol));
        if (!EnableCloudMusicLyrics || _isClockUI)
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
                Device.SetTextLayout(CloudMusicLyricsProfile.DefaultHaloPixelTextLayout);
            }
        });
        _forceLyricRefresh = true;
        Console.WriteLine($"[INFO]网易云歌词显示方式已切换为：{value}");
    }

    partial void OnLyricTransitionPresetChanged(LyricTransitionPreset value)
    {
        CloudMusicLyricsProfile.LyricTransitionPreset = value;
        if (EnableCloudMusicLyrics && !_isClockUI)
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
                Console.WriteLine($"[INFO]网易云歌词动画：{(enabled ? $"已请求 {LyricTransitionPreset}" : "已请求停用")}");
        }
        catch (Exception ex)
        {
            _forceLyricRefresh = enabled;
            Console.WriteLine($"[ERROR]设置网易云歌词动画失败：{ex.Message}");
        }
    }

    private bool TryShowLyricText(string text)
    {
        if (DisplayProtocol == LyricDisplayProtocol.CustomText)
        {
            Device.SetTextLayout(CloudMusicLyricsProfile.DefaultHaloPixelTextLayout);
            return Device.ShowLegacyText(text);
        }

        var result = Device.ShowLyricText(text, LyricTransitionPreset);
        if (result.Status != LyricWriteStatus.UnsupportedDevice)
            return result.TextWritten;

        Device.SetTextLayout(CloudMusicLyricsProfile.DefaultHaloPixelTextLayout);
        return Device.ShowLegacyText(text);
    }

    private bool ExecuteAsLyricOwner(Action action)
        => ExecuteAsLyricOwner(Interlocked.Read(ref _lyricSessionGeneration), action);

    private bool ExecuteAsLyricOwner(long sessionGeneration, Action action)
        => LyricSessionCoordinator.ExecuteIfOwner(
            LyricSourceKind.CloudMusic,
            sessionGeneration,
            action);

    private void StopLyricSession(bool restoreDefaultDisplay)
    {
        var generation = Interlocked.Read(ref _lyricSessionGeneration);
        LyricSessionCoordinator.Deactivate(
            LyricSourceKind.CloudMusic,
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
        var screenColor = ParseHexColor(CloudMusicLyricsProfile.DefaultScreenColor);
        var ambientColor = ParseHexColor(CloudMusicLyricsProfile.DefaultAmbientLightColor);
        HidPacketBuilder.CurrentColor = screenColor;
        try
        {
            Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
            {
                Effect = CloudMusicLyricsProfile.DefaultAmbientLightEffect,
                Color = new Core.Models.Display.HaloPixelColor(ambientColor.R, ambientColor.G, ambientColor.B),
                Brightness = CloudMusicLyricsProfile.DefaultAmbientLightBrightness,
                Speed = (byte)CloudMusicLyricsProfile.DefaultAmbientLightSpeed
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
            Device.SetUIModel(CloudMusicLyricsProfile.DefaultHaloPixelUIModel);
        }
        catch { }
    }

    partial void OnUseInputedAddressChanged(bool value)
    {
        CloudMusicLyricsProfile.UseInputedAddress = value;
        Reader.UseInputedAddress = value;
    }

    partial void OnInputedAddressChanged(string value)
    {
        CloudMusicLyricsProfile.InputedAddress = value;
        Reader.Address = ParseHexAddress(value);
    }

    partial void OnSwitchBackWhenPauseChanged(bool value) => CloudMusicLyricsProfile.SwitchBackWhenPause = value;
    partial void OnSwitchBackTimeoutChanged(int value) => CloudMusicLyricsProfile.SwitchBackTimeout = value;

    partial void OnEnableScreenColorSyncChanged(bool value)
    {
        CloudMusicLyricsProfile.EnableScreenColorSync = value;
        if (!_isClockUI)
            _forceColorRefresh = true;
    }

    partial void OnEnableAmbientColorSyncChanged(bool value)
    {
        CloudMusicLyricsProfile.EnableAmbientColorSync = value;
        if (!_isClockUI)
            _forceColorRefresh = true;
    }

    partial void OnSyncAmbientLightEffectChanged(Core.Models.Lighting.AmbientLightEffect value)
    {
        CloudMusicLyricsProfile.SyncAmbientLightEffect = value;
        if (!_isClockUI)
            _forceColorRefresh = true;
    }

    partial void OnSyncAmbientLightBrightnessChanged(Core.Models.Lighting.AmbientLightBrightness value)
    {
        CloudMusicLyricsProfile.SyncAmbientLightBrightness = value;
        if (!_isClockUI)
            _forceColorRefresh = true;
    }

    partial void OnSyncAmbientLightSpeedChanged(int value)
    {
        CloudMusicLyricsProfile.SyncAmbientLightSpeed = value;
        if (!_isClockUI)
            _forceColorRefresh = true;
    }

    private Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _smtcManager;
    private Windows.Media.Control.GlobalSystemMediaTransportControlsSession? _cloudMusicSession;
    public (byte R, byte G, byte B)? CurrentAlbumColor { get; private set; }
    private string _currentTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private string _albumColorTrackIdentity = string.Empty;
    private int _albumColorUpdateGeneration;

    public bool IsPlaying
    {
        get
        {
            if (_cloudMusicSession == null) return false;
            try
            {
                var playbackInfo = _cloudMusicSession.GetPlaybackInfo();
                return playbackInfo?.PlaybackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch {}
            return false;
        }
    }

    public string CurrentTitle => _currentTitle;
    public string CurrentArtist => _currentArtist;

    private async Task InitSmtcAsync()
    {
        try
        {
            _smtcManager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            UpdateCloudMusicSession();
            _smtcManager.SessionsChanged += (s, e) => UpdateCloudMusicSession();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SMTC init failed: {ex.Message}");
        }
    }

    private void UpdateCloudMusicSession()
    {
        if (_smtcManager == null) return;
        var sessions = _smtcManager.GetSessions();
        var targetSession = sessions.FirstOrDefault(x => x.SourceAppUserModelId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase));
        if (targetSession != _cloudMusicSession)
        {
            if (_cloudMusicSession != null)
            {
                _cloudMusicSession.MediaPropertiesChanged -= OnSmtcMediaPropertiesChanged;
            }
            _cloudMusicSession = targetSession;
            if (_cloudMusicSession != null)
            {
                _cloudMusicSession.MediaPropertiesChanged += OnSmtcMediaPropertiesChanged;
                QueueAlbumColorUpdate();
            }
            else
            {
                Interlocked.Increment(ref _albumColorUpdateGeneration);
                _currentTitle = string.Empty;
                _currentArtist = string.Empty;
                _albumColorTrackIdentity = string.Empty;
                CurrentAlbumColor = null;
            }
        }
    }

    private void OnSmtcMediaPropertiesChanged(Windows.Media.Control.GlobalSystemMediaTransportControlsSession sender, Windows.Media.Control.MediaPropertiesChangedEventArgs args)
    {
        QueueAlbumColorUpdate();
    }

    private void QueueAlbumColorUpdate()
    {
        var generation = Interlocked.Increment(ref _albumColorUpdateGeneration);
        _ = UpdateAlbumColorAsync(generation);
    }

    private async Task UpdateAlbumColorAsync(int generation)
    {
        // NetEase commonly raises two events during a track change: the first can
        // contain the new title with the previous thumbnail, while the corrected
        // thumbnail can arrive close to one second later. Coalesce the pair so the
        // previous track color is never applied to the new title.
        await Task.Delay(1200);
        if (generation != Volatile.Read(ref _albumColorUpdateGeneration))
            return;

        var session = _cloudMusicSession;
        if (session == null)
            return;

        try
        {
            var media = await session.TryGetMediaPropertiesAsync();
            if (media == null || generation != Volatile.Read(ref _albumColorUpdateGeneration) || session != _cloudMusicSession)
                return;

            var title = media.Title ?? string.Empty;
            var artist = media.Artist ?? string.Empty;
            var trackIdentity = $"{title}\n{artist}";
            var trackChanged = !string.Equals(_albumColorTrackIdentity, trackIdentity, StringComparison.Ordinal);

            _currentTitle = title;
            _currentArtist = artist;
            if (trackChanged)
            {
                _albumColorTrackIdentity = trackIdentity;
                CurrentAlbumColor = null;
            }

            if (media.Thumbnail == null)
            {
                CurrentAlbumColor = null;
                _forceColorRefresh = true;
                Console.WriteLine($"[WARN]网易云 SMTC 当前曲目没有封面：{title} - {artist}");
                return;
            }

            var color = await SpotifyLyricsReader.GetDominantColorAsync(media.Thumbnail);
            if (generation != Volatile.Read(ref _albumColorUpdateGeneration) || session != _cloudMusicSession)
                return;

            CurrentAlbumColor = color;
            _forceColorRefresh = true;
            if (color.HasValue)
            {
                Console.WriteLine(
                    $"[INFO]已从网易云 SMTC 提取封面颜色：{title} - {artist} / RGB({color.Value.R}, {color.Value.G}, {color.Value.B})");
            }
            else
            {
                Console.WriteLine($"[WARN]无法从网易云 SMTC 封面提取颜色：{title} - {artist}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update CloudMusic album color: {ex.Message}");
        }
    }

    public CloudMusicLyricsToolPageViewModel()
    {
        if (EnableCloudMusicLyrics)
        {
            DisableSpotifyLyricsSource();
            _lyricSessionGeneration = LyricSessionCoordinator.Activate(LyricSourceKind.CloudMusic);
        }

        _ = InitSmtcAsync();
        Console.WriteLine("初始化网易云歌词读取器");
        AddressResolverProvider.LoadCachedResolvers();
        var newestMemoryVersion = CloudMusicLyricsReader.VersionResolverDictionary.Keys
            .OrderByDescending(static version => Version.TryParse(version, out var parsed) ? parsed : new Version())
            .FirstOrDefault();
        SupportedVersion = newestMemoryVersion is null
            ? "3.1.41"
            : $"3.1.41 / {newestMemoryVersion}";
        Reader = new CloudMusicLyricsReader
        {
            UseInputedAddress = UseInputedAddress,
            Address = ParseHexAddress(InputedAddress)
        };
        Console.WriteLine("准备启动网易云后台线程");
        Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("正在搜索花再设备...");
                while (!DeviceReady)
                {
                    var ready = Device.Initialize();
                    AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() =>
                        {
                            DeviceReady = ready;
                        });
                    await Task.Delay(500);
                }
                Console.WriteLine("花再设备已连接");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR]搜索花再设备时发生错误：{ex.Message}");
                Console.WriteLine($"[TRACE]{ex.StackTrace}");
            }

        });
        Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("正在搜索云音乐...");
                while (!CloudMusicReady)
                {
                    var ready = Reader.Initialize();
                    if (!ready && !UseInputedAddress)
                        ready = await TryUpdateAddressResolverAsync();
                    AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() =>
                    {
                        CloudMusicReady = ready;
                        CloudMusicVersion = Reader.VersionInfo is not null ? $"{Reader.VersionInfo.FileVersion}" : "未检测到云音乐";
                    });
                    await Task.Delay(500);
                }
                Console.WriteLine("云音乐已准备就绪");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR]搜索云音乐时发生错误：{ex.Message}");
                Console.WriteLine($"[TRACE]{ex.StackTrace}");
            }
        });
        Task.Run(async () =>
        {
            Console.WriteLine("启动云音乐地址重解析线程");
            while (true)
            {
                try
                {
                    Reader.ReresolveAddress();
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]云音乐地址重解析线程发生错误：{ex.Message}");
                    Console.WriteLine($"[TRACE]{ex.StackTrace}");
                }
            }
        });
        Task.Run(async () =>
        {
            Console.WriteLine("启动网易云歌词主线程");
            Console.WriteLine("等待花再设备...");
            while (!DeviceReady)
                await Task.Delay(500);
            if (DeviceReady && EnableCloudMusicLyrics)
            {
                Console.WriteLine("花再设备已就绪，显示启动信息");
                var startupGeneration = Interlocked.Read(ref _lyricSessionGeneration);
                ConfigureLyricTransition(true, sessionGeneration: startupGeneration);
                ExecuteAsLyricOwner(startupGeneration, () => TryShowLyricText("花再工具箱已启动~"));
                await Task.Delay(3000);
                Console.WriteLine("OK");
            }
            while (true)
            {
                _isClockUI = false;
                var time = 0;
                try
                {
                    if (DeviceReady && CloudMusicReady && EnableCloudMusicLyrics)
                    {
                        var sessionGeneration = Interlocked.Read(ref _lyricSessionGeneration);
                        Console.WriteLine("[DEBUG]设备均在线，准备进入主循环");
                        var lastRead = string.Empty;
                        var stalePreviousTrackLyric = string.Empty;
                        var lastTrackTitle = string.Empty;
                        var lastTrackArtist = string.Empty;
                        var wasPlaying = false;
                        (byte R, byte G, byte B) lastScreenColor = (0, 0, 0);
                        (byte R, byte G, byte B) lastAmbientColor = (0, 0, 0);
                        var lastAmbientLightEffect = CloudMusicLyricsProfile.SyncAmbientLightEffect;
                        var lastAmbientLightBrightness = CloudMusicLyricsProfile.SyncAmbientLightBrightness;
                        var lastAmbientLightSpeed = CloudMusicLyricsProfile.SyncAmbientLightSpeed;
                        while (true)
                        {
                            try
                            {
                                if (!DeviceReady || !CloudMusicReady || !EnableCloudMusicLyrics ||
                                    !LyricSessionCoordinator.IsOwner(LyricSourceKind.CloudMusic, sessionGeneration))
                                    break;

                                bool isPlaying = IsPlaying;
                                bool trackChanged = lastTrackTitle != CurrentTitle || lastTrackArtist != CurrentArtist;
                                if (trackChanged)
                                {
                                    if (!string.IsNullOrEmpty(lastRead))
                                        stalePreviousTrackLyric = lastRead;
                                    lastRead = string.Empty;
                                    ExecuteAsLyricOwner(sessionGeneration, Device.ClearLyricReplayCache);
                                }
                                var lyrics = string.Empty;
                                var lyricsRead = Reader.UsesTimedLyrics
                                    ? Reader.TryReadLyrics($"{CurrentTitle}\n{CurrentArtist}", out lyrics)
                                    : Reader.TryReadLyrics(out lyrics);
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
                                lastTrackTitle = CurrentTitle ?? string.Empty;
                                lastTrackArtist = CurrentArtist ?? string.Empty;

                                if (!_isClockUI)
                                    ConfigureLyricTransition(
                                        true,
                                        sessionGeneration: sessionGeneration);

                                var albumColor = CurrentAlbumColor;

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

                                var ambientEffect = CloudMusicLyricsProfile.SyncAmbientLightEffect;
                                var ambientBrightness = CloudMusicLyricsProfile.SyncAmbientLightBrightness;
                                var ambientSpeed = CloudMusicLyricsProfile.SyncAmbientLightSpeed;
                                bool colorChanged = lastScreenColor != screenColor || lastAmbientColor != ambientColor;
                                bool effectChanged = lastAmbientLightEffect != ambientEffect ||
                                                     lastAmbientLightBrightness != ambientBrightness ||
                                                     lastAmbientLightSpeed != ambientSpeed;

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
                                        Console.WriteLine(
                                            $"[INFO]已应用网易云氛围灯颜色：RGB({ambientColor.R}, {ambientColor.G}, {ambientColor.B}) / {ambientEffect} / {ambientBrightness} / {ambientSpeed}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[ERROR] SetAmbientLight failed: {ex.Message}");
                                    }

                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(screenColor.R, screenColor.G, screenColor.B)));
                                        Console.WriteLine($"[INFO]已应用网易云屏幕颜色：RGB({screenColor.R}, {screenColor.G}, {screenColor.B})");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[ERROR] SetPixelScreenColor failed: {ex.Message}");
                                    }
                                }

                                if (!_isClockUI && (lyricsChanged || _forceLyricRefresh))
                                {
                                    var lyricToSend = lyricsChanged ? lyrics : lastRead;
                                    if (!string.IsNullOrEmpty(lyricToSend))
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
                                                    await Task.Delay(500);
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
                                            await Task.Delay(200);
                                        }
                                    }
                                }
                                await Task.Delay(50);
                                time = isPlaying ? 0 : time + 50;
                                if (SwitchBackWhenPause && !isPlaying && !_isClockUI && time >= CloudMusicLyricsProfile.SwitchBackTimeout * 1000)
                                {
                                    _isClockUI = true;
                                    ConfigureLyricTransition(false, sessionGeneration: sessionGeneration);
                                    (byte R, byte G, byte B) finalScreenColor = ParseHexColor(CloudMusicLyricsProfile.DefaultScreenColor);
                                    (byte R, byte G, byte B) finalAmbientColor = ParseHexColor(CloudMusicLyricsProfile.DefaultAmbientLightColor);
                                    ExecuteAsLyricOwner(sessionGeneration, () => HidPacketBuilder.CurrentColor = finalScreenColor);
                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = CloudMusicLyricsProfile.DefaultAmbientLightEffect,
                                            Color = new Core.Models.Display.HaloPixelColor(finalAmbientColor.R, finalAmbientColor.G, finalAmbientColor.B),
                                            Brightness = CloudMusicLyricsProfile.DefaultAmbientLightBrightness,
                                            Speed = (byte)CloudMusicLyricsProfile.DefaultAmbientLightSpeed
                                        }));
                                    }
                                    catch {}
                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(finalScreenColor.R, finalScreenColor.G, finalScreenColor.B)));
                                    }
                                    catch {}
                                    ExecuteAsLyricOwner(sessionGeneration, () => Device.SetUIModel(CloudMusicLyricsProfile.DefaultHaloPixelUIModel));
                                    Console.WriteLine("已切换至时钟界面");
                                }

                                if (_isClockUI && _forcePauseLightRefresh)
                                {
                                    _forcePauseLightRefresh = false;
                                    (byte R, byte G, byte B) finalAmbientColor = ParseHexColor(CloudMusicLyricsProfile.DefaultAmbientLightColor);
                                    try
                                    {
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = CloudMusicLyricsProfile.DefaultAmbientLightEffect,
                                            Color = new Core.Models.Display.HaloPixelColor(finalAmbientColor.R, finalAmbientColor.G, finalAmbientColor.B),
                                            Brightness = CloudMusicLyricsProfile.DefaultAmbientLightBrightness,
                                            Speed = (byte)CloudMusicLyricsProfile.DefaultAmbientLightSpeed
                                        }));
                                    }
                                    catch {}
                                }

                                if (_isClockUI && _forcePauseScreenRefresh)
                                {
                                    _forcePauseScreenRefresh = false;
                                    (byte R, byte G, byte B) finalScreenColor = ParseHexColor(CloudMusicLyricsProfile.DefaultScreenColor);
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
                                        ExecuteAsLyricOwner(sessionGeneration, () => Device.SetUIModel(CloudMusicLyricsProfile.DefaultHaloPixelUIModel));
                                    }
                                    catch {}
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR]网易云歌词主循环发生错误：{ex.Message}");
                                Console.WriteLine($"[TRACE]{ex.StackTrace}");
                            }
                        }
                        ConfigureLyricTransition(false, sessionGeneration: sessionGeneration);
                        Console.WriteLine($"[DEBUG]主循环已退出");
                    }
                    Console.WriteLine($"[DEBUG]状态\t音响：{DeviceReady} 软件：{CloudMusicReady} 启用状态：{EnableCloudMusicLyrics}");
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]网易云歌词主线程发生错误：{ex.Message}");
                    Console.WriteLine($"[TRACE]{ex.StackTrace}");
                }
            }
        });
        Console.WriteLine("网易云后台线程启动完成");
    }

    private async Task<bool> TryUpdateAddressResolverAsync()
    {
        if (Reader.Version.Major == 0)
            return false;

        var version = Reader.Version.ToString(3);
        if (string.Equals(version, _lastResolverAttemptVersion, StringComparison.OrdinalIgnoreCase) &&
            DateTime.Now - _lastResolverAttemptTime < TimeSpan.FromMinutes(1))
            return Reader.ReresolveAddress();

        _lastResolverAttemptVersion = version;
        _lastResolverAttemptTime = DateTime.Now;
        var resolver = await AddressResolverProvider.GetAsync(version);
        if (resolver is null)
            return false;

        CloudMusicLyricsReader.SetAddressResolver(resolver);
        AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() => SupportedVersion = resolver.Version);
        return Reader.ReresolveAddress();
    }
}
