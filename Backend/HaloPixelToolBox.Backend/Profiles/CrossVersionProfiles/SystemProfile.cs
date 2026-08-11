using XFEExtension.NetCore.AutoConfig;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Backend.Profiles.CrossVersionProfiles;

public partial class SystemProfile : XFEProfile
{
    public SystemProfile() => ProfilePath = $@"{AppPathHelper.LocalProfile}\{nameof(SystemProfile)}";

    /// <summary>
    /// 主题颜色
    /// </summary>
    [ProfileProperty]
    private ElementTheme _theme = ElementTheme.Default;
    /// <summary>
    /// 是否自动启动
    /// </summary>
    [ProfileProperty]
    private bool _autoStart;
    /// <summary>
    /// 管理端连接的服务器地址
    /// </summary>
    [ProfileProperty]
    private string _serverAddress = HaloPixelToolBox.Core.Utilities.DataManager.DefaultRequestAddress;

    static partial void SetThemeProperty(ref ElementTheme value) => AppThemeHelper.ChangeTheme(value);
}
