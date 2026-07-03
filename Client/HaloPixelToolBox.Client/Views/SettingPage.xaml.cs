using HaloPixelToolBox.Client.ViewModels;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Client.Views;

/// <summary>
/// 设置页面
/// </summary>
public sealed partial class SettingPage : Page
{
    public static SettingPage? Current { get; set; }
    public SettingPageViewModel ViewModel { get; set; } = new();
    public SettingPage()
    {
        Current = this;
        this.InitializeComponent();
        ViewModel.DialogService.RegisterDialog(cleanCacheContentDialog);
        ViewModel.SettingService.AddComboBox(appThemeComboBox, ProfileHelper.GetEnumProfileSaveFunc<ElementTheme>(), ProfileHelper.GetEnumProfileLoadFuncForComboBox());
        ViewModel.SettingService.Initialize();
        ViewModel.SettingService.RegisterEvents();
    }
}