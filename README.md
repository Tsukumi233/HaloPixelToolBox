# 🎧 HaloPixelToolBox（花再音响工具箱）

一个面向桌面端的实用工具箱，致力于为音响 / 桌面设备提供更丰富、更直观的信息展示能力。

---

## 🚧 当前开发进度

- ✅ 已支持 **网易云音乐歌词显示**
- ✅ 客户端可从服务器动态获取不同版本的内存解析地址，并在本地缓存；服务器暂时不可用时自动回退缓存
- ✅ 提供独立管理员端，可管理版本解析地址和 IP 黑名单
- 🚀 其它音乐平台歌词正在开发中

> 项目仍处于持续迭代阶段，功能和体验都会不断完善。

---

## 🖥️ 服务端与管理员端

服务端默认监听 `http://localhost:3300/`，客户端和管理员端填写的请求地址为 `http://localhost:3300/api`。如需允许其它设备访问，可通过环境变量修改监听地址和首次创建的管理员账号：

| 环境变量 | 默认值 | 说明 |
| --- | --- | --- |
| `HALOPIXEL_SERVER_BINDING_URL` | `http://localhost:3300/` | 服务端 HTTP 监听地址 |
| `HALOPIXEL_ADMIN_USERNAME` | `admin` | 首次生成用户配置时创建的超级管理员账号 |
| `HALOPIXEL_ADMIN_PASSWORD` | `HaloPixelToolBox@2026` | 首次生成用户配置时创建的超级管理员密码 |

首次部署前请务必设置自己的管理员密码。环境变量只影响首次创建的用户配置；已生成配置后，账号数据以服务端配置文件为准。

启动项目：

```powershell
dotnet run --project Server/HaloPixelToolBox.Server/HaloPixelToolBox.Server.csproj
dotnet run --project Backend/HaloPixelToolBox.Backend/HaloPixelToolBox.Backend.csproj -p:Platform=x64
dotnet run --project Client/HaloPixelToolBox.Client/HaloPixelToolBox.Client.csproj -p:Platform=x64
```

管理员端登录后可以：

- 查看、新增、修改和删除不同网易云音乐版本的模块名、基址和偏移链
- 查看、封禁和解封 IPv4/IPv6 地址，并填写备注

服务端绑定非本机地址时，Windows 可能要求管理员预留 URL ACL 并开放对应防火墙端口。管理员端与客户端的服务器地址始终需要包含 `/api`。

端到端协议测试（需先启动服务端）：

```powershell
dotnet run --project Server/HaloPixelToolBox.Server.Test/HaloPixelToolBox.Server.Test.csproj -- --integration
```

---

## 📝 提交 Issue

如果你在使用过程中遇到问题，或有新的功能想法，欢迎提交 Issue。

- 🐞 Bug 报告 / ✨ 功能建议均可
- Issue **支持使用中文**
- 提交前请阅读 👉 [Issue 提交说明](./CONTRIBUTING.md)

---

## 🔮 计划支持的功能

- 🎵 更多音乐软件的歌词显示
- 💬 社交软件消息推送
- 🎨 更多个性化与自定义内容
