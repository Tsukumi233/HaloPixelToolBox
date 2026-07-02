namespace HaloPixelToolBox.Core.Models.User;

/// <summary>
/// 我的用户信息
/// </summary>
public class MyUser : XFEExtension.NetCore.ServerInteractive.Models.UserModels.User
{
    /// <summary>
    /// 用户角色
    /// </summary>
    public UserRole Role { get => (UserRole)PermissionLevel; set => PermissionLevel = (int)value; }
}
