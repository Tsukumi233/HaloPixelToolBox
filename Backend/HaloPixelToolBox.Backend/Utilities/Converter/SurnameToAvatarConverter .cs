using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Drawing.Imaging;
using XFEExtension.NetCore.StringExtension;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Utilities.Converter
{
    /// <summary>
    /// 把中文姓氏转换为一个头像
    /// </summary>
    public partial class SurnameToAvatarConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not string valueString) return null;
            if (valueString.IsNullOrWhiteSpace())
                return null;
            if (valueString.Length > 1)
                valueString = valueString[0].ToString();
            var savePath = $@"{AppPathHelper.AppCache}\{valueString}.png";
            if (!File.Exists(savePath))
                AvatarGenerator.CreateAvatar(valueString).Save(savePath, ImageFormat.Png);
            var bitmap = new BitmapImage(new(savePath));
            return bitmap;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}