using CommunityToolkit.Mvvm.ComponentModel;
using HaloPixelToolBox.Client.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Client.Views;
using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Models.Lighting;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Implements;
using XFEExtension.NetCore.WinUIHelper.Utilities;
using System;

namespace HaloPixelToolBox.Client.ViewModels;

public partial class DefaultSettingsPageViewModel : ServiceBaseViewModelBase<string>
{
    [ObservableProperty]
    private bool switchBackWhenPause = SpotifyLyricsProfile.SwitchBackWhenPause;

    [ObservableProperty]
    private int switchBackTimeout = SpotifyLyricsProfile.SwitchBackTimeout;

    [ObservableProperty]
    private HaloPixelUIModel defaultHaloPixelUIModel = SpotifyLyricsProfile.DefaultHaloPixelUIModel;

    [ObservableProperty]
    private AmbientLightEffect defaultAmbientLightEffect = SpotifyLyricsProfile.DefaultAmbientLightEffect;

    [ObservableProperty]
    private AmbientLightBrightness defaultAmbientLightBrightness = SpotifyLyricsProfile.DefaultAmbientLightBrightness;

    [ObservableProperty]
    private int defaultAmbientLightSpeed = SpotifyLyricsProfile.DefaultAmbientLightSpeed;

    [ObservableProperty]
    private string defaultAmbientLightColor = SpotifyLyricsProfile.DefaultAmbientLightColor;

    [ObservableProperty]
    private string defaultScreenColor = SpotifyLyricsProfile.DefaultScreenColor;

    public ISettingService SettingService { get; } = ServiceManager.GetService<ISettingService>();

    partial void OnSwitchBackWhenPauseChanged(bool value)
    {
        SpotifyLyricsProfile.SwitchBackWhenPause = value;
        CloudMusicLyricsProfile.SwitchBackWhenPause = value;
    }

    partial void OnSwitchBackTimeoutChanged(int value)
    {
        SpotifyLyricsProfile.SwitchBackTimeout = value;
        CloudMusicLyricsProfile.SwitchBackTimeout = value;
    }

    partial void OnDefaultHaloPixelUIModelChanged(HaloPixelUIModel value)
    {
        SpotifyLyricsProfile.DefaultHaloPixelUIModel = value;
        CloudMusicLyricsProfile.DefaultHaloPixelUIModel = value;
        SpotifyLyricsToolPage.Current?.ViewModel?.ForcePauseUIModelRefresh();
        CloudMusicLyricsToolPage.Current?.ViewModel?.ForcePauseUIModelRefresh();
    }

    partial void OnDefaultAmbientLightEffectChanged(AmbientLightEffect value)
    {
        SpotifyLyricsProfile.DefaultAmbientLightEffect = value;
        CloudMusicLyricsProfile.DefaultAmbientLightEffect = value;
        SpotifyLyricsToolPage.Current?.ViewModel?.ForcePauseLightRefresh();
        CloudMusicLyricsToolPage.Current?.ViewModel?.ForcePauseLightRefresh();
    }

    partial void OnDefaultAmbientLightBrightnessChanged(AmbientLightBrightness value)
    {
        SpotifyLyricsProfile.DefaultAmbientLightBrightness = value;
        CloudMusicLyricsProfile.DefaultAmbientLightBrightness = value;
        OnPropertyChanged(nameof(DefaultAmbientLightBrightnessIndex));
        SpotifyLyricsToolPage.Current?.ViewModel?.ForcePauseLightRefresh();
        CloudMusicLyricsToolPage.Current?.ViewModel?.ForcePauseLightRefresh();
    }

    partial void OnDefaultAmbientLightSpeedChanged(int value)
    {
        SpotifyLyricsProfile.DefaultAmbientLightSpeed = value;
        CloudMusicLyricsProfile.DefaultAmbientLightSpeed = value;
        SpotifyLyricsToolPage.Current?.ViewModel?.ForcePauseLightRefresh();
        CloudMusicLyricsToolPage.Current?.ViewModel?.ForcePauseLightRefresh();
    }

    partial void OnDefaultAmbientLightColorChanged(string value)
    {
        SpotifyLyricsProfile.DefaultAmbientLightColor = value;
        CloudMusicLyricsProfile.DefaultAmbientLightColor = value;
        SpotifyLyricsToolPage.Current?.ViewModel?.ForcePauseLightRefresh();
        CloudMusicLyricsToolPage.Current?.ViewModel?.ForcePauseLightRefresh();
    }

    partial void OnDefaultScreenColorChanged(string value)
    {
        SpotifyLyricsProfile.DefaultScreenColor = value;
        CloudMusicLyricsProfile.DefaultScreenColor = value;
        SpotifyLyricsToolPage.Current?.ViewModel?.ForcePauseScreenRefresh();
        CloudMusicLyricsToolPage.Current?.ViewModel?.ForcePauseScreenRefresh();
    }

    public int DefaultAmbientLightBrightnessIndex
    {
        get => (int)DefaultAmbientLightBrightness - 1;
        set
        {
            if (value >= 0 && value <= 2)
            {
                DefaultAmbientLightBrightness = (AmbientLightBrightness)(value + 1);
            }
        }
    }

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
}
