using CommunityToolkit.Mvvm.ComponentModel;
using HaloPixelToolBox.Client.Interface.Services;
using HaloPixelToolBox.Client.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Client.Utilities.Helpers;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;

namespace HaloPixelToolBox.Client.ViewModels;

public partial class AppShellPageViewModel : ViewModelBase, IDisposable
{
    private readonly CancellationTokenSource _updateCancellation = new();
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _updateCancellation.Cancel();
        _updateCancellation.Dispose();
        NavigationViewService.NavigationService.Navigated -= NavigationService_Navigated;
        PageService.CurrentPageLoaded -= CurrentPage_Loaded;
        if (PageService is IDisposable pageService) pageService.Dispose();
        if (CloseWindowService is not null)
            CloseWindowService.Closed -= CloseWindowService_Closed;
    }
    [ObservableProperty] public partial int SelectedIndex { get; set; }
    [ObservableProperty] public partial bool NeverAskAgainWhenClose { get; set; }
    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial string UpgradeContentText { get; set; } = string.Empty;
    [ObservableProperty] public partial string UserName { get; set; } = Environment.UserName;
    [ObservableProperty] public partial ImageSource UserTile { get; set; } = Win32Helper.GetUserTile();

    public IDialogService DialogService { get; } = ServiceManager.GetService<IDialogService>();
    public INavigationViewService NavigationViewService { get; } = ServiceManager.GetService<INavigationViewService>();
    public Interface.Services.IPageService PageService { get; } = ServiceManager.GetService<Interface.Services.IPageService>();
    public IMessageService MessageService { get; } = ServiceManager.GetService<IMessageService>();
    public ILoadingService LoadingService { get; } = ServiceManager.GetService<ILoadingService>();
    public IUpgradeService UpgradeService { get; } = ServiceManager.GetService<IUpgradeService>();
    public ICloseWindowService? CloseWindowService { get; } = ServiceManager.GetGlobalService<ICloseWindowService>();

    public AppShellPageViewModel()
    {
        NavigationViewService.NavigationService.Navigated += NavigationService_Navigated;
        PageService.CurrentPageLoaded += CurrentPage_Loaded;
        CloseWindowService?.Closed += CloseWindowService_Closed;
        UpgradeService.Initialize(async () =>
        {
            if (_disposed) return;
            try
            {
                MessageService.ShowMessage("正在检查更新...", "检查更新", InfoBarSeverity.Informational);
                Console.WriteLine("正在检查更新...");
                var upgradeInfo = await UpgradeHelper.GetReleaseNotes(_updateCancellation.Token);
                if (_disposed) return;
                if (upgradeInfo == null)
                {
                    MessageService.ShowMessage("检查更新失败，请稍后重试", "检查更新", InfoBarSeverity.Error);
                    Console.WriteLine("[ERROR]获取更新信息失败");
                    return;
                }
                if (upgradeInfo.IsLatest)
                {
                    MessageService.ShowMessage("当前已是最新版本", "检查更新", InfoBarSeverity.Success);
                    Console.WriteLine("当前已是最新版本");
                }
                else
                {
                    if (upgradeInfo.LatestVersion == SystemProfile.IgnoreVersion)
                    {
                        MessageService.ShowMessage("当前版本已被忽略", "检查更新", InfoBarSeverity.Informational);
                        Console.WriteLine("当前版本已被忽略");
                    }
                    else
                    {
                        MessageService.ShowMessage("检测到新版本", "检查更新", InfoBarSeverity.Informational);
                        Console.WriteLine("检测到新版本");
                        Console.WriteLine($"[DEBUG]最新版本: {upgradeInfo.LatestVersion}");
                        Console.WriteLine($"[DEBUG]当前版本: {UpgradeHelper.Version}");
                        Console.WriteLine($"[DEBUG]更新信息：{upgradeInfo.ReleaseNotes}");
                        UpgradeContentText = upgradeInfo.ReleaseNotes;
                        switch (await DialogService.ShowDialog("upgradeDialog"))
                        {
                            case ContentDialogResult.None:
                                Console.WriteLine("用户取消了更新");
                                break;
                            case ContentDialogResult.Primary:
                                if (_disposed) return;
                                Console.WriteLine("用户选择打开 Releases 页面...");
                                UpgradeHelper.OpenReleases();
                                break;
                            case ContentDialogResult.Secondary:
                                Console.WriteLine("用户选择忽略当前版本");
                                SystemProfile.IgnoreVersion = upgradeInfo.LatestVersion;
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR]检查更新时发生错误：{ex}");
            }
        });
    }

    private async void CloseWindowService_Closed(object sender, WindowEventArgs args)
    {
        args.Handled = true;
        NeverAskAgainWhenClose = SystemProfile.NeverAskAgainWhenClose;
        SelectedIndex = SystemProfile.MinimizeWhenClose ? 0 : 1;
        if (!NeverAskAgainWhenClose)
        {
            if (await DialogService.ShowDialog("closeDialog") == ContentDialogResult.Primary)
            {
                SystemProfile.NeverAskAgainWhenClose = NeverAskAgainWhenClose;
                SystemProfile.MinimizeWhenClose = SelectedIndex == 0;
            }
            else
            {
                return;
            }
        }
        if (SystemProfile.MinimizeWhenClose)
        {
            Win32Helper.ShowWindow(WindowHelper.GetHwndForCurrentWindow(), Win32Helper.SW_HIDE);
        }
        else
        {
            await ((App)Application.Current).ShutdownAsync();
        }
    }

    private async void CurrentPage_Loaded(object sender, RoutedEventArgs e)
    {
        await UpgradeService.CheckUpgrade();
    }

    private void NavigationService_Navigated(object? sender, NavigationEventArgs e) => CanGoBack = NavigationViewService.NavigationService.CanGoBack;
}
