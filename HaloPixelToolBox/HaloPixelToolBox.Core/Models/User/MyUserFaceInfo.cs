using XFEExtension.NetCore.ServerInteractive.Interfaces;

namespace HaloPixelToolBox.Core.Models.User;

/// <summary>
/// 用户面部信息
/// </summary>
public class MyUserFaceInfo : IUserFaceInfo
{
    /// <summary>
    /// 用户 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 昵称
    /// </summary>
    public string NickName { get; set; } = string.Empty;

    /// <summary>
    /// 权限等级
    /// </summary>
    public int PermissionLevel { get; set; }

    /// <summary>
    /// 转换为字符串
    /// </summary>
    /// <returns>用户信息字符串</returns>
    public override string ToString() => $"{NickName}({(UserRole)PermissionLevel})";

    /// <summary>
    /// 从用户信息创建用户面部信息
    /// </summary>
    /// <param name="user">用户信息</param>
    /// <returns>用户面部信息</returns>
    public static MyUserFaceInfo FromUser(IUserInfo user) => new()
    {
        Id = user.Id,
        NickName = user.NickName,
        PermissionLevel = user.PermissionLevel
    };

    /// <summary>
    /// 相等运算符重载
    /// </summary>
    public static bool operator ==(MyUserFaceInfo? left, MyUserFaceInfo? right)
    {
        if (right is not null && right.Equals(left))
            return true;
        if (left is null || right is null)
            return false;
        return left.Id == right.Id;
    }

    /// <summary>
    /// 不相等运算符重载
    /// </summary>
    public static bool operator !=(MyUserFaceInfo? left, MyUserFaceInfo? right) => !(left == right);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj?.GetHashCode() == GetHashCode();

    /// <inheritdoc/>
    // ReSharper disable once NonReadonlyMemberInGetHashCode
    public override int GetHashCode() => Id.GetHashCode();
}
