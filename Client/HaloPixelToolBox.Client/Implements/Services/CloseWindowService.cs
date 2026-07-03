using Windows.Foundation;
using HaloPixelToolBox.Client.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Implements.Services;

namespace HaloPixelToolBox.Client.Implements.Services;

public class CloseWindowService : GlobalServiceBase, ICloseWindowService
{
    Window? window;
    public event TypedEventHandler<object, WindowEventArgs>? Closed;

    public void Initialize(Window window)
    {
        this.window = window;
        this.window.Closed += (e, s) => Closed?.Invoke(e, s);
    }
}
