using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;

namespace HaloPixelToolBox.Backend.ViewModels;

public partial class MainPageViewModel : ViewModelBase
{
    [ObservableProperty] public partial int ClickCount { get; set; }
    public IAutoNavigationParameterService<string> AutoNavigationParameterService { get; set; } = ServiceManager.GetService<IAutoNavigationParameterService<string>>();
    public INavigationService? NavigationService { get; } = ServiceManager.GetGlobalService<INavigationService>();
    public IMessageService? MessageService { get; } = ServiceManager.GetGlobalService<IMessageService>();

    public MainPageViewModel()
    {
        AutoNavigationParameterService.ParameterChange += AutoNavigationParameterService_ParameterChange;
    }

    private void AutoNavigationParameterService_ParameterChange(object? sender, string? e)
    {
        MessageService?.ShowMessage($"Parameter: {e}", "Focus", InfoBarSeverity.Warning);
        Console.WriteLine($"切换至主页，参数: {e}");
    }

    [RelayCommand]
    private void IncrementClickCount()
    {
        ClickCount++;
        MessageService?.ShowMessage($"Click count: {ClickCount}", "Info");
        Console.WriteLine($"点击次数: {ClickCount}");
    }
}
