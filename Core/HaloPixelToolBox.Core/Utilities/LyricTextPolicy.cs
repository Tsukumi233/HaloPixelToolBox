using System.Globalization;

namespace HaloPixelToolBox.Core.Utilities;

/// <summary>
/// Guards the firmware lyric channel from blank lines. A source-0x0E packet that
/// contains only spacing/control characters makes the A160 expose its underlying
/// clock page even though the lyric session itself is still enabled.
/// </summary>
public static class LyricTextPolicy
{
    public static bool HasVisibleContent(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character) &&
                !char.IsControl(character) &&
                char.GetUnicodeCategory(character) != UnicodeCategory.Format)
            {
                return true;
            }
        }

        return false;
    }
}
