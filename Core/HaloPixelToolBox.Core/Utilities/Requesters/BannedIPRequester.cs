using System.Text.Json;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.Requester;
using XFEExtension.NetCore.ServerInteractive.Models;
using XFEExtension.NetCore.ServerInteractive.Utilities.JsonConverter;

namespace HaloPixelToolBox.Core.Utilities.Requesters;

/// <summary>
/// 适配 ServerInteractive 3.2.2 服务端使用的分段 IP 黑名单路由。
/// </summary>
public partial class BannedIPRequester : StandardRequestServiceBase
{
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = CreateJsonSerializerOptions();

    [Request("ip/banned/get", Name = "get_bannedIPList")]
    public object GetBannedIPListRequest() => new
    {
        session = Session,
        deviceInfo = DeviceInfo
    };

    [Response("ip/banned/get", Name = "get_bannedIPList")]
    public object GetBannedIPListResponse() =>
        JsonSerializer.Deserialize<List<IPAddressInfo>>(Response, s_jsonSerializerOptions) ?? [];

    [Request("ip/banned/add", Name = "add_bannedIP")]
    public object AddBannedIPRequest() => new
    {
        session = Session,
        deviceInfo = DeviceInfo,
        bannedIP = Parameters[0],
        notes = Parameters[1]
    };

    [Response("ip/banned/add", Name = "add_bannedIP")]
    public object AddBannedIPResponse() => Response;

    [Request("ip/banned/remove", Name = "remove_bannedIP")]
    public object RemoveBannedIPRequest() => new
    {
        session = Session,
        deviceInfo = DeviceInfo,
        bannedIP = Parameters[0]
    };

    [Response("ip/banned/remove", Name = "remove_bannedIP")]
    public object RemoveBannedIPResponse() => bool.TryParse(Response, out var result) && result;

    private static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonDateTimeConverter());
        return options;
    }
}
