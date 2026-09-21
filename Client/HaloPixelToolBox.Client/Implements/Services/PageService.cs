using HaloPixelToolBox.Client.Interface.Services;

namespace HaloPixelToolBox.Client.Implements.Services;

public class PageService : IPageService, IDisposable
{
    Page? _page;
    public Page CurrentPage => _page ?? throw new InvalidOperationException("PageService has not been initialized. Please call Initialize() with a valid Page instance.");

    public event RoutedEventHandler? CurrentPageLoaded;

    public void Initialize(Page page)
    {
        if (_page is not null) _page.Loaded -= OnLoaded;
        _page = page;
        _page.Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => CurrentPageLoaded?.Invoke(sender, args);

    public void Dispose()
    {
        if (_page is not null) _page.Loaded -= OnLoaded;
        _page = null;
        CurrentPageLoaded = null;
    }
}
