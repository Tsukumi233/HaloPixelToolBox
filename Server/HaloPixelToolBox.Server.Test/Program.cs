using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Models.User;
using HaloPixelToolBox.Core.Utilities;
using System.Net;
using XFEExtension.NetCore.ServerInteractive.Models;
using XFEExtension.NetCore.ServerInteractive.Models.RequesterModels;

if (!args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Pass --integration to run the ClientRequester end-to-end server test.");
    return;
}

var serverAddress = Environment.GetEnvironmentVariable("HALOPIXEL_SERVER_ADDRESS") ?? DataManager.DefaultRequestAddress;
var userName = Environment.GetEnvironmentVariable("HALOPIXEL_ADMIN_USERNAME") ?? "admin";
var password = Environment.GetEnvironmentVariable("HALOPIXEL_ADMIN_PASSWORD") ?? "HaloPixelToolBox@2026";
var testVersion = $"0.0.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
const string testIPAddress = "192.0.2.123";
var addressCreated = false;
var ipBanned = false;

try
{
    Ensure(await DataManager.InitializeAsync([serverAddress], true), $"Cannot connect to {serverAddress}");

    var publicAddress = await DataManager.ClientRequester.Request<AddressResolverModel>("getAddress", "3.1.35");
    Ensure(publicAddress.StatusCode == HttpStatusCode.OK && publicAddress.Result?.Version == "3.1.35", "Public address lookup failed");

    var login = await DataManager.ClientRequester.Request<UserLoginResult<MyUserFaceInfo>>("login", userName, password);
    Ensure(login.StatusCode == HttpStatusCode.OK && login.Result?.UserInfo is not null, "Admin login failed");

    var testAddress = new AddressResolverModel
    {
        Version = testVersion,
        ModuleName = "integration-test.dll",
        BaseAddress = 0x1234,
        Offsets = [0x10, 0x20]
    };
    var addAddress = await DataManager.ClientRequester.Request<bool>("addAddress", testAddress);
    Ensure(addAddress.StatusCode == HttpStatusCode.OK && addAddress.Result, "Adding an address failed");
    addressCreated = true;

    testAddress.ModuleName = "integration-test-updated.dll";
    testAddress.BaseAddress = 0x5678;
    var changeAddress = await DataManager.ClientRequester.Request<bool>("changeAddress", testVersion, testAddress);
    Ensure(changeAddress.StatusCode == HttpStatusCode.OK && changeAddress.Result, "Changing an address failed");

    var addressList = await DataManager.ClientRequester.Request<List<AddressResolverModel>>("getAddressList");
    Ensure(addressList.StatusCode == HttpStatusCode.OK &&
           addressList.Result?.Any(item => item.Version == testVersion && item.BaseAddress == 0x5678) == true,
        "Address list did not contain the updated item");

    var addBan = await DataManager.ClientRequester.Request<string>("add_bannedIP", testIPAddress, "integration test");
    Ensure(addBan.StatusCode == HttpStatusCode.OK, "Banning an IP failed");
    ipBanned = true;

    var bannedList = await DataManager.ClientRequester.Request<List<IPAddressInfo>>("get_bannedIPList");
    Ensure(bannedList.StatusCode == HttpStatusCode.OK &&
           bannedList.Result?.Any(item => item.IPAddress == testIPAddress) == true,
        "Banned IP list did not contain the test IP");

    Console.WriteLine("ClientRequester integration test passed.");
}
finally
{
    if (addressCreated)
    {
        var removeAddress = await DataManager.ClientRequester.Request<bool>("removeAddress", testVersion);
        Ensure(removeAddress.StatusCode == HttpStatusCode.OK && removeAddress.Result, "Address cleanup failed");
    }

    if (ipBanned)
    {
        var removeBan = await DataManager.ClientRequester.Request<bool>("remove_bannedIP", testIPAddress);
        Ensure(removeBan.StatusCode == HttpStatusCode.OK && removeBan.Result, "IP ban cleanup failed");
    }
}

static void Ensure(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
