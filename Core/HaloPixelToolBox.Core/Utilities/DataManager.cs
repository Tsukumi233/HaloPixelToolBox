using HaloPixelToolBox.Core.Models.User;
using HaloPixelToolBox.Core.Utilities.Requesters;
using System.Net;
using XFEExtension.NetCore.ServerInteractive.Utilities.Extensions;
using XFEExtension.NetCore.ServerInteractive.Utilities.Requester;

namespace HaloPixelToolBox.Core.Utilities;

/// <summary>
/// 创建和维护客户端、管理端共用的服务器请求器。
/// </summary>
public static class DataManager
{
    public const string DefaultRequestAddress = "http://localhost:3300/api";

    private static readonly SemaphoreSlim s_initializeLock = new(1, 1);
    private static string[] s_requestAddresses = [DefaultRequestAddress];

    /// <summary>
    /// 当前实际使用的请求地址。
    /// </summary>
    public static string RequestAddress { get; private set; } = DefaultRequestAddress;

    /// <summary>
    /// 客户端请求器。客户端和管理员端都必须通过此实例访问服务器。
    /// </summary>
    public static ClientRequester ClientRequester { get; private set; } = CreateRequester(DefaultRequestAddress);

    /// <summary>
    /// 使用给定候选地址初始化请求器，优先选择最先连通的地址。
    /// 即使全部地址不可用，也会保留第一个地址，以便后续业务请求正常返回网络错误。
    /// </summary>
    public static async Task<bool> InitializeAsync(IEnumerable<string>? requestAddresses = null, bool force = false)
    {
        await s_initializeLock.WaitAsync();
        try
        {
            var addresses = NormalizeAddresses(requestAddresses).ToArray();
            if (addresses.Length == 0)
                addresses = [DefaultRequestAddress];

            if (!force && addresses.SequenceEqual(s_requestAddresses, StringComparer.OrdinalIgnoreCase) &&
                await CheckConnectToAddressAsync(RequestAddress))
                return true;

            s_requestAddresses = addresses;
            var connectTasks = addresses.Select(async address => new
            {
                Address = address,
                IsConnected = await CheckConnectToAddressAsync(address)
            }).ToList();

            while (connectTasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(connectTasks);
                connectTasks.Remove(completedTask);
                var result = await completedTask;
                if (!result.IsConnected)
                    continue;

                ReplaceRequester(result.Address);
                return true;
            }

            ReplaceRequester(addresses[0]);
            return false;
        }
        finally
        {
            s_initializeLock.Release();
        }
    }

    /// <summary>
    /// 直接切换到指定地址。主要用于设置页保存地址后立即生效。
    /// </summary>
    public static void Configure(string requestAddress)
    {
        var address = NormalizeAddress(requestAddress) ?? DefaultRequestAddress;
        s_requestAddresses = [address];
        ReplaceRequester(address);
    }

    public static async Task<bool> CheckConnectToAddressAsync(string requestAddress)
    {
        try
        {
            var requester = ClientRequesterBuilder.CreateBuilder()
                .AddCheckConnectRequest()
                .Build(options => options.RequestAddress = requestAddress);
            var response = await requester.Request<DateTime>("check_connect");
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN]无法连接服务器 {requestAddress}：{ex.Message}");
            return false;
        }
    }

    private static ClientRequester CreateRequester(string requestAddress) => ClientRequesterBuilder.CreateBuilder()
        .UseXFEStandardRequest<MyUserFaceInfo>()
        .AddRequest<BannedIPRequester>()
        .AddRequest<AddressResolverRequester>()
        .Build(options => options.RequestAddress = requestAddress);

    private static void ReplaceRequester(string requestAddress)
    {
        var session = ClientRequester.Session;
        RequestAddress = requestAddress;
        ClientRequester = CreateRequester(requestAddress);
        ClientRequester.Session = session;
    }

    private static IEnumerable<string> NormalizeAddresses(IEnumerable<string>? addresses) =>
        (addresses ?? s_requestAddresses)
        .Select(NormalizeAddress)
        .Where(address => address is not null)
        .Select(address => address!)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string? NormalizeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        address = address.Trim().TrimEnd('/');
        return Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? address
            : null;
    }
}
