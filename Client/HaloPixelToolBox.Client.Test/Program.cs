using System.Runtime.Versioning;
using HaloPixelToolBox.Core.Utilities;

namespace HaloPixelToolBox.Client.Test;

[SupportedOSPlatform("windows")]
internal class Program
{
    [SMTest]
    public static void TestMethod()
    {
        var device = new HaloPixelDevice();
        device.Initialize();
    }
}