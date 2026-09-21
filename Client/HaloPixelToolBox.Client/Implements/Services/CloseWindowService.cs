using Windows.Foundation;
using HaloPixelToolBox.Client.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Implements.Services;

namespace HaloPixelToolBox.Client.Implements.Services;

public class CloseWindowService : GlobalServiceBase, ICloseWindowService, IDisposable
{
    Window? window;
    public event TypedEventHandler<object, WindowEventArgs>? Closed;

    public void Initialize(Window window)
    {
        if (this.window is not null) this.window.Closed -= OnClosed;
        this.window = window;
        this.window.Closed += OnClosed;
    }

    private void OnClosed(object sender, WindowEventArgs args) => Closed?.Invoke(sender, args);

    public void Dispose()
    {
        if (window is not null) window.Closed -= OnClosed;
        window = null;
        Closed = null;
    }
}
