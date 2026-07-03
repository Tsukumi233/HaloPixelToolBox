using Microsoft.UI.Xaml.Navigation;
using LogViewPageViewModel = HaloPixelToolBox.Backend.ViewModels.LogViewPageViewModel;

namespace HaloPixelToolBox.Backend.Views;

/// <summary>
/// ��������־ҳ��
/// </summary>
public sealed partial class LogViewPage : Page
{
    public string? Parameter { get; set; }
    public static LogViewPage? Current { get; set; }
    public LogViewPageViewModel ViewModel { get; set; } = new();
    public LogViewPage()
    {
        Current = this;
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.AutoNavigationParameterService.OnParameterChange(e.Parameter);
    }
}