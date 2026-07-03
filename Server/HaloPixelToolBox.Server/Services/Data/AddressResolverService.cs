using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Models.User;
using HaloPixelToolBox.Server.Profiles.Data;
using System.Text.Json;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server.Services.CoreService;
using XFEExtension.NetCore.StringExtension;

namespace HaloPixelToolBox.Server.Services.Data;

/// <summary>
/// 地址解析服务
/// </summary>
public partial class AddressResolverService : ServerCoreUserServiceBase
{
    public int EditAddressPermission { get; set; } = (int)UserRole.管理员;

    [EntryPoint("data/get/versionAddress")]
    public async Task GetVersionAddressEntryPoint()
    {
        string? version = Json > "version";
        if (!version.NullOrWhiteSpace)
        {
            if (MainDataProfile.VersionAddressTable.TryGetValue(version, out var addressResolverModel))
                await Close(addressResolverModel);
            else
                throw Error($"未找到版本 v{version} 对应的解析地址，请联系作者更新");
        }
        else
        {
            await Close(MainDataProfile.VersionAddressTable);
        }
    }

    [EntryPoint("data/add/versionAddress")]
    public async Task AddVersionAddressEntryPoint()
    {
        VerifyPermission();
        string version = Json > "version" ?? throw Error("缺少版本号");
        if (version.NullOrWhiteSpace) throw Error("版本号不可为空");
        var addressModel = JsonSerializer.Deserialize<AddressResolverModel>(Json > "addressModel" ?? throw Error("缺少地址解析模型")) ?? throw Error("缺少地址解析模型");
        MainDataProfile.VersionAddressTable.Add(version, addressModel);
    }

    [EntryPoint("data/remove/versionAddress")]
    public async Task RemoveVersionAddressEntryPoint()
    {
        VerifyPermission();
        string version = Json > "version" ?? throw Error("缺少版本号");
        if (version.NullOrWhiteSpace) throw Error("版本号不可为空");
        MainDataProfile.VersionAddressTable.Remove(version);
    }

    [EntryPoint("data/change/versionAddress")]
    public async Task ChangeVersionAddressEntryPoint()
    {
        VerifyPermission();
        string version = Json > "version" ?? throw Error("缺少版本号");
        if (version.NullOrWhiteSpace) throw Error("版本号不可为空");
        var addressModel = JsonSerializer.Deserialize<AddressResolverModel>(Json > "addressModel" ?? throw Error("缺少地址解析模型")) ?? throw Error("缺少地址解析模型");
        MainDataProfile.VersionAddressTable[version] = addressModel;
    }

    private void VerifyPermission()
    {
        if (User.PermissionLevel > EditAddressPermission)
            throw Error("权限不足");
    }
}
