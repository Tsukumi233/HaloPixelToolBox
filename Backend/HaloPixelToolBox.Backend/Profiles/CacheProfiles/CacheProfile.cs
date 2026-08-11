using XFEExtension.NetCore.AutoConfig;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Backend.Profiles.CacheProfiles;

public partial class CacheProfile : XFEProfile
{
    public CacheProfile() => ProfilePath = $@"{AppPathHelper.CacheProfile}\{nameof(CacheProfile)}";

    [ProfileProperty]
    private string _session = string.Empty;

    [ProfileProperty]
    private string _account = "admin";
}
