using XFEExtension.NetCore.AutoConfig;

namespace HaloPixelToolBox.Server.Profiles.Manage;

public partial class ServerProfile : XFEProfile
{
    [ProfileProperty]
    private string serverBindingIpAddress = "http://*:3300/";
}
