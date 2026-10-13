# TraeTools

> Trae 一体化管理工具：多账号每日签到 · 云端自动签到 · 账号快速切换 · 用量统计
> 支持 **Windows 桌面端** 与 **Android 手机端**（APK）

TraeTools 是对 **TRAE-Checkin** 的重构版，并集成了 **Trea-Switch** 的账号切换能力，用 Avalonia 重写为现代化跨平台应用（.NET 10）。一个工具覆盖 Trae 账号的「登录 → 签到 → 切换 → 用量查看 → 云端托管」全流程。

> **2026-10 客户端合并公告**：字节跳动已于 2026-10-09 将 TraeWork 与 TraeCode 合并为统一的 **TRAE** 客户端（Agent / IDE 双模式），TraeWork 于 2026-10-23 停服。本工具已同步适配：设备号解析、客户端进程/目录探测均覆盖新旧名称（TRAE / TRAE CN / TRAE SOLO CN），老用户升级客户端后无需任何操作。

## 功能特性

- **多账号每日签到**：一页管理多个 Trae 账号，一键签到全部启用账号；自动签到可设定时间，到点自动执行
- **云端签到（永不掉线）**：一键把签到任务部署到你自己的 GitHub fork（GitHub Actions 每天 08:00 运行），无需电脑/手机开机
- **账号切换**（仅 Windows 桌面端）：为每个账号备份登录态（vault），一键冷切换客户端账号，失败自动回滚
- **用量统计**：按会话展示模型 / 输入输出 Token / 缓存命中率 / 积分消耗，并本地持久化；另含「积分快过期提醒」
- **账号资料**：展示每个账号的昵称 / 脱敏手机号 / 头像 / 学生认证徽章
- **飞书推送**：签到结果可实时推送到飞书机器人
- **应用更新（不强制）**：内置检查更新，发现新版仅提示并引导下载，绝不强制安装、不自动替换

## 界面

| 导航 | 说明 |
|---|---|
| 仪表盘 | 剩余积分、今日签到状态、积分趋势图、账号概览 |
| 用量统计 | 逐会话用量 + 模型分布 + 汇聚统计，全部本地留档 |
| 每日签到 | 本月日历、连签天数、签到记录、一键/自动签到 |
| 账号切换 | 建档（备份登录态）、一键切换、校验（**仅 Windows 桌面端**） |
| 云端签到 | GitHub 授权、一键部署、部署状态与日志 |
| 设置 | 自动签到配置、Token 信息、飞书推送、账号管理、关于 |

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

# Windows 桌面版
dotnet build -c Release

# Android 手机版（首次需安装 Android workload，约 1.5GB）
dotnet workload install android
dotnet publish TraeTools.csproj -c Release -f net10.0-android -o publish-android
# 产物：publish-android/cn.akexin.traetools-Signed.apk
```

## 使用指南

### 1. 添加 / 登录账号

- **Windows**：`设置 → 账号管理 → + 添加`：自动打开登录窗口（内嵌浏览器），手机号+验证码登录后自动读取凭证并保存；登录态约 14 天，失效可一键静默续期。
- **Android**：`设置 → 账号管理 → + 添加` → 「打开浏览器登录」→ 在浏览器登录 trae.cn 后复制 **Cloud-IDE-Token** 粘贴保存（手机端无内嵌浏览器，手动录入为唯一方式）。登录态约 14 天，失效后重新粘贴即可。

### 2. 每日签到

签到页点「一键签到」或开启「每日自动签到」；也可在仪盘点「立即签到」。签到结果写入本地历史，日历与连签天数随之更新。

> Android 端自动签到仅在 App 运行期间有效；需要无人值守签到请使用「云端签到」（GitHub Actions 每日 08:00 执行，与手机状态无关）。

### 3. 用量统计

进入「用量统计」页面自动同步近 7 天会话；每次会话展示模型、入/出 Token、缓存命中字节与积分消耗，数据按账号本地保留，可追溯历史趋势。

### 4. 云端签到（部署到 GitHub Actions）

1. `云端签到` 页点击「授权 GitHub」（设备码授权，无需密码，仅需 repo 权限）
2. 点击「一键部署到云端」：程序自动 fork 本仓库 → 写入每个账号的 `TRAE_SESSION` / `TRAE_DEVICE_ID` secret → 启用并触发验证运行
3. 验证成功即生效：**每天北京时间 08:00** 自动签到，即使本机关机也不中断

部署原理：fork 仓库中的 `.github/workflows/checkin.yml` 定时运行 `checkin.py`，脚本用 `X-Cloudide-Session` 换取新 JWT 后调用签到接口（仅依赖 Python 标准库）。

### 5. 账号切换（仅 Windows 桌面端）

`账号切换` 页：为当前登录的客户端「建档」（备份登录态载体）→ 选择目标账号 → 「切换至此」一键冷切换；切换失败会自动回滚到原账号并重启客户端。账号删空后可直接「建档」输入新名称重新建立。

> 该功能依赖操作本机 TRAE 客户端的数据目录与进程，Android 端不提供。

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
- 使用自动签到、账号切换等功能请遵守 Trae 服务条款，风险自负
- 请勿出售、外借或用于非法用途；账号凭证仅保存在你自己的设备/GitHub secret 中

## 致谢

- [TRAE-Checkin](https://github.com/star620/TRAE-Checkin)：本项目的前身，云端签到体系（checkin.py + GitHub Actions 流程）源自该仓库
- [Trea-Switch](https://github.com/star620/Trea-Switch)：账号切换（vault 建档 / 冷切换 / 守护回滚）能力源自该仓库

## 许可证

见 [LICENSE](LICENSE)。
