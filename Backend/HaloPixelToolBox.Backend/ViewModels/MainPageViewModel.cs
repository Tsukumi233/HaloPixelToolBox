using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HaloPixelToolBox.Backend.Profiles.CacheProfiles;
using HaloPixelToolBox.Backend.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Core.Models.User;
using HaloPixelToolBox.Core.Utilities;
using System.Net;
using XFEExtension.NetCore.ServerInteractive.Models.RequesterModels;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;

namespace HaloPixelToolBox.Backend.ViewModels;

public partial class MainPageViewModel : ViewModelBase
{
    [ObservableProperty] public partial string ServerAddress { get; set; } = SystemProfile.ServerAddress;
    [ObservableProperty] public partial string Account { get; set; } = CacheProfile.Account;
    [ObservableProperty] public partial string Password { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = "尚未连接服务器";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsLoggedIn { get; set; }

    public IAutoNavigationParameterService<string> AutoNavigationParameterService { get; set; } = ServiceManager.GetService<IAutoNavigationParameterService<string>>();

    public MainPageViewModel()
    {
        AutoNavigationParameterService.ParameterChange += AutoNavigationParameterService_ParameterChange;
        _ = RestoreSessionAsync();
    }

    partial void OnServerAddressChanged(string value) => SystemProfile.ServerAddress = value.Trim();

    private void AutoNavigationParameterService_ParameterChange(object? sender, string? e) { }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Account) || string.IsNullOrWhiteSpace(Password))
        {
            StatusText = "请输入管理员账号和密码";
            return;
        }

        IsBusy = true;
        try
        {
            StatusText = "正在连接服务器...";
            if (!await DataManager.InitializeAsync([ServerAddress], true))
            {
                StatusText = "服务器连接失败，请检查地址和服务器状态";
                return;
            }

            var response = await DataManager.ClientRequester.Request<UserLoginResult<MyUserFaceInfo>>("login", Account.Trim(), Password);
            if (response.StatusCode != HttpStatusCode.OK || response.Result?.UserInfo is null)
            {
                StatusText = "登录失败，请检查账号或密码";
                return;
            }

            if (response.Result.UserInfo.PermissionLevel < (int)UserRole.管理员)
            {
                DataManager.ClientRequester.Session = string.Empty;
                StatusText = "该账号没有管理员权限";
                return;
            }

            CacheProfile.Session = response.Result.Session;
            CacheProfile.Account = Account.Trim();
            SystemProfile.ServerAddress = ServerAddress.Trim();
            IsLoggedIn = true;
            Password = string.Empty;
            StatusText = $"已登录：{response.Result.UserInfo.NickName}（{(UserRole)response.Result.UserInfo.PermissionLevel}）";
        }
        catch (Exception ex)
        {
            StatusText = $"登录失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        IsBusy = true;
        try
        {
            var connected = await DataManager.InitializeAsync([ServerAddress], true);
            StatusText = connected ? "服务器连接正常，请登录" : "服务器连接失败";
            if (connected && !string.IsNullOrWhiteSpace(CacheProfile.Session))
                await RestoreSessionAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Logout()
    {
        DataManager.ClientRequester.Session = string.Empty;
        CacheProfile.Session = string.Empty;
        IsLoggedIn = false;
        StatusText = "已退出登录";
    }

    private async Task RestoreSessionAsync()
    {
        if (string.IsNullOrWhiteSpace(CacheProfile.Session))
            return;

        IsBusy = true;
        try
        {
            if (!await DataManager.InitializeAsync([ServerAddress]))
            {
                StatusText = "服务器连接失败";
                return;
            }

            DataManager.ClientRequester.Session = CacheProfile.Session;
            var response = await DataManager.ClientRequester.Request<MyUserFaceInfo>("relogin");
            if (response.StatusCode == HttpStatusCode.OK &&
                response.Result is { } user &&
                user.PermissionLevel >= (int)UserRole.管理员)
            {
                IsLoggedIn = true;
                StatusText = $"已恢复登录：{user.NickName}（{(UserRole)user.PermissionLevel}）";
                return;
            }

            Logout();
            StatusText = "登录已过期，请重新登录";
        }
        catch (Exception ex)
        {
            StatusText = $"恢复登录失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
