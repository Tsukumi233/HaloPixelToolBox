using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace HaloPixelToolBox.Client.Utilities.Helpers;

public static class UpgradeHelper
{
    public const string ReleasesUrl = "https://github.com/Tsukumi233/HaloPixelToolBox/releases";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    public static Version Version => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    public sealed record UpgradeNotes(bool IsLatest, string LatestVersion, string ReleaseNotes);

    public static async Task<UpgradeNotes?> GetReleaseNotes(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/Tsukumi233/HaloPixelToolBox/releases/latest");
            request.Headers.UserAgent.ParseAdd("HaloPixelToolBox/" + Version);
            using var response = await Client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var tag = json.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!System.Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                throw new FormatException($"无法识别版本标签：{tag}");
            var normalized = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build), Math.Max(0, latest.Revision));
            return new(normalized <= Version, tag,
                (json.RootElement.GetProperty("body").GetString() ?? string.Empty) +
                "\n\n请前往 GitHub Releases 手动下载并更新。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN]检查 GitHub 更新失败：{ex.Message}");
            return null;
        }
    }

    public static async Task<bool> CheckUpgrade() => (await GetReleaseNotes())?.IsLatest ?? true;

    public static void OpenReleases()
    {
        using var process = Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
    }
}
