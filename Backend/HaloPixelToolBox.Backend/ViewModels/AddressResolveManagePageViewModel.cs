using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Utilities;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;

namespace HaloPixelToolBox.Backend.ViewModels;

public partial class AddressResolveManagePageViewModel : ViewModelBase
{
    [ObservableProperty] public partial ObservableCollection<AddressResolverModel> Items { get; set; } = [];
    [ObservableProperty] public partial AddressResolverModel? SelectedItem { get; set; }
    [ObservableProperty] public partial string Version { get; set; } = string.Empty;
    [ObservableProperty] public partial string ModuleName { get; set; } = "cloudmusic.dll";
    [ObservableProperty] public partial string BaseAddressText { get; set; } = string.Empty;
    [ObservableProperty] public partial string OffsetsText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = "请先在主页登录管理员账号";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    private string? _originalVersion;

    partial void OnSelectedItemChanged(AddressResolverModel? value)
    {
        if (value is null)
            return;

        _originalVersion = value.Version;
        Version = value.Version;
        ModuleName = value.ModuleName;
        BaseAddressText = value.BaseAddressHex;
        OffsetsText = value.OffsetsHex;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!EnsureLoggedIn())
            return;

        IsBusy = true;
        try
        {
            var response = await DataManager.ClientRequester.Request<List<AddressResolverModel>>("getAddressList");
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
            {
                StatusText = "读取解析地址失败，请确认登录状态和管理员权限";
                return;
            }

            Items = new(response.Result);
            StatusText = $"已加载 {Items.Count} 条解析地址";
        }
        catch (Exception ex)
        {
            StatusText = $"读取解析地址失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void New()
    {
        SelectedItem = null;
        _originalVersion = null;
        Version = string.Empty;
        ModuleName = "cloudmusic.dll";
        BaseAddressText = string.Empty;
        OffsetsText = string.Empty;
        StatusText = "正在新建解析地址";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!EnsureLoggedIn())
            return;
        if (!TryBuildModel(out var model, out var error))
        {
            StatusText = error;
            return;
        }

        IsBusy = true;
        try
        {
            var response = _originalVersion is null
                ? await DataManager.ClientRequester.Request<bool>("addAddress", model)
                : await DataManager.ClientRequester.Request<bool>("changeAddress", _originalVersion, model);

            if (response.StatusCode != HttpStatusCode.OK || !response.Result)
            {
                StatusText = "保存失败，版本可能已存在或登录已过期";
                return;
            }

            _originalVersion = model.Version;
            StatusText = $"版本 {model.Version} 已保存";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"保存失败：{ex.Message}";
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
            StatusText = "请先选择要删除的版本";
            return;
        }

        IsBusy = true;
        try
        {
            var version = SelectedItem.Version;
            var response = await DataManager.ClientRequester.Request<bool>("removeAddress", version);
            if (response.StatusCode != HttpStatusCode.OK || !response.Result)
            {
                StatusText = "删除失败，请确认登录状态";
                return;
            }

            New();
            StatusText = $"版本 {version} 已删除";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"删除失败：{ex.Message}";
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

    private bool TryBuildModel(out AddressResolverModel model, out string error)
    {
        model = new();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(Version))
        {
            error = "版本号不可为空";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ModuleName))
        {
            error = "组件名称不可为空";
            return false;
        }
        if (!TryParseAddress(BaseAddressText, out var baseAddress) || baseAddress <= 0)
        {
            error = "基址格式无效，请输入十进制或 0x 开头的十六进制数";
            return false;
        }

        var offsets = new List<long>();
        foreach (var text in OffsetsText.Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseAddress(text, out var offset) || offset < 0)
            {
                error = $"偏移 {text} 格式无效";
                return false;
            }
            offsets.Add(offset);
        }
        if (offsets.Count == 0)
        {
            error = "至少需要一个偏移";
            return false;
        }

        model = new()
        {
            Version = Version.Trim(),
            ModuleName = ModuleName.Trim(),
            BaseAddress = baseAddress,
            Offsets = [.. offsets]
        };
        return true;
    }

    private static bool TryParseAddress(string text, out long result)
    {
        result = 0;
        text = text.Trim();
        var isHex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (isHex)
            text = text[2..];
        if (!long.TryParse(text, isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return false;
        result = value;
        return true;
    }
}
