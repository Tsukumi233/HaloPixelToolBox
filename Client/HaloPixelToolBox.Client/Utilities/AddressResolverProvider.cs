using HaloPixelToolBox.Client.Profiles.CacheProfiles;
using HaloPixelToolBox.Client.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Utilities;
using System.Net;

namespace HaloPixelToolBox.Client.Utilities;

/// <summary>
/// 从服务器获取地址解析配置，并在网络不可用时回退到本地缓存。
/// </summary>
public static class AddressResolverProvider
{
    public static void LoadCachedResolvers()
    {
        foreach (var pair in CacheProfile.VersionAddress)
        {
            var model = Clone(pair.Value, pair.Key);
            CloudMusicLyricsReader.SetAddressResolver(model);
        }
    }

    public static async Task<AddressResolverModel?> GetAsync(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var cached = CacheProfile.VersionAddress.TryGetValue(version, out var cachedModel)
            ? Clone(cachedModel, version)
            : null;

        try
        {
            var connected = await DataManager.InitializeAsync([SystemProfile.ServerAddress]);
            if (!connected)
            {
                Console.WriteLine($"[WARN]解析地址服务器不可用，尝试使用版本 {version} 的本地缓存");
                return cached;
            }

            var response = await DataManager.ClientRequester.Request<AddressResolverModel>("getAddress", version);
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
                return cached;

            var model = Clone(response.Result, version);
            if (!IsValid(model))
                return cached;

            CacheProfile.VersionAddress[version] = model;
            CloudMusicLyricsReader.SetAddressResolver(model);
            Console.WriteLine($"[INFO]已从服务器更新网易云音乐 {version} 的解析地址并写入缓存");
            return model;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN]获取版本 {version} 的解析地址失败，使用本地缓存：{ex.Message}");
            return cached;
        }
    }

    private static bool IsValid(AddressResolverModel model) =>
        !string.IsNullOrWhiteSpace(model.Version) &&
        !string.IsNullOrWhiteSpace(model.ModuleName) &&
        model.BaseAddress > 0 &&
        model.Offsets.Length > 0;

    private static AddressResolverModel Clone(AddressResolverModel model, string version) => new()
    {
        Version = version,
        ModuleName = model.ModuleName,
        BaseAddress = model.BaseAddress,
        Offsets = [.. model.Offsets]
    };
}
