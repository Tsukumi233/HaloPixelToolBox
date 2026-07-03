using HaloPixelToolBox.Core.Models.Bar;
using XFEExtension.NetCore.AutoConfig;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Profiles.CacheProfiles;

public partial class CacheProfile : XFEProfile
{
    public CacheProfile() => ProfilePath = $@"{AppPathHelper.CacheProfile}\{nameof(CacheProfile)}";

    [ProfileProperty]
    [ProfilePropertyAddGet("Current.versionAddress.CurrentProfile = Current")]
    [ProfilePropertyAddGet("return Current.versionAddress")]
    private ProfileDictionary<string, AddressResolverModel> versionAddress = [];
}
