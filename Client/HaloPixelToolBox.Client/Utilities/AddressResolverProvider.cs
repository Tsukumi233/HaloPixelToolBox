using HaloPixelToolBox.Client.Profiles.CacheProfiles;
using HaloPixelToolBox.Client.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Utilities;
using System.Net;
using System.Net.Http.Json;

namespace HaloPixelToolBox.Client.Utilities;

/// <summary>
/// 从服务器获取地址解析配置，并在网络不可用时回退到本地缓存。
/// </summary>
public static class AddressResolverProvider
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    public static void LoadCachedResolvers()
    {
        foreach (var pair in CacheProfile.VersionAddress)
        {
            var model = Clone(pair.Value, pair.Key);
            CloudMusicLyricsReader.SetAddressResolver(model);
        }
    }

    public static async Task<AddressResolverModel?> GetAsync(string version, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var cached = CacheProfile.VersionAddress.TryGetValue(version, out var cachedModel)
            ? Clone(cachedModel, version)
            : null;

        try
        {
            // This public route accepts { version } and returns AddressResolverModel.
            // Use cancellable HTTP directly: the legacy requester has no cancellation API.
            var address = SystemProfile.ServerAddress.TrimEnd('/') + "/data/get/versionAddress";
            using var response = await Client.PostAsJsonAsync(address, new { version }, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
                return cached;
            var result = await response.Content.ReadFromJsonAsync<AddressResolverModel>(cancellationToken);
            if (result is null) return cached;
            cancellationToken.ThrowIfCancellationRequested();
            var model = Clone(result, version);
            if (!IsValid(model))
                return cached;

            CacheProfile.VersionAddress[version] = model;
            CloudMusicLyricsReader.SetAddressResolver(model);
            Console.WriteLine($"[INFO]已从服务器更新网易云音乐 {version} 的解析地址并写入缓存");
            return model;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
