using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;

namespace HaloPixelToolBox.Client.ViewModels;

public partial class MainPageViewModel : ViewModelBase
{
    public IAutoNavigationParameterService<string> AutoNavigationParameterService { get; set; } = ServiceManager.GetService<IAutoNavigationParameterService<string>>();
    public INavigationService? NavigationService { get; } = ServiceManager.GetGlobalService<INavigationService>();
    public IMessageService? MessageService { get; } = ServiceManager.GetGlobalService<IMessageService>();

    public MainPageViewModel()
    {
        AutoNavigationParameterService.ParameterChange += AutoNavigationParameterService_ParameterChange;
    }

    private void AutoNavigationParameterService_ParameterChange(object? sender, string? e)
    {
    }
}
