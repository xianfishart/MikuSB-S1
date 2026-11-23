# MikuSB-S1

个人维护的 MikuSB 服务端修改版（本地 S1 环境）。上游项目：[dubstream3/MikuSB](https://github.com/dubstream3/MikuSB)。

本仓库只包含**代码与配置样例**，不含游戏资源、程序集、数据库和日志。仓库里的前端与脚本是部署副本，权威副本在运行目录 `S1\Web`、`S1\tools`。

> 上游是 C# 源码工程（`MikuSB/SdkServer/GameServer/...`）。本仓库的代码是从发布版程序集**反编译后修改**得到的单一工程 `MikuGameServer.csproj`，目录结构与上游源码树不同，**不能与上游分支直接合并**。改动说明见 `docs/CHANGELOG.md`。

## 目录

```
MikuGameServer.csproj      单一工程，dotnet build 产出 MikuGameServer.dll
S1Admin/                   GM 后台、调试口（本地新增模块）
MikuSB.GameServer.*/       游戏逻辑与协议处理（反编译 + 修改）
runtime/web/               GM 网页前端      → 部署到 <S1>\Web
runtime/config/            配置样例与小开关   → 部署到 <S1>\Config
runtime/tools/             登录背景相关脚本
docs/CHANGELOG.md          改动清单
LICENSE                    MIT（沿用上游）
```

## 构建

需要 .NET 10 SDK。

```
dotnet build -c Release
```

产物 `bin/Release/net10.0/MikuGameServer.dll`，覆盖到运行目录即可（覆盖前先备份旧 dll）。

## 运行目录结构

```
S1\
├─ MikuSB.exe                上游 release 的程序宿主与运行时依赖（不在本仓库）
├─ MikuGameServer.dll        本仓库编译产物
├─ Config\
│  ├─ Config.json            从 runtime/config/Config.example.json 复制后修改
│  └─ Config.Local.json      机器相关覆盖（BindAddress 等），可选
├─ Web\                      runtime/web 的三个文件
├─ Resources\                游戏配置表与资源（不在本仓库）
└─ tools\
```

`Config.json` 里必须改的两处：

- `Loader.GamePath`：游戏 `Game.exe` 的实际路径
- `HttpServer.AdminToken`：GM 后台口令，随便设一个随机串

口令更推荐写到独立的 `Config/AdminToken.txt`（一行文本）：启动流程会按默认值重写 `Config.json`，
写在里面的口令可能被冲掉；而 `ConsoleHost` 读取顺序是 **AdminToken.txt → Config.json 的 AdminToken → 占位值**。

## GM 后台

浏览器打开 `http://127.0.0.1:21400/`，口令就是 `HttpServer.AdminToken` 的值（网页会存到 localStorage 的 `mikush.admin.token`）。

调试口只监听 `127.0.0.1`。源码里**不写死口令**，`S1Admin/ConsoleHost.cs` 优先读 `Config/AdminToken.txt`，
其次读 `Config/Config.json` 的 `HttpServer.AdminToken`，都没有时用占位值 `dev-admin-token`。

## 改动摘要

- **GM 后台**：活动复刻页按期列表，每期一张卡 + 一颗「登录背景 N 号」按钮；`/api/replay`、`/api/loginbg` 两个调试接口；切期与换背景解耦。
- **活动复刻**：期号 pin、DLC 主入口横幅隐藏、`Config/EraLite.json` 的轻量复刻期（`dlcEra` 0=游戏默认，N=固定第 N 期，改文件即生效）。
- **登录背景**：用 mods 覆盖 pak 替换 `Game/Content/Startup/Settings/login.txt`，0~30 共 31 套；`tools/loginbg.py` 负责生成 pak，`loginbg_table.py` 维护「期号 → 编号」对照表。
- **前端**：换背景提示改为常驻可关，补全 toast 的显示/隐藏样式。

详细条目见 [`docs/CHANGELOG.md`](docs/CHANGELOG.md)。

## 注意

- 不含任何游戏资源、二进制或主数据；`Resources/`、`Config/Database/`、`*.pak` 都在 `.gitignore` 里。
- 仅用于本地研究与测试，请勿用于官方服务或商业用途。所有游戏相关商标与版权归其权利人所有。

## 许可

MIT，沿用上游（Copyright (c) 2026 Kei-Luna, Naruse and contributors）。
