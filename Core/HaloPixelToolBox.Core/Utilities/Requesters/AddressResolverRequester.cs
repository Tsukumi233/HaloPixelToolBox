using HaloPixelToolBox.Core.Models.Bar;
using System.Text;
using System.Text.Json;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.Requester;

namespace HaloPixelToolBox.Core.Utilities.Requesters;

public partial class AddressResolverRequester : StandardRequestServiceBase
{
    [Request("data/get/versionAddress", Name = "getAddress")]
    public object GetVersionAddressRequest() => new
    {
        version = Parameters[0]
    };

    [Response("data/get/versionAddress", Name = "getAddress")]
    public object GetVersionAddressResponse() => JsonSerializer.Deserialize<AddressResolverModel>(Response) ?? new AddressResolverModel();

    [Request("data/get/versionAddressList", Name = "getAddressList")]
    public object GetVersionAddressListRequest() => new
    {
        session = Session,
        deviceInfo = DeviceInfo
    };

    [Response("data/get/versionAddressList", Name = "getAddressList")]
    public object GetVersionAddressListResponse() => JsonSerializer.Deserialize<List<AddressResolverModel>>(Response) ?? [];

    [Request("data/add/versionAddress", Name = "addAddress")]
    public object AddVersionAddressRequest() => new
    {
        session = Session,
        deviceInfo = DeviceInfo,
        addressModel = EncodeAddressModel(Parameters[0])
    };

    [Response("data/add/versionAddress", Name = "addAddress")]
    public object AddVersionAddressResponse() => bool.TryParse(Response, out var result) && result;

    [Request("data/change/versionAddress", Name = "changeAddress")]
    public object ChangeVersionAddressRequest() => new
    {
        session = Session,
        deviceInfo = DeviceInfo,
        originalVersion = Parameters[0],
        addressModel = EncodeAddressModel(Parameters[1])
    };

    [Response("data/change/versionAddress", Name = "changeAddress")]
    public object ChangeVersionAddressResponse() => bool.TryParse(Response, out var result) && result;

    [Request("data/remove/versionAddress", Name = "removeAddress")]
    public object RemoveVersionAddressRequest() => new
    {
        session = Session,
        deviceInfo = DeviceInfo,
        version = Parameters[0]
    };

    [Response("data/remove/versionAddress", Name = "removeAddress")]
    public object RemoveVersionAddressResponse() => bool.TryParse(Response, out var result) && result;

    private static string EncodeAddressModel(object model) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(model)));
}
