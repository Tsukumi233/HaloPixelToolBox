using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Server.Profiles.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server.Services.CoreService;
using XFEExtension.NetCore.StringExtension;

namespace HaloPixelToolBox.Server.Services.Data;

/// <summary>
/// 地址解析服务
/// </summary>
public partial class AddressResolverService : ServerCoreUserServiceBase
{
    private static readonly object s_addressTableLock = new();

    public int EditAddressPermission { get; set; }

    [EntryPoint("data/get/versionAddress")]
    public async Task GetVersionAddressEntryPoint()
    {
        string version = Json > "version" ?? throw Error("缺少对应版本");
        if (version.NullOrWhiteSpace) throw Error("缺少对应版本");
        AddressResolverModel? addressResolverModel;
        lock (s_addressTableLock)
        {
            addressResolverModel = MainDataProfile.VersionAddressTable.TryGetValue(version, out var model)
                ? Clone(model, version)
                : null;
        }

        if (addressResolverModel is null)
            throw Error($"未找到版本 v{version} 对应的解析地址，请联系作者更新");

        await Close(addressResolverModel);
    }

    [EntryPoint("data/get/versionAddressList")]
    public async Task GetVersionAddressListEntryPoint()
    {
        VerifyPermission();
        List<AddressResolverModel> result;
        lock (s_addressTableLock)
        {
            result = MainDataProfile.VersionAddressTable
                .Select(pair => Clone(pair.Value, pair.Key))
                .OrderByDescending(model => ParseVersion(model.Version))
                .ToList();
        }
        await Close(result);
    }

    [EntryPoint("data/add/versionAddress")]
    public async Task AddVersionAddressEntryPoint()
    {
        VerifyPermission();
        var addressModel = ReadAndValidateAddressModel();
        lock (s_addressTableLock)
        {
            if (MainDataProfile.VersionAddressTable.ContainsKey(addressModel.Version))
                throw Error($"版本 v{addressModel.Version} 已存在");
            MainDataProfile.VersionAddressTable.Add(addressModel.Version, addressModel);
        }
        await Close(true);
    }

    [EntryPoint("data/remove/versionAddress")]
    public async Task RemoveVersionAddressEntryPoint()
    {
        VerifyPermission();
        string version = Json > "version" ?? throw Error("缺少版本号");
        if (version.NullOrWhiteSpace) throw Error("版本号不可为空");
        lock (s_addressTableLock)
        {
            if (!MainDataProfile.VersionAddressTable.Remove(version))
                throw Error($"版本 v{version} 不存在");
        }
        await Close(true);
    }

    [EntryPoint("data/change/versionAddress")]
    public async Task ChangeVersionAddressEntryPoint()
    {
        VerifyPermission();
        string originalVersion = Json > "originalVersion" ?? throw Error("缺少原版本号");
        if (originalVersion.NullOrWhiteSpace) throw Error("原版本号不可为空");
        var addressModel = ReadAndValidateAddressModel();
        lock (s_addressTableLock)
        {
            if (!MainDataProfile.VersionAddressTable.ContainsKey(originalVersion))
                throw Error($"版本 v{originalVersion} 不存在");
            if (!string.Equals(originalVersion, addressModel.Version, StringComparison.OrdinalIgnoreCase) &&
                MainDataProfile.VersionAddressTable.ContainsKey(addressModel.Version))
                throw Error($"版本 v{addressModel.Version} 已存在");

            if (!string.Equals(originalVersion, addressModel.Version, StringComparison.OrdinalIgnoreCase))
                MainDataProfile.VersionAddressTable.Remove(originalVersion);
            MainDataProfile.VersionAddressTable[addressModel.Version] = addressModel;
        }
        await Close(true);
    }

    private void VerifyPermission()
    {
        if (User.PermissionLevel < EditAddressPermission)
            throw Error("权限不足");
    }

    private AddressResolverModel ReadAndValidateAddressModel()
    {
        var json = Json > "addressModel" ?? throw Error("缺少地址解析模型");
        var model = DeserializeAddressModel(json) ?? throw Error("地址解析模型无效");
        model.Version = model.Version.Trim();
        model.ModuleName = model.ModuleName.Trim();
        if (model.Version.NullOrWhiteSpace) throw Error("版本号不可为空");
        if (model.ModuleName.NullOrWhiteSpace) throw Error("组件名称不可为空");
        if (model.BaseAddress <= 0) throw Error("基址必须大于 0");
        if (model.Offsets.Length == 0) throw Error("至少需要一个偏移");
        return Clone(model, model.Version);
    }

    private AddressResolverModel? DeserializeAddressModel(string value)
    {
        string json;
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            json = value;
        }

        try
        {
            return JsonSerializer.Deserialize<AddressResolverModel>(json, JsonSerializerOptions);
        }
        catch (JsonException) when (ReferenceEquals(json, value))
        {
            // 兼容直接发送 JSON 字符串、但被 ServerInteractive 查询器保留转义符的旧请求。
            return JsonSerializer.Deserialize<AddressResolverModel>(Regex.Unescape(value), JsonSerializerOptions);
        }
    }

    private static AddressResolverModel Clone(AddressResolverModel model, string version) => new()
    {
        Version = version,
        ModuleName = model.ModuleName,
        BaseAddress = model.BaseAddress,
        Offsets = [.. model.Offsets]
    };

    private static Version ParseVersion(string version) => Version.TryParse(version, out var result) ? result : new Version();
}
