# 改动清单

按模块归并，条目前后不代表优先级。日期为本地开发时间。

## S1Admin（GM 后台 / 调试口）

- 新增 `ConsoleHost`：`TcpListener` 只绑 `127.0.0.1:21400`，静态页面 + JSON 接口，Bearer 鉴权。
  **口令不再硬编码**，读取顺序为 `Config/AdminToken.txt` → `Config/Config.json` 的
  `HttpServer.AdminToken` → 占位值 `dev-admin-token`。
  之所以优先独立文件：启动流程会按默认值重写 `Config.json`，写在里面的口令会被冲掉（2026-11-23）。
- `AdminApi` 路由：
  - `GET /api/replay` — 期号列表（id / 名称 / 起止时间）
  - `POST /api/replay` — 切换期号、清除
  - `GET /api/loginbg` — `{ok, current, pending, map}`
  - `POST /api/loginbg` — `{era}` 按映射换背景，或 `{index}` 直接指定编号
- `LoginBgReplay`：把 `Resources/loginbg/bg-NN.pak` 复制成
  `Game/Content/Paks/mods/Patch_Miku_LoginBG_100_P.pak`；编号范围 0~30；
  `current.txt` 记录当前编号，`pending.txt` 处理「客户端占用 pak 写不进去」的情况，
  下次启动前由 `ApplyPending()` 补做。
- 切期与换背景**解耦**：`ApplyReplay` 不再顺带换背景，换背景只认 GM 卡片按钮或 `/api/loginbg`。
- `DlcClientPatches` / `DlcMainEntry`：DLC 活动入口横幅按**运行时** pin 判定隐藏，
  修复轻量复刻期下构建期期号算错、横幅收不起来的问题。
- `EraLite`：`Config/EraLite.json` 的 `lite`（轻量复刻期列表）与 `dlcEra`
  （0 = 游戏默认，N = 固定第 N 期），按文件 mtime 热加载，改完即生效。
- `ConfigKeeper`：启动时补齐缺失的 `HttpServer.*` 配置项，维护 pin 快照。

## 游戏逻辑

- `DlcEraState.PickDefaultEraId`：`EraLite.DlcEra == 0` 时继续走默认算法
  （当前期是否开放 → 最新开放期 → 0），不再回退到最新一期。
- `CommandGame.Launch`：启动客户端前先 `LoginBgReplay.ApplyPending()`，
  把上次没写成功的背景切换补做掉。
- `DlcMainEntry.DumpAndCall`：客户端侧取文件由 `pcall(dofile, path)` 改为
  `io.open` + `loadstring`，修复部分环境下 dofile 静默失效。

## Web（`runtime/web`）

- 活动复刻页：每期一张卡，卡片下方一颗「登录背景 N 号」按钮，
  点击按该期映射换背景，不动版本 pin；在用编号用绿框标记。
- 6 个新 i18n 键 × 10 种语言。
- `toast()` 支持 `sticky`（常驻、点击关闭）与自定义时长；补齐 `.toast` 的
  默认隐藏 / `.show` 显示样式（此前 `remove('show')` 实际不生效）；
  `sticky` 时右上角画 `×`。错误态类名由 `error` 改为 `err`，与 CSS 对齐。

## tools

- `loginbg.py` — 生成覆盖 pak。`--list` 列编号、`--set N` 切换、`--reset` 恢复官方；
  支持 `SCENE_ASSETS` 处理不按 `login_bgNNN` 命名的场景（如 `login_dlc01_01`）。
- `loginbg_capture.py` — 自动进登录界面截图，用于实拍校准编号。
- `loginbg_index.py` — 生成「编号总览」图与文字版一览。
- `loginbg_table.py` — 维护桌面《登录背景-复刻期对照表》，`--sync` 写回 `Resources/loginbg/map.txt`；
  末行 `99` 是「游戏默认」按钮对应的编号。
- `loginbg_sheet.py` — 对照表导出为表格。

## 已知遗留

- 客户端缺 `Entry_01c`、`Entry_01d` 两个流式子关卡包，日志有 Display 级提示，不影响进图。
- 登录后进大厅前有 3 秒左右的加载屏等待（UMG/字体/材质同步加载），属客户端固有开销。
