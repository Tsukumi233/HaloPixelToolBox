using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using System.Drawing;
using System.Windows.Forms;
using HaloPixelToolBox.Client.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Implements.Services;

namespace HaloPixelToolBox.Client.Implements.Services;

public partial class TrayIconService : GlobalServiceBase, ITrayIconService
{
    private readonly NotifyIcon _notifyIcon;
    private DispatcherQueue? _dispatcher;
    private bool _disposed;

    public TrayIconService()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "花再音响工具箱",
            Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "appicon.ico")), // 一定要是 .ico
            Visible = true
        };

        _notifyIcon.MouseClick += OnMouseClick;
        _notifyIcon.Visible = true;
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (_disposed) return;
        if (e.Button == MouseButtons.Right)
            ShowTrayMenu();
        else
            ShowWindow();
    }

    public void Initilize(DispatcherQueue dispatcherQueue)
    {
        _dispatcher = dispatcherQueue;
    }

    public void ShowWindow()
    {
        _dispatcher?.TryEnqueue(() =>
        {
            if (_disposed) return;
            App.MainWindow.Activate();
        });
    }

    private void ShowTrayMenu()
    {
        _dispatcher?.TryEnqueue(() =>
        {
            if (_disposed) return;
            var menu = new TrayMenuWindow();
            menu.AppWindow.MoveInZOrderAtTop();
            menu.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            menu.AppWindow.IsShownInSwitchers = false;
            if (menu.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsAlwaysOnTop = true;
            }
            var displayArea = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(Cursor.Position.X, Cursor.Position.Y), DisplayAreaFallback.Primary);
            menu.Activate();
        });
    }


    public void ExitApp()
    {
        _dispatcher?.TryEnqueue(async () =>
        {
            if (_disposed) return;
            _notifyIcon.Visible = false;
            await ((App)Microsoft.UI.Xaml.Application.Current).ShutdownAsync();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.Visible = false;
        var icon = _notifyIcon.Icon;
        _notifyIcon.Dispose();
        icon?.Dispose();
        _dispatcher = null;
        GC.SuppressFinalize(this);
    }
}
