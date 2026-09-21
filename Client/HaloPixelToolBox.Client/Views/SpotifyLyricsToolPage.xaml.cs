using HaloPixelToolBox.Client.ViewModels;
using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Models.Lighting;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Client.Views;

public sealed partial class SpotifyLyricsToolPage : Page
{
    public static SpotifyLyricsToolPage? Current { get; set; }
    public SpotifyLyricsToolPageViewModel ViewModel => App.SpotifyLyricsViewModel;

    public SpotifyLyricsToolPage()
    {
        Console.WriteLine("正在初始化Spotify歌词界面...");
        Current = this;
        InitializeComponent();
        ViewModel.AutoNavigationParameterService.Initialize(this);
        ViewModel.SettingService.AddComboBox(lyricDisplayProtocolComboBox, ProfileHelper.GetEnumProfileSaveFunc<LyricDisplayProtocol>(), ProfileHelper.GetEnumProfileLoadFuncForComboBox());
        ViewModel.SettingService.AddComboBox(lyricTransitionPresetComboBox, ProfileHelper.GetEnumProfileSaveFunc<LyricTransitionPreset>(), ProfileHelper.GetEnumProfileLoadFuncForComboBox());
        ViewModel.SettingService.AddComboBox(defaultHaloPixelTextLayoutComboBox, ProfileHelper.GetEnumProfileSaveFunc<HaloPixelTextLayout>(), ProfileHelper.GetEnumProfileLoadFuncForComboBox());
        ViewModel.SettingService.AddComboBox(syncAmbientLightEffectComboBox, ProfileHelper.GetEnumProfileSaveFunc<AmbientLightEffect>(), ProfileHelper.GetEnumProfileLoadFuncForComboBox());
        ViewModel.SettingService.Initialize();
        ViewModel.SettingService.RegisterEvents();

        lyricDisplayProtocolComboBox.SelectionChanged += (s, e) =>
        {
            if (lyricDisplayProtocolComboBox.SelectedItem is ComboBoxItem item &&
                Enum.TryParse<LyricDisplayProtocol>(item.Tag?.ToString(), out var protocol))
            {
                ViewModel.DisplayProtocol = protocol;
            }
        };

        lyricTransitionPresetComboBox.SelectionChanged += (s, e) =>
        {
            if (lyricTransitionPresetComboBox.SelectedItem is ComboBoxItem item &&
                Enum.TryParse<LyricTransitionPreset>(item.Tag?.ToString(), out var preset))
            {
                ViewModel.LyricTransitionPreset = preset;
            }
        };

        // Propagate ComboBox selection changes to ViewModel properties
        syncAmbientLightEffectComboBox.SelectionChanged += (s, e) =>
        {
            if (syncAmbientLightEffectComboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<AmbientLightEffect>(item.Tag?.ToString(), out var effect))
            {
                ViewModel.SyncAmbientLightEffect = effect;
            }
        };

        // Initialize brightness radio buttons
        switch (ViewModel.SyncAmbientLightBrightness)
        {
            case AmbientLightBrightness.Low:
                brightnessLowRadio.IsChecked = true;
                break;
            case AmbientLightBrightness.Medium:
                brightnessMediumRadio.IsChecked = true;
                break;
            case AmbientLightBrightness.High:
                brightnessHighRadio.IsChecked = true;
                break;
        }

        NavigationCacheMode = NavigationCacheMode.Required;
        Console.WriteLine("Spotify歌词界面初始化完成");
    }

    private void OnBrightnessRadioChecked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, brightnessLowRadio))
            ViewModel.SyncAmbientLightBrightness = AmbientLightBrightness.Low;
        else if (ReferenceEquals(sender, brightnessMediumRadio))
            ViewModel.SyncAmbientLightBrightness = AmbientLightBrightness.Medium;
        else if (ReferenceEquals(sender, brightnessHighRadio))
            ViewModel.SyncAmbientLightBrightness = AmbientLightBrightness.High;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Console.WriteLine("导航到Spotify歌词页面");
        ViewModel.AutoNavigationParameterService.Initialize(this);
        ViewModel.AutoNavigationParameterService.OnParameterChange(e.Parameter);
        ViewModel.OnNavigatedTo();
    }
}
