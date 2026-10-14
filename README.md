# TraeTools

> 极简 Trae 签到助手：本地一键签到 · 云端自动托管
> 支持 **Windows 桌面端** 与 **Android 手机端**（APK）

TraeTools 是对 **TRAE-Checkin** 的重构版，用 Avalonia 重写为现代化跨平台应用（.NET 10）。
**2.0 做了产品减法**：去除用量统计、账号切换与繁复设置，界面收敛为**单页仪表盘**——本地签到、云端托管、最近记录三块，专注、清晰、一次点完。

> **2.0 极简重构（Less is More）**：原「仪表盘 / 用量统计 / 每日签到 / 账号切换 / 云端签到 / 设置」六页导航已合并为**单页 + 二级设置**；用量统计与账号切换两块能力整体下线。已登录账号、签到历史与云端部署状态**完全保留**，升级后无需重新配置。

> **2026-10 客户端合并公告**：字节跳动已于 2026-10-09 将 TraeWork 与 TraeCode 合并为统一的 **TRAE** 客户端（Agent / IDE 双模式），TraeWork 于 2026-10-23 停服。本工具已同步适配：设备号解析、客户端进程/目录探测均覆盖新旧名称（TRAE / TRAE CN / TRAE SOLO CN），老用户升级客户端后无需任何操作。

## 功能特性

- **本地一键签到**：打开即点，秒签；多账号时出现极简账号勾选行（勾谁签谁，自动签到与此一致）
- **云端托管签到（永不掉线）**：一键把签到任务部署到你自己的 GitHub fork（GitHub Actions 每天 08:00 运行），无需电脑/手机开机；部署进度以竖向流程清单可视化
- **极简运行日志**：首页仅展示最近 3 条签到记录，只做追溯、不做统计
- **云端状态徽章**：顶栏常驻「🟢 云端运行中 / 🟡 待部署 / 🔴 云端未部署」，点击展开云端详情
- **账号资料**：展示每个账号的昵称 / 脱敏手机号 / 头像 / 学生认证徽章（在设置中管理）
- **飞书推送**：签到结果可实时推送到飞书机器人
- **应用更新（不强制）**：内置检查更新，发现新版仅提示；Windows 端经用户确认后才下载并后台替换，Android 端仅引导至下载页手动安装——均不强制，可随时取消

## 界面

| 区域 | 说明 |
|---|---|
| 顶栏 | 小号加粗标题「签到助手」+ 云端状态徽章（可点击展开详情）+ 设置图标 |
| 本地签到卡片 | 主标题 + 副文案 + 主色胶囊按钮「一键签到」；多账号时附账号勾选 |
| 云端自动签到卡片 | 授权引导 / 竖向部署流程清单（GitHub 授权 → Secret 写入 → 定时任务）+ 一键授权部署 CTA + 次数弱化的「打开 Actions 面板 / 粘贴 Token(PAT)」 |
| 最近记录 | 极简列表，仅最近 3 条（时间 / 账号 / 结果），无卡片包裹，仅淡分割线 |
| 设置（二级页） | 自动签到配置、Token 信息、飞书推送、账号管理、关于 |

## 快速开始

### 方式一：直接使用

