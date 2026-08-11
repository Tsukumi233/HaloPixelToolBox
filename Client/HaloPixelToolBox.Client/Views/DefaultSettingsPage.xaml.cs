using HaloPixelToolBox.Client.ViewModels;
using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Models.Lighting;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Client.Views
{
    public sealed partial class DefaultSettingsPage : Page
    {
        public static DefaultSettingsPage? Current { get; set; }
        public DefaultSettingsPageViewModel ViewModel { get; set; } = new();

        public DefaultSettingsPage()
        {
            Current = this;
            this.InitializeComponent();

            // Register comboboxes in SettingService
            ViewModel.SettingService.AddComboBox(defaultHaloPixelUIModelComboBox, ProfileHelper.GetEnumProfileSaveFunc<HaloPixelUIModel>(), ProfileHelper.GetEnumProfileLoadFuncForComboBox());
            ViewModel.SettingService.AddComboBox(defaultAmbientLightEffectComboBox, ProfileHelper.GetEnumProfileSaveFunc<AmbientLightEffect>(), ProfileHelper.GetEnumProfileLoadFuncForComboBox());

            // Wire selections back to VM immediately
            defaultHaloPixelUIModelComboBox.SelectionChanged += (s, e) =>
            {
                if (defaultHaloPixelUIModelComboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<HaloPixelUIModel>(item.Tag?.ToString(), out var model))
                {
                    ViewModel.DefaultHaloPixelUIModel = model;
                }
            };

            defaultAmbientLightEffectComboBox.SelectionChanged += (s, e) =>
            {
                if (defaultAmbientLightEffectComboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<AmbientLightEffect>(item.Tag?.ToString(), out var effect))
                {
                    ViewModel.DefaultAmbientLightEffect = effect;
                }
            };

            ViewModel.SettingService.Initialize();
            ViewModel.SettingService.RegisterEvents();

            // Initialize default brightness radio buttons
            switch (ViewModel.DefaultAmbientLightBrightness)
            {
                case AmbientLightBrightness.Low:
                    defaultBrightnessLowRadio.IsChecked = true;
                    break;
                case AmbientLightBrightness.Medium:
                    defaultBrightnessMediumRadio.IsChecked = true;
                    break;
                case AmbientLightBrightness.High:
                    defaultBrightnessHighRadio.IsChecked = true;
                    break;
            }

            // Initialize color pickers and indicators
            try
            {
                var defaultAmbientColor = DefaultSettingsPageViewModel.ParseHexColor(ViewModel.DefaultAmbientLightColor);
                var defaultAmbientUIColor = Color.FromArgb(255, defaultAmbientColor.R, defaultAmbientColor.G, defaultAmbientColor.B);
                defaultAmbientColorPicker.Color = defaultAmbientUIColor;
                defaultAmbientColorIndicator.Background = new SolidColorBrush(defaultAmbientUIColor);

                var defaultScreenColor = DefaultSettingsPageViewModel.ParseHexColor(ViewModel.DefaultScreenColor);
                var defaultScreenUIColor = Color.FromArgb(255, defaultScreenColor.R, defaultScreenColor.G, defaultScreenColor.B);
                defaultScreenColorPicker.Color = defaultScreenUIColor;
                defaultScreenColorIndicator.Background = new SolidColorBrush(defaultScreenUIColor);
            }
            catch { }
        }

        private void OnDefaultBrightnessRadioChecked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, defaultBrightnessLowRadio))
                ViewModel.DefaultAmbientLightBrightness = AmbientLightBrightness.Low;
            else if (ReferenceEquals(sender, defaultBrightnessMediumRadio))
                ViewModel.DefaultAmbientLightBrightness = AmbientLightBrightness.Medium;
            else if (ReferenceEquals(sender, defaultBrightnessHighRadio))
                ViewModel.DefaultAmbientLightBrightness = AmbientLightBrightness.High;
        }

        private void OnDefaultAmbientColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            defaultAmbientColorIndicator.Background = new SolidColorBrush(args.NewColor);
            ViewModel.DefaultAmbientLightColor = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
        }

        private void OnDefaultScreenColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            defaultScreenColorIndicator.Background = new SolidColorBrush(args.NewColor);
            ViewModel.DefaultScreenColor = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
        }
    }
}
