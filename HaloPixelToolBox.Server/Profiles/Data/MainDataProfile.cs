using XFEExtension.NetCore.AutoConfig;

namespace HaloPixelToolBox.Server.Profiles.Data;

/// <summary>
/// 主要数据配置文件
/// </summary>
public partial class MainDataProfile : XFEProfile
{
    /// <summary>
    /// 版本地址表
    /// </summary>
    [ProfileProperty]
    [ProfilePropertyAddGet("Current.versionAddressTable.CurrentProfile = Current")]
    [ProfilePropertyAddGet("return Current.versionAddressTable")]
    private ProfileDictionary<string, string> versionAddressTable = [];
}
