using HaloPixelToolBox.Backend.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Backend.Utilities;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;
using XFEExtension.NetCore.XFEConsole;
using AppShellPage = HaloPixelToolBox.Backend.Views.AppShellPage;
using LogViewPage = HaloPixelToolBox.Backend.Views.LogViewPage;
using MainPage = HaloPixelToolBox.Backend.Views.MainPage;
using SettingPage = HaloPixelToolBox.Backend.Views.SettingPage;
using UnhandledExceptionEventArgs = System.UnhandledExceptionEventArgs;

namespace HaloPixelToolBox.Backend;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 主页窗口
    /// </summary>
    public static MainWindow MainWindow { get; set; } = new();

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        XFEConsole.UseXFEConsoleLog();
        XFEConsole.Log.LogPath = $@"{AppPath.LogDictionary}\{DateTime.Now:yyyy-M-d_HH-mm-ss}.log";
        Console.WriteLine("正在启动应用程序...");
        this.InitializeComponent();
        AppThemeHelper.Theme = SystemProfile.Theme;
        PageManager.RegisterPage(typeof(AppShellPage));
        PageManager.RegisterPage(typeof(MainPage));
        PageManager.RegisterPage(typeof(LogViewPage));
        PageManager.RegisterPage(typeof(AddressResolveManagePage));
        PageManager.RegisterPage(typeof(IPBanManagePage));
        PageManager.RegisterPage(typeof(SettingPage));
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception ex) return;
        Console.WriteLine($"[ERROR]应用程序发生错误:{ex.Message}");
        Console.WriteLine($"[TRACE]{ex.StackTrace}");
        if (ServiceManager.GetService<IMessageService>() is not { } messageService) return;
        messageService.ShowMessage(ex.Message, "发生错误", InfoBarSeverity.Error);
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Console.WriteLine($"[ERROR]应用程序发生错误:{e.Message}");
        Console.WriteLine($"[TRACE]{e.Exception.StackTrace}");
        if (ServiceManager.GetService<IMessageService>() is not { } messageService) return;
        messageService.ShowMessage(e.Message, "发生错误", InfoBarSeverity.Error);
        e.Handled = true;
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow.Content = new AppShellPage();
        MainWindow.Activate();
        AppThemeHelper.MainWindow = MainWindow;
        Console.WriteLine("应用程序已启动");
    }
}
