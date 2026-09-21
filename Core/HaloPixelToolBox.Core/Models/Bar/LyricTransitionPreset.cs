namespace HaloPixelToolBox.Core.Models.Bar;

/// <summary>
/// Halo PixelBar 固件内置的歌词进入/退出动画预设。
/// 协议值固定为 1-5；TempoHub 界面中的“动画 0-4”仅是显示索引。
/// </summary>
public enum LyricTransitionPreset : byte
{
    Preset1 = 1,
    Preset2 = 2,
    Preset3 = 3,
    Preset4 = 4,
    Preset5 = 5
}
