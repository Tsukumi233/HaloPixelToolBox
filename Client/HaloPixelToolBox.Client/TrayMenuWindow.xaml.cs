using HaloPixelToolBox.Client.Views;

namespace HaloPixelToolBox.Client;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class TrayMenuWindow : Window
{
    public TrayMenuWindow()
    {
        InitializeComponent();
        Content = new TrayMenuPage(this);
    }
}
