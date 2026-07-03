using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace HaloPixelToolBox.Utilities
{
    [SupportedOSPlatform("windows")]
    public static class AvatarGenerator
    {
        /// <summary>
        /// 根据中文姓氏生成头像图片（白字 + 随机深色背景）
        /// </summary>
        /// <param name="surname">中文姓氏</param>
        /// <param name="size">头像边长（像素）</param>
        /// <returns>Bitmap 对象</returns>
        public static Bitmap CreateAvatar(string surname, int size = 128)
        {
            if (string.IsNullOrWhiteSpace(surname))
                surname = "?";

            var initial = surname.Trim()[0].ToString();

            var bg = ColorFromName(initial);
            if (!IsDarkEnough(bg))
                bg = Darken(bg, 0.3);

            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using (var brush = new SolidBrush(bg))
            {
                g.FillEllipse(brush, 0, 0, size, size);
            }

            var fontSize = size * 0.45f;
            using var font = new Font("Microsoft YaHei", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            using var textBrush = new SolidBrush(Color.White);
            var rect = new Rectangle(0, 0, size, size);
            g.DrawString(initial, font, textBrush, rect, sf);

            return bmp;
        }

        private static Color ColorFromName(string name)
        {
            var hash = name.GetHashCode();
            var rnd = new Random(hash);
            return Color.FromArgb(255,
                rnd.Next(64, 192),
                rnd.Next(64, 192),
                rnd.Next(64, 192));
        }

        private static bool IsDarkEnough(Color c)
        {
            var lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            return lum < 0.6;
        }

        private static Color Darken(Color c, double factor)
        {
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - factor)),
                (int)(c.G * (1 - factor)),
                (int)(c.B * (1 - factor)));
        }
    }
}
