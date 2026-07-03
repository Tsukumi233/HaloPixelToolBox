using System.Windows.Forms;
using Windows.Graphics;
using HaloPixelToolBox.Client.ViewModels;

namespace HaloPixelToolBox.Client.Views;

/// <summary>
/// 菜单页面
/// </summary>
public sealed partial class TrayMenuPage : Page
{
    public Window MenuWindow { get; set; }
    public TrayMenuPageViewModel ViewModel { get; set; }
    public static TrayMenuPage? Current { get; set; }

    public TrayMenuPage(Window window)
    {
        Current = this;
        MenuWindow = window;
        ViewModel = new(window);
        InitializeComponent();
        ViewModel.AutoNavigationParameterService.Initialize(this);
    }

    void ResizeToContent()
    {
        var width = panel.ActualWidth;
        var height = panel.ActualHeight;

        if (width <= 0 || height <= 0)
            return;

        var scale = Content.XamlRoot.RasterizationScale;

        var w = (int)Math.Ceiling(width * scale);
        var h = (int)Math.Ceiling(height * scale);

        MenuWindow.AppWindow.Resize(new SizeInt32(w, h));
        MenuWindow.AppWindow.Move(new PointInt32(Cursor.Position.X + 5, Cursor.Position.Y - h + 10));
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(ResizeToContent);
        ViewModel.AutoNavigationParameterService.OnParameterChange(string.Empty);
    }
}
