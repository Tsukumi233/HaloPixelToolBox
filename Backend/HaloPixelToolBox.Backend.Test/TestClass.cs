using System.Runtime.Versioning;

namespace HaloPixelToolBox.Backend.Test;

[SupportedOSPlatform("windows")]
public class TestClass
{
    [SMTest]
    public static void TestMethod()
    {
        // This is a test method that will be executed by the test runner.
        // You can add your test logic here.
        Console.WriteLine("TestMethod executed successfully.");
    }
}