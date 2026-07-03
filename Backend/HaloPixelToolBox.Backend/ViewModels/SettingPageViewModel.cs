using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HaloPixelToolBox.Backend.Core.Utilities.Helpers;
using HaloPixelToolBox.Backend.Profiles.CrossVersionProfiles;
using Microsoft.Win32;
using XFEExtension.NetCore.FileExtension;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Backend.ViewModels;

public partial class SettingPageViewModel : ViewModelBase
{
    [ObservableProperty] public partial bool IsAutoStartEnable { get; set; } = SystemProfile.AutoStart;
    [ObservableProperty] public partial string AppCacheDirectory { get; set; } = AppPathHelper.AppCache;
    [ObservableProperty] public partial string AppCacheSize { get; set; } = FileHelper.GetDirectorySize(new(AppPathHelper.AppCache)).FileSize();
    [ObservableProperty] public partial string AppDataDirectory { get; set; } = AppPathHelper.AppLocalData;
    [ObservableProperty] public partial string AppDataSize { get; set; } = FileHelper.GetDirectorySize(new(AppPathHelper.AppLocalData)).FileSize();
    public ISettingService SettingService { get; set; } = ServiceManager.GetService<ISettingService>();
    public IDialogService DialogService { get; set; } = ServiceManager.GetService<IDialogService>();

    partial void OnIsAutoStartEnableChanged(bool value)
    {
        SystemProfile.AutoStart = value;
        SetAutoStart(value);
    }

    private static void SetAutoStart(bool enable) => SetAutoStart(enable, Assembly.GetExecutingAssembly().GetName().Name ?? "HaloPixelToolBox");

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
    private static void OpenPath(string originalPath) => Helper.OpenPath(originalPath);

    [RelayCommand]
    private async Task ClearCache()
    {
        if (await DialogService.ShowDialog("cleanCacheContentDialog") == ContentDialogResult.Primary)
        {
            Directory.Delete(AppPathHelper.AppCache, true);
            AppCacheSize = FileHelper.GetDirectorySize(new(AppPathHelper.AppCache)).FileSize();
        }
    }
}
