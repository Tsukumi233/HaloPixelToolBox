using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using WinRT;

namespace HaloPixelToolBox.Utilities.Helpers
{
    public static class DataTransferManagerHelper
    {
        private static readonly Guid s_dtmIid = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

        private static IDataTransferManagerInterop DataTransferManagerInterop => DataTransferManager.As<IDataTransferManagerInterop>();

        public static DataTransferManager GetForWindow()
        {
            var result = DataTransferManagerInterop.GetForWindow(WindowHelper.GetHwndForCurrentWindow(), s_dtmIid);
            var dataTransferManager = MarshalInterface<DataTransferManager>.FromAbi(result);
            return (dataTransferManager);
        }

        public static void ShowShareUIForWindow(IntPtr hwnd) => DataTransferManagerInterop.ShowShareUIForWindow(hwnd);

        [ComImport]
        [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }
    }
}