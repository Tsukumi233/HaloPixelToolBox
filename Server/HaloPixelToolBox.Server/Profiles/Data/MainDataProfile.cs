using HaloPixelToolBox.Core.Models.Bar;
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
    private ProfileDictionary<string, AddressResolverModel> versionAddressTable = new()
    {
        {
            "3.1.35",
            new()
            {
                Version = "3.1.35",
                ModuleName = "cloudmusic.dll",
                BaseAddress = 0x01ED9690,
                Offsets = [0x120, 0x8, 0x0]
            }
        },
        {
            "3.1.30",
            new()
            {
                Version = "3.1.30",
                ModuleName = "cloudmusic.dll",
                BaseAddress = 0x01DF44D0,
                Offsets = [0x120, 0x8, 0x0]
            }
        },
        {
            "3.1.29",
            new()
            {
                Version = "3.1.29",
                ModuleName = "cloudmusic.dll",
                BaseAddress = 0x01DEB4D0,
                Offsets = [0x120, 0x8, 0x0]
            }
        },
        {
            "3.1.28",
            new()
            {
                Version = "3.1.28",
                ModuleName = "cloudmusic.dll",
                BaseAddress = 0x01DDF290,
                Offsets = [0x120, 0x8, 0x0]
            }
        },
        {
            "3.1.27",
            new()
            {
                Version = "3.1.27",
                ModuleName = "cloudmusic.dll",
                BaseAddress = 0x01DDE290,
                Offsets = [ 0xE0, 0x8, 0xE8, 0x38, 0x118, 0x8, 0x0]
            }
        },
        {
            "3.1.26",
            new()
            {
                Version = "3.1.26",
                ModuleName = "cloudmusic.dll",
                BaseAddress = 0x01DD5130,
                Offsets = [ 0xE8, 0x38, 0x120, 0x18, 0x0]
            }
        },
        {
            "3.1.25",
            new()
            {
                Version = "3.1.25",
                ModuleName = "cloudmusic.dll",
                BaseAddress = 0x01DAFF60,
                Offsets = [0xE0, 0x8, 0x128, 0x18, 0x0]
            }
        }
    };
}
