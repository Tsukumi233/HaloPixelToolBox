using CommunityToolkit.Mvvm.ComponentModel;
using HaloPixelToolBox.Client.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Client.Utilities;
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
    [ObservableProperty] public partial bool SwitchBackWhenPause { get; set; } = CloudMusicLyricsProfile.SwitchBackWhenPause;
    [ObservableProperty] public partial bool UseInputedAddress { get; set; } = CloudMusicLyricsProfile.UseInputedAddress;
    [ObservableProperty] public partial string InputedAddress { get; set; } = CloudMusicLyricsProfile.InputedAddress;
    [ObservableProperty] public partial int SwitchBackTimeout { get; set; } = CloudMusicLyricsProfile.SwitchBackTimeout;
    [ObservableProperty] public partial string CloudMusicVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupportedVersion { get; set; } = "等待检测";
    public HaloPixelDevice Device { get; set; } = new();
    public CloudMusicLyricsReader Reader { get; set; }

    public ISettingService SettingService { get; } = ServiceManager.GetService<ISettingService>();

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

    partial void OnEnableCloudMusicLyricsChanged(bool value) => CloudMusicLyricsProfile.EnableCloudMusicLyrics = value;

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

    public CloudMusicLyricsToolPageViewModel()
    {
        Console.WriteLine("初始化网易云歌词读取器");
        AddressResolverProvider.LoadCachedResolvers();
        SupportedVersion = CloudMusicLyricsReader.VersionResolverDictionary.Keys
            .OrderByDescending(static version => Version.TryParse(version, out var parsed) ? parsed : new Version())
            .FirstOrDefault() ?? "由服务器动态获取";
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
            if (DeviceReady)
            {
                Console.WriteLine("花再设备已就绪，显示启动信息");
                Device.SetTextLayout(HaloPixelTextLayout.Center);
                Device.ShowText("花再工具箱已启动~");
                await Task.Delay(3000);
                Console.WriteLine("OK");
            }
            while (true)
            {
                var isClockUI = false;
                var time = 0;
                try
                {
                    if (DeviceReady && CloudMusicReady && EnableCloudMusicLyrics)
                    {
                        Console.WriteLine("[DEBUG]设备均在线，准备进入主循环");
                        var lastRead = string.Empty;
                        var scrolled = false;
                        while (true)
                        {
                            try
                            {
                                if (!DeviceReady || !CloudMusicReady || !EnableCloudMusicLyrics)
                                    break;
                                if (Reader.TryReadLyrics(out var lyrics) && lastRead != lyrics)
                                {
                                    Console.WriteLine($"已读取到歌词：{lyrics}");
                                    lastRead = lyrics;
                                    isClockUI = false;
                                    time = 0;
                                    if (scrolled)
                                    {
                                        Device.ShowText(string.Empty);
                                        await Task.Delay(100);
                                        scrolled = false;
                                    }
                                    Device.SetTextLayout(CloudMusicLyricsProfile.DefaultHaloPixelTextLayout);
                                    Device.ShowText(lyrics);
                                    Debug.WriteLine(lyrics.DisplayLength());
                                    if (lyrics.DisplayLength() > 30)
                                    {
                                        scrolled = true;
                                        await Task.Delay(500);
                                        Device.SetTextLayout(HaloPixelTextLayout.ScrollRightToLeft);
                                    }
                                }
                                await Task.Delay(50);
                                time += 50;
                                if (!isClockUI && time >= CloudMusicLyricsProfile.SwitchBackTimeout * 1000)
                                {
                                    isClockUI = true;
                                    Device.SetUIModel(CloudMusicLyricsProfile.DefaultHaloPixelUIModel);
                                    Console.WriteLine("已切换至时钟界面");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR]网易云歌词主循环发生错误：{ex.Message}");
                                Console.WriteLine($"[TRACE]{ex.StackTrace}");
                            }
                        }
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
