using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HaloPixelToolBox.Client.Interface.Services;
using HaloPixelToolBox.Client.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Client.Utilities;
using HaloPixelToolBox.Core.Utilities.Helpers;
using HaloPixelToolBox.Core.Utilities;
using Microsoft.Win32;
using System.Reflection;
using Windows.System;
using XFEExtension.NetCore.FileExtension;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Client.ViewModels;

public partial class SettingPageViewModel : ViewModelBase
{
    [ObservableProperty] public partial int CloseButtonActionIndex { get; set; } = SystemProfile.MinimizeWhenClose ? 0 : 1;
    [ObservableProperty] public partial int DefaultTabPageIndex { get; set; } = SystemProfile.DefaultPage == "SpotifyLyricsToolPage" ? 1 : 0;
    [ObservableProperty] public partial bool IsAutoStartEnable { get; set; } = SystemProfile.AutoStart;
    [ObservableProperty] public partial bool MinimizeWhenOpen { get; set; } = SystemProfile.MinimizeWhenOpen;
    [ObservableProperty] public partial string AppCacheDirectory { get; set; } = AppPathHelper.AppCache;
    [ObservableProperty] public partial string AppCacheSize { get; set; } = FileHelper.GetDirectorySize(new(AppPathHelper.AppCache)).FileSize();
    [ObservableProperty] public partial string AppDataDirectory { get; set; } = AppPathHelper.AppLocalData;
    [ObservableProperty] public partial string AppDataSize { get; set; } = FileHelper.GetDirectorySize(new(AppPathHelper.AppLocalData)).FileSize();
    [ObservableProperty] public partial string AppLogDirectory { get; set; } = AppPath.LogDictionary;
    [ObservableProperty] public partial string AppLogSize { get; set; } = FileHelper.GetDirectorySize(new(AppPath.LogDictionary)).FileSize();
    [ObservableProperty] public partial string CurrentVersion { get; set; } = Assembly.GetEntryAssembly()?.GetName().Version is Version version ? version.ToString(3) : "无法获取版本信息";
    [ObservableProperty] public partial string IgnoreVersion { get; set; } = SystemProfile.IgnoreVersion;
    [ObservableProperty] public partial string ServerAddress { get; set; } = SystemProfile.ServerAddress;
    public ISettingService SettingService { get; set; } = ServiceManager.GetService<ISettingService>();
    public IDialogService DialogService { get; set; } = ServiceManager.GetService<IDialogService>();

    partial void OnCloseButtonActionIndexChanged(int value) => SystemProfile.MinimizeWhenClose = value == 0;

    partial void OnDefaultTabPageIndexChanged(int value) => SystemProfile.DefaultPage = value == 1 ? "SpotifyLyricsToolPage" : "CloudMusicLyricsToolPage";

    partial void OnIsAutoStartEnableChanged(bool value)
    {
        SystemProfile.AutoStart = value;
        SetAutoStart(value);
    }

    partial void OnIgnoreVersionChanged(string value) => SystemProfile.IgnoreVersion = value;

    partial void OnServerAddressChanged(string value)
    {
        SystemProfile.ServerAddress = value.Trim();
        DataManager.Configure(SystemProfile.ServerAddress);
    }

    partial void OnMinimizeWhenOpenChanged(bool value) => SystemProfile.MinimizeWhenOpen = value;

    private static void SetAutoStart(bool enable) => SetAutoStart(enable, Assembly.GetExecutingAssembly().GetName().Name ?? "HaloPixelToolBox.Client");

    private static void SetAutoStart(bool enable, string appName, string exePath = "")
    {
        const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(runKey, true);
        if (enable)
        {
            if (exePath == string.Empty)
                exePath = Environment.ProcessPath ?? string.Empty;

            key?.SetValue(appName, $"\"{exePath}\"");
        }
        else
        {
            if (key?.GetValue(appName) != null)
            {
                key.DeleteValue(appName);
            }
        }
    }

    [RelayCommand]
    static void OpenPath(string originalPath) => Helper.OpenPath(originalPath);

    [RelayCommand]
    async Task ClearCache()
    {
        if (await DialogService.ShowDialog("cleanCacheContentDialog") == ContentDialogResult.Primary)
        {
            Directory.Delete(AppPathHelper.AppCache, true);
            AppCacheSize = FileHelper.GetDirectorySize(new(AppPathHelper.AppCache)).FileSize();
        }
    }

    [RelayCommand]
    static async Task LinkToGithubRepo() => await Launcher.LaunchUriAsync(new Uri("https://github.com/XFEstudio/HaloPixelToolBox.Client"));

    [RelayCommand]
    static async Task LinkToGithubIssue() => await Launcher.LaunchUriAsync(new Uri("https://github.com/XFEstudio/HaloPixelToolBox.Client/issues/new/choose"));

    [RelayCommand]
    void ClearIgnoreVersion() => IgnoreVersion = string.Empty;

    [RelayCommand]
    static async Task CheckUpgrade() => await ServiceManager.GetGlobalService<IUpgradeService>()!.CheckUpgrade();
}
