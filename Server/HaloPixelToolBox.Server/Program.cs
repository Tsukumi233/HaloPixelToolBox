using HaloPixelToolBox.Core.Models.User;
using HaloPixelToolBox.Server.Profiles.Data;
using HaloPixelToolBox.Server.Profiles.Manage;
using HaloPixelToolBox.Server.Services.Data;
using XFEExtension.NetCore.ServerInteractive.Interfaces;
using XFEExtension.NetCore.ServerInteractive.Utilities.Extensions;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server;

var bindingAddress = Environment.GetEnvironmentVariable("HALOPIXEL_SERVER_BINDING_URL") ?? ServerProfile.ServerBindingIpAddress;

var server = XFEServerBuilder.CreateBuilder()
                             .UseXFEServer()
                             .AddServerCore(XFEServerCoreBuilder.CreateBuilder()
                                                                .UseXFEStandardServerCore<MyUserFaceInfo>(options =>
                                                                {
                                                                    options.GetUserFunction = static () => UserDataProfile.MyUserTable;
                                                                    options.AddUserFunction = static userInfo => UserDataProfile.MyUserTable.Add(userInfo);
                                                                    options.GetEncryptedUserLoginModelFunction = static () => UserDataProfile.EncryptedUserLoginModelTable;
                                                                    options.AddEncryptedUserLoginModelFunction = UserDataProfile.EncryptedUserLoginModelTable.Add;
                                                                    options.RemoveEncryptedUserLoginModelFunction = UserDataProfile.EncryptedUserLoginModelTable.Remove;
                                                                    options.GetLoginKeepDays = static () => 2;
                                                                    options.LoginResultConvertFunction = static user => MyUserFaceInfo.FromUser((IUserInfo)user);
                                                                })
                                                                .AddService<AddressResolverService>()
                                                                .AddParameter("EditAddressPermission", (int)UserRole.管理员)
                                                                .AddParameter("GetPermission", (int)UserRole.管理员)
                                                                .AddParameter("AddPermission", (int)UserRole.管理员)
                                                                .AddParameter("RemovePermission", (int)UserRole.管理员)
                                                                .Build(options =>
                                                                {
                                                                    options.ServerCoreName = "HaloPixelToolBoxServer";
                                                                    options.MainEntryPoint = "api";
                                                                    options.BindIP(bindingAddress);
                                                                }))
                             .Build();

await server.Start();
