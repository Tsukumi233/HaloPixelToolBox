namespace HaloPixelToolBox.Core.Models.User;

/// <summary>
/// 用户角色及权限
/// </summary>
public enum UserRole
{
    /// <summary>
    /// 超级管理员
    /// </summary>
    超级管理员 = 11,

    /// <summary>
    /// 管理员
    /// </summary>
    管理员 = 10,

    /// <summary>
    /// 普通商家用户
    /// </summary>
    普通用户 = 1,

    /// <summary>
    /// 无权限
    /// </summary>
    无权限 = 0,
}
