using HaloPixelToolBox.Core.Models.User;
using XFEExtension.NetCore.AutoConfig;
using XFEExtension.NetCore.ServerInteractive.Models.UserModels;

namespace HaloPixelToolBox.Server.Profiles.Data;

/// <summary>
/// 用户数据配置文件
/// </summary>
public partial class UserDataProfile : XFEProfile
{
    /// <summary>
    /// 用户信息表
    /// </summary>
    [ProfileProperty]
    [ProfilePropertyAddGet("Current._myUserTable.CurrentProfile = Current")]
    [ProfilePropertyAddGet("return Current._myUserTable")]
    private ProfileList<MyUser> _myUserTable =
    [
        new()
        {
            UserName = Environment.GetEnvironmentVariable("HALOPIXEL_ADMIN_USERNAME") ?? "admin",
            Password = Environment.GetEnvironmentVariable("HALOPIXEL_ADMIN_PASSWORD") ?? "HaloPixelToolBox@2026",
            NickName = "花再工具箱管理员",
            Role = UserRole.超级管理员
        }
    ];

    /// <summary>
    /// 加密用户登录模型表
    /// </summary>
    [ProfileProperty]
    [ProfilePropertyAddGet("Current._encryptedUserLoginModelTable.CurrentProfile = Current")]
    [ProfilePropertyAddGet("return Current._encryptedUserLoginModelTable")]
    private ProfileList<EncryptedUserLoginModel> _encryptedUserLoginModelTable = [];
}
