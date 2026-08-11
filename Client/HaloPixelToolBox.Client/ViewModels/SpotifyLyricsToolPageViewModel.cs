using CommunityToolkit.Mvvm.ComponentModel;
using HaloPixelToolBox.Client.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Client.Utilities;
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

    public HaloPixelDevice Device { get; set; } = new();
    public SpotifyLyricsReader Reader { get; set; }

    public ISettingService SettingService { get; } = ServiceManager.GetService<ISettingService>();

    private bool _forceRefresh;
    private bool _forcePauseLightRefresh;
    private bool _forcePauseScreenRefresh;
    private bool _forcePauseUIModelRefresh;
    private bool _isClockUI;

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
        _forceRefresh = true;
    }

    partial void OnEnableSpotifyLyricsChanged(bool value)
    {
        SpotifyLyricsProfile.EnableSpotifyLyrics = value;
        if (value && !_isClockUI)
            _forceRefresh = true;
    }
    partial void OnSwitchBackWhenPauseChanged(bool value) => SpotifyLyricsProfile.SwitchBackWhenPause = value;
    partial void OnSwitchBackTimeoutChanged(int value) => SpotifyLyricsProfile.SwitchBackTimeout = value;
    partial void OnEnableScreenColorSyncChanged(bool value)
    {
        SpotifyLyricsProfile.EnableScreenColorSync = value;
        if (!_isClockUI)
            _forceRefresh = true;
    }
    partial void OnEnableAmbientColorSyncChanged(bool value)
    {
        SpotifyLyricsProfile.EnableAmbientColorSync = value;
        if (!_isClockUI)
            _forceRefresh = true;
    }
    partial void OnSyncAmbientLightEffectChanged(Core.Models.Lighting.AmbientLightEffect value)
    {
        SpotifyLyricsProfile.SyncAmbientLightEffect = value;
        if (!_isClockUI)
            _forceRefresh = true;
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
            _forceRefresh = true;
    }
    partial void OnSyncAmbientLightSpeedChanged(int value)
    {
        SpotifyLyricsProfile.SyncAmbientLightSpeed = value;
        if (!_isClockUI)
            _forceRefresh = true;
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
        Console.WriteLine("初始化Spotify歌词读取器");
        Reader = new SpotifyLyricsReader();

        Console.WriteLine("准备启动Spotify后台线程");
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
                Console.WriteLine("正在搜索Spotify...");
                while (!SpotifyReady)
                {
                    var ready = Reader.Initialize();
                    AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() =>
                    {
                        SpotifyReady = ready;
                        SpotifyTrackInfo = !string.IsNullOrEmpty(Reader.CurrentTitle) ? $"{Reader.CurrentArtist} - {Reader.CurrentTitle}" : "未检测到播放中的歌曲";
                    });
                    await Task.Delay(500);
                }
                Console.WriteLine("Spotify已准备就绪");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR]搜索Spotify时发生错误：{ex.Message}");
                Console.WriteLine($"[TRACE]{ex.StackTrace}");
            }
        });

        Task.Run(async () =>
        {
            Console.WriteLine("启动Spotify歌词主线程");
            Console.WriteLine("等待花再设备...");
            while (!DeviceReady)
                await Task.Delay(500);

            if (DeviceReady && EnableSpotifyLyrics)
            {
                Console.WriteLine("花再设备已就绪，显示启动信息");
                Device.SetTextLayout(HaloPixelTextLayout.Center);
                Device.ShowText("Spotify歌词同步已就绪");
                await Task.Delay(3000);
            }

            while (true)
            {
                _isClockUI = false;
                int time = 0;
                try
                {
                    if (DeviceReady && SpotifyReady && EnableSpotifyLyrics)
                    {
                        Console.WriteLine("[DEBUG]设备均在线，准备进入主循环");
                        string lastRead = string.Empty;
                        string lastTrackTitle = string.Empty;
                        string lastTrackArtist = string.Empty;
                        bool scrolled = false;
                        bool wasPlaying = false;
                        (byte R, byte G, byte B) lastScreenColor = (0, 0, 0);
                        (byte R, byte G, byte B) lastAmbientColor = (0, 0, 0);
                        var lastAmbientLightEffect = SpotifyLyricsProfile.SyncAmbientLightEffect;
                        var lastAmbientLightBrightness = SpotifyLyricsProfile.SyncAmbientLightBrightness;
                        int lastAmbientLightSpeed = SpotifyLyricsProfile.SyncAmbientLightSpeed;
                        while (true)
                        {
                            try
                            {
                                if (!DeviceReady || !SpotifyReady || !EnableSpotifyLyrics)
                                    break;

                                bool isPlaying = Reader.IsPlaying;
                                bool trackChanged = lastTrackTitle != Reader.CurrentTitle || lastTrackArtist != Reader.CurrentArtist;
                                bool lyricsChanged = Reader.TryReadLyrics(out var lyrics) && lastRead != lyrics;

                                if (isPlaying && (_isClockUI || trackChanged))
                                {
                                    if (!wasPlaying && isPlaying)
                                    {
                                        _isClockUI = false;
                                        _forceRefresh = true;
                                        time = 0;
                                    }
                                    else if (lyricsChanged || trackChanged)
                                    {
                                        _isClockUI = false;
                                        _forceRefresh = true;
                                        time = 0;
                                    }
                                }
                                wasPlaying = isPlaying;
                                lastTrackTitle = Reader.CurrentTitle ?? string.Empty;
                                lastTrackArtist = Reader.CurrentArtist ?? string.Empty;

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

                                if (!_isClockUI && (colorChanged || effectChanged || _forceRefresh))
                                {
                                    lastScreenColor = screenColor;
                                    lastAmbientColor = ambientColor;
                                    lastAmbientLightEffect = ambientEffect;
                                    lastAmbientLightBrightness = ambientBrightness;
                                    lastAmbientLightSpeed = ambientSpeed;
                                    HidPacketBuilder.CurrentColor = screenColor;

                                    try
                                    {
                                        Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = ambientEffect,
                                            Color = new Core.Models.Display.HaloPixelColor(ambientColor.R, ambientColor.G, ambientColor.B),
                                            Brightness = ambientBrightness,
                                            Speed = (byte)ambientSpeed
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[ERROR] SetAmbientLight failed: {ex.Message}");
                                    }

                                    try
                                    {
                                        Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(screenColor.R, screenColor.G, screenColor.B));
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[ERROR] SetPixelScreenColor failed: {ex.Message}");
                                    }
                                }

                                if (!_isClockUI && (lyricsChanged || _forceRefresh))
                                {
                                    _forceRefresh = false;
                                    if (lyricsChanged)
                                    {
                                        Console.WriteLine($"已读取到歌词：{lyrics}");
                                        lastRead = lyrics;
                                        if (_isClockUI)
                                        {
                                            _isClockUI = false;
                                            _forceRefresh = true;
                                        }
                                        time = 0;
                                        if (scrolled)
                                        {
                                            Device.ShowText(string.Empty);
                                            await Task.Delay(100);
                                            scrolled = false;
                                        }
                                    }

                                    Device.SetTextLayout(SpotifyLyricsProfile.DefaultHaloPixelTextLayout);
                                    Device.ShowText(lastRead);

                                    if (lyricsChanged && lastRead.DisplayLength() > 30)
                                    {
                                        scrolled = true;
                                        await Task.Delay(500);
                                        Device.SetTextLayout(HaloPixelTextLayout.ScrollRightToLeft);
                                    }
                                }
                                await Task.Delay(50);
                                time = isPlaying ? 0 : time + 50;
                                if (SwitchBackWhenPause && !isPlaying && !_isClockUI && time >= SpotifyLyricsProfile.SwitchBackTimeout * 1000)
                                {
                                    _isClockUI = true;
                                    (byte R, byte G, byte B) finalScreenColor = ParseHexColor(SpotifyLyricsProfile.DefaultScreenColor);
                                    (byte R, byte G, byte B) finalAmbientColor = ParseHexColor(SpotifyLyricsProfile.DefaultAmbientLightColor);
                                    HidPacketBuilder.CurrentColor = finalScreenColor;
                                    try
                                    {
                                        Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = SpotifyLyricsProfile.DefaultAmbientLightEffect,
                                            Color = new Core.Models.Display.HaloPixelColor(finalAmbientColor.R, finalAmbientColor.G, finalAmbientColor.B),
                                            Brightness = SpotifyLyricsProfile.DefaultAmbientLightBrightness,
                                            Speed = (byte)SpotifyLyricsProfile.DefaultAmbientLightSpeed
                                        });
                                    }
                                    catch {}
                                    try
                                    {
                                        Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(finalScreenColor.R, finalScreenColor.G, finalScreenColor.B));
                                    }
                                    catch {}
                                    Device.SetUIModel(SpotifyLyricsProfile.DefaultHaloPixelUIModel);
                                    Console.WriteLine("已切换至时钟界面");
                                }

                                if (_isClockUI && _forcePauseLightRefresh)
                                {
                                    _forcePauseLightRefresh = false;
                                    (byte R, byte G, byte B) finalAmbientColor = ParseHexColor(SpotifyLyricsProfile.DefaultAmbientLightColor);
                                    try
                                    {
                                        Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = SpotifyLyricsProfile.DefaultAmbientLightEffect,
                                            Color = new Core.Models.Display.HaloPixelColor(finalAmbientColor.R, finalAmbientColor.G, finalAmbientColor.B),
                                            Brightness = SpotifyLyricsProfile.DefaultAmbientLightBrightness,
                                            Speed = (byte)SpotifyLyricsProfile.DefaultAmbientLightSpeed
                                        });
                                    }
                                    catch {}
                                }

                                if (_isClockUI && _forcePauseScreenRefresh)
                                {
                                    _forcePauseScreenRefresh = false;
                                    (byte R, byte G, byte B) finalScreenColor = ParseHexColor(SpotifyLyricsProfile.DefaultScreenColor);
                                    HidPacketBuilder.CurrentColor = finalScreenColor;
                                    try
                                    {
                                        Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(finalScreenColor.R, finalScreenColor.G, finalScreenColor.B));
                                    }
                                    catch {}
                                }

                                if (_isClockUI && _forcePauseUIModelRefresh)
                                {
                                    _forcePauseUIModelRefresh = false;
                                    try
                                    {
                                        Device.SetUIModel(SpotifyLyricsProfile.DefaultHaloPixelUIModel);
                                    }
                                    catch {}
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR]Spotify歌词主循环发生错误：{ex.Message}");
                                Console.WriteLine($"[TRACE]{ex.StackTrace}");
                            }
                        }
                    }
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]Spotify歌词主线程发生错误：{ex.Message}");
                    Console.WriteLine($"[TRACE]{ex.StackTrace}");
                }
            }
        });
        Console.WriteLine("Spotify后台线程启动完成");
    }
}
