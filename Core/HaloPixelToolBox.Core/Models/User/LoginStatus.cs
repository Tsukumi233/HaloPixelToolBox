using XFEExtension.NetCore.ServerInteractive.Models.RequesterModels;

namespace HaloPixelToolBox.Core.Models.User;

/// <summary>
/// 登录状态
/// </summary>
/// <param name="isLogin"></param>
/// <param name="userInfo"></param>
public class LoginStatus(bool isLogin, UserLoginResult<MyUserFaceInfo>? userInfo = null)
{
    /// <summary>
    /// 是否登录
    /// </summary>
    public bool IsLogin { get; set; } = isLogin;
    /// <summary>
    /// 登录结果
    /// </summary>
    public UserLoginResult<MyUserFaceInfo>? LoginResult { get; set; } = userInfo;
}
