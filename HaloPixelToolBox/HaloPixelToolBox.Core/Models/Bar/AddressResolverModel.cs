namespace HaloPixelToolBox.Core.Models.Bar;

/// <summary>
/// 地址解析模型
/// </summary>
public class AddressResolverModel
{
    /// <summary>
    /// 组件名称
    /// </summary>
    public string ModuleName { get; set; } = string.Empty;
    /// <summary>
    /// 基础地址
    /// </summary>
    public nint BaseAddress { get; set; }
    /// <summary>
    /// 偏移
    /// </summary>
    public nint[] Offsets { get; set; } = [];
}
