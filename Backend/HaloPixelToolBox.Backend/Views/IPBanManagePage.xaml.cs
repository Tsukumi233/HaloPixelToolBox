namespace HaloPixelToolBox.Backend.Views;

public sealed partial class IPBanManagePage : Page
{
    public IPBanManagePageViewModel ViewModel { get; } = new();

    public IPBanManagePage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();
}
