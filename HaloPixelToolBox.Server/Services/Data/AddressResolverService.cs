using HaloPixelToolBox.Core.Models.User;
using HaloPixelToolBox.Server.Profiles.Data;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.CoreService;
using XFEExtension.NetCore.ServerInteractive.Utilities.Helpers;

namespace HaloPixelToolBox.Server.Services.Data;

/// <summary>
/// 地址解析服务
/// </summary>
public partial class AddressResolverService : ServerCoreStandardServiceBase
{
    public int AddAddressPermission { get; set; } = (int)UserRole.管理员;

    [EntryPoint("data/get/versionAddress")]
    public async Task GetVersionAddressEntryPoint() => await Close(MainDataProfile.VersionAddressTable);

    [EntryPoint("data/add/versionAddress")]
    public async Task AddVersionAddressEntryPoint()
    {
        UserHelper.ValidateUserPermission(Json > "session", Json > "deviceInfo", ClientIP, AddAddressPermission, UserDataProfile.EncryptedUserLoginModelTable, UserDataProfile.MyUserTable);
    }
}
