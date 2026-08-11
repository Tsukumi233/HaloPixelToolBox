namespace HaloPixelToolBox.Core.Models.Bar;

/// <summary>
/// 地址解析模型
/// </summary>
public class AddressResolverModel
{
    /// <summary>
    /// 目标版本
    /// </summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>
    /// 组件名称
    /// </summary>
    public string ModuleName { get; set; } = string.Empty;
    /// <summary>
    /// 基础地址
    /// </summary>
    public long BaseAddress { get; set; }
    /// <summary>
    /// 偏移
    /// </summary>
    public long[] Offsets { get; set; } = [];

    /// <summary>
    /// 供管理界面显示的十六进制基址
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string BaseAddressHex => $"0x{BaseAddress:X}";

    /// <summary>
    /// 供管理界面显示的十六进制偏移
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string OffsetsHex => string.Join(", ", Offsets.Select(offset => $"0x{offset:X}"));
}
