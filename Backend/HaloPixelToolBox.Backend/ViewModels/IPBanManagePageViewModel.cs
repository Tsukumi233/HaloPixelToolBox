using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HaloPixelToolBox.Core.Utilities;
using System.Collections.ObjectModel;
using System.Net;
using XFEExtension.NetCore.ServerInteractive.Models;

namespace HaloPixelToolBox.Backend.ViewModels;

public partial class IPBanManagePageViewModel : ViewModelBase
{
    [ObservableProperty] public partial ObservableCollection<IPAddressInfo> Items { get; set; } = [];
    [ObservableProperty] public partial IPAddressInfo? SelectedItem { get; set; }
    [ObservableProperty] public partial string IPAddressText { get; set; } = string.Empty;
    [ObservableProperty] public partial string Notes { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = "请先在主页登录管理员账号";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!EnsureLoggedIn())
            return;

        IsBusy = true;
        try
        {
            var response = await DataManager.ClientRequester.Request<List<IPAddressInfo>>("get_bannedIPList");
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
            {
                StatusText = "读取 IP 黑名单失败，请确认登录状态和管理员权限";
                return;
            }

            Items = new(response.Result.OrderByDescending(item => item.BannedTime));
            StatusText = $"当前共封禁 {Items.Count} 个 IP";
        }
        catch (Exception ex)
        {
            StatusText = $"读取 IP 黑名单失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!EnsureLoggedIn())
            return;
        var input = IPAddressText.Trim();
        if (!System.Net.IPAddress.TryParse(input, out var parsedAddress))
        {
            StatusText = "请输入有效的 IPv4 或 IPv6 地址";
            return;
        }
        var normalizedAddress = parsedAddress.ToString();
        if (Items.Any(item => string.Equals(item.IPAddress, normalizedAddress, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = $"IP {normalizedAddress} 已在黑名单中";
            return;
        }

        IsBusy = true;
        try
        {
            var response = await DataManager.ClientRequester.Request<string>("add_bannedIP", normalizedAddress, Notes.Trim());
            if (response.StatusCode != HttpStatusCode.OK)
            {
                StatusText = "封禁失败，请确认登录状态和管理员权限";
                return;
            }

            IPAddressText = string.Empty;
            Notes = string.Empty;
            StatusText = $"IP {normalizedAddress} 已封禁";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"封禁失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (!EnsureLoggedIn() || SelectedItem is null)
        {
            StatusText = "请先选择要解封的 IP";
            return;
        }

        IsBusy = true;
        try
        {
            var address = SelectedItem.IPAddress;
            var response = await DataManager.ClientRequester.Request<bool>("remove_bannedIP", address);
            if (response.StatusCode != HttpStatusCode.OK || !response.Result)
            {
                StatusText = "解封失败，请确认登录状态和管理员权限";
                return;
            }

            SelectedItem = null;
            StatusText = $"IP {address} 已解封";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"解封失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool EnsureLoggedIn()
    {
        if (!string.IsNullOrWhiteSpace(DataManager.ClientRequester.Session))
            return true;
        StatusText = "请先返回主页登录管理员账号";
        return false;
    }
}
