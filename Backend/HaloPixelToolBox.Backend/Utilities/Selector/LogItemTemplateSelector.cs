using XFEExtension.NetCore.XFEConsole;

namespace HaloPixelToolBox.Backend.Utilities.Selector;

public partial class LogItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DebugTemplate { get; set; }
    public DataTemplate? TraceTemplate { get; set; }
    public DataTemplate? InfoTemplate { get; set; }
    public DataTemplate? ErrorTemplate { get; set; }
    public DataTemplate? WarningTemplate { get; set; }
    public DataTemplate? FatalTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        if (item is not XFELogEntry logEntry) return InfoTemplate;
        switch (logEntry.Level)
        {
            case LogLevel.Trace:
                return TraceTemplate;
            case LogLevel.Debug:
                return DebugTemplate;
            case LogLevel.Info:
                break;
            case LogLevel.Warning:
                return WarningTemplate;
            case LogLevel.Error:
                return ErrorTemplate;
            case LogLevel.Fatal:
                return FatalTemplate;
            default:
                break;
        }
        return InfoTemplate;
    }
}