在 [Releases](https://github.com/akexin/TraeTools-Android/releases) 下载最新版：

| 平台 | 产物 | 说明 |
|---|---|---|
| Windows | `TraeTools.exe` / `TraeTools-win-x64.zip` | 单文件自包含，无需安装 .NET 运行时 |
| Android | `TraeTools-Android.apk` | 支持 Android 8.0（API 26）及以上，下载后需允许「安装未知来源应用」 |

> Android 版与 Windows 版数据相互独立（加密机制不同），账号需在手机端重新登录一次。

### 方式二：源码编译

需要 **.NET 10 SDK**（Android 目标需额外安装 workload）：

```bash
git clone https://github.com/akexin/TraeTools-Android.git
cd TraeTools-Android

# Windows 桌面版（多目标项目须显式指定 -f）
dotnet build -c Release -f net10.0-windows

# Android 手机版（首次需安装 Android workload，约 1.5GB）
dotnet workload install android
dotnet publish TraeTools.csproj -c Release -f net10.0-android -o publish-android
# 产物：publish-android/cn.akexin.traetools-Signed.apk

# 非 Windows 系统（Linux/macOS）构建 Android 时需追加 -p:EnableWindowsTargeting=true
# （多目标项目还原会一并还原 net10.0-windows，需允许下载 Windows 目标包）
```

## 使用指南

### 1. 添加 / 登录账号

- **Windows**：`设置 → 账号管理 → + 添加`：自动打开登录窗口（内嵌浏览器），手机号+验证码登录后自动读取凭证并保存；登录态约 14 天，失效可一键静默续期。
- **Android**：`设置 → 账号管理 → + 添加` → 「打开浏览器登录」→ 在浏览器登录 trae.cn 后复制 **Cloud-IDE-Token** 粘贴保存（手机端无内嵌浏览器，手动录入为唯一方式）。登录态约 14 天，失效后重新粘贴即可。

### 2. 本地签到

首页「本地签到」卡片点「一键签到」即可；多个账号时卡片内会出现勾选行，勾选即为要签的账号。也可在设置中开启「每日自动签到」，到点自动执行。每次结果会写入本地历史，并出现在首页「最近记录」。

> Android 端自动签到仅在 App 运行期间有效；需要无人值守签到请使用「云端签到」（GitHub Actions 每日 08:00 执行，与手机状态无关）。

### 3. 云端签到（部署到 GitHub Actions）

1. 首页「云端自动签到」卡片点击主按钮（首次为「授权 GitHub」；设备码授权，无需密码，仅需 repo 权限）
2. 再次点击（此时为「一键部署到云端」）：程序自动 fork 本仓库 → 写入每个账号的 `TRAE_SESSION` / `TRAE_DEVICE_ID` secret → 启用并触发验证运行；部署进度在卡片内的竖向流程清单上实时更新
3. 验证成功即生效：**每天北京时间 08:00** 自动签到，即使本机关机也不中断；顶栏徽章变为 🟢 云端运行中

部署原理：fork 仓库中的 `.github/workflows/checkin.yml` 定时运行 `checkin.py`，脚本用 `X-Cloudide-Session` 换取新 JWT 后调用签到接口（仅依赖 Python 标准库）。

## 常见问题

| 现象 | 说明 / 处理 |
|---|---|
| 签到提示「当前参与用户太多」 | Trae 风控：同设备/网络频繁请求触发。建议错开 08:00 高峰、减少账号数、不同账号用独立设备号 |
| 云端部署 HTTP 401 | GitHub 授权失效或权限不足：点「重新授权」；仍失败则自动删除旧 fork 重建 |
| 云端提示「未找到 workflow」 | 旧 fork 早于本仓库添加 checkin.yml：按提示一键「删除并重建 fork」即可自愈 |
| 签到 0 积分 | 当日已签过（重复签到不加分），或非会员当日基础 150 分已到账 |
| Token 失效 | 程序会用 `X-Cloudide-Session` 静默换新；彻底失效时提示重新登录 |
| Android 安装 APK 被拦截 | 系统设置中允许浏览器/文件管理「安装未知来源应用」后重试 |
| Android 与 Windows 账号不互通 | 两端加密机制不同（DPAPI / Keystore），数据独立，需分别登录 |

> 云端签到凭证 `TRAE_SESSION` 有效期约 14 天，过期后请在软件内重新登录并重新部署。

## 开发者

- 技术栈：Avalonia 12 / .NET 10 / CommunityToolkit.Mvvm / Sodium.Core / WebView2（Windows）
- 目录：`ViewModels/` 页面逻辑、`Services/` 网络与领域服务、`Views/` 界面、`Models/` 数据模型、`Platform/` 跨平台抽象（DPAPI/Keystore、浏览器启动等）
- 多目标：`net10.0-windows`（WinExe 单文件）与 `net10.0-android`（APK），平台差异集中在 `Platform/` 与 `#if ANDROID` 条件编译
- 行为默认不对外请求官方非公开接口之外的能力；用量/积分接口为逆向网页接口，字段随官方可能变化，代码均做容错解析

## 免责声明

- 本项目为个人开源工具，与 ByteDance / Trae 官方无任何关系
- 使用自动签到、云端托管等功能请遵守 Trae 服务条款，风险自负
- 请勿出售、外借或用于非法用途；账号凭证仅保存在你自己的设备/GitHub secret 中

## 致谢

- [TRAE-Checkin](https://github.com/star620/TRAE-Checkin)：本项目的前身，云端签到体系（checkin.py + GitHub Actions 流程）源自该仓库

> 2.0 起已移除对 [Trea-Switch](https://github.com/star620/Trea-Switch)（账号切换 / vault 建档）能力的集成与依赖。

## 许可证

见 [LICENSE](LICENSE)。
