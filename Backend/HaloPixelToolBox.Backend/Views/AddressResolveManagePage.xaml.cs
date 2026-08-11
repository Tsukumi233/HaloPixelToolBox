namespace HaloPixelToolBox.Backend.Views;

/// <summary>
/// 地址解析管理页面
/// </summary>
public sealed partial class AddressResolveManagePage : Page
{
    public static AddressResolveManagePage? Current { get; set; }
    public AddressResolveManagePageViewModel ViewModel { get; set; } = new();
    public AddressResolveManagePage()
    {
        Current = this;
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();
}
