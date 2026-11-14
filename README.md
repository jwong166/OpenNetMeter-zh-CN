# OpenNetMeter 简体中文版

Windows 网络流量监控工具 [OpenNetMeter](https://github.com/Ashfaaq18/OpenNetMeter) 的**简体中文界面版本**。

- 上游版本：`0.16.0`（commit `077fd70`）
- 汉化原则：只改界面语言，功能、布局、配色、数据库结构、导出格式与官方版完全一致
- 分发形式：免安装、免运行时的自包含单文件 exe
- 许可：Apache License 2.0（`LICENSE` / `NOTICE` 原样保留）

> 本项目为**非官方汉化**，与原作者 Ashfaaq Riphque 无隶属关系。上游程序相关的问题请提交至[官方仓库](https://github.com/Ashfaaq18/OpenNetMeter/issues)。
> 翻译和新增功能来自DeepSeek-V4.1-Flash-1017，思考强度极致，含人量基本为0

## 下载与运行

从本仓库的 **Releases** 页面下载最新版：

```
OpenNetMeter-0.16.0-zhCN-win-x64.exe
```

**运行要求**

| 项目 | 说明 |
| --- | --- |
| 系统 | Windows 10 / 11，x64 |
| 运行时 | 无需安装 .NET（已内置，单文件约 70–90 MB） |
| 权限 | **需要管理员权限**：程序通过 ETW 内核会话统计各进程流量 |
| 磁盘写入 | 设置与用量数据库位于 `%LOCALAPPDATA%\OpenNetMeter` |

程序为绿色单文件，无需安装、不写注册表；但**数据不是"绿色"的**——卸载/删除 exe 后，历史记录仍保留在上述目录，需要手动清理。

**安全提示**：exe 未做代码签名，首次运行时 Windows SmartScreen 可能提示"已保护你的电脑"。确认来源后点击"更多信息 → 仍要运行"。可用 Release 说明中的 SHA-256 校验下载完整性：

```
certutil -hashfile OpenNetMeter-0.16.0-zhCN-win-x64.exe SHA256
```

---

## 功能

与官方版本一致：

- 实时上传/下载速率与曲线图
- 当前会话流量、指定起始日期的累计流量
- 近七天用量趋势与环比
- 各进程实时与累计流量，支持排序与在线搜索
- 按进程/按日期/按网络配置查询历史用量，支持 CSV、JSON、HTML 报告导出
- 按进程录制一段时间的流量并导出
- 迷你悬浮挂件（可置顶、可调透明度、可自由拖动）
- 托盘常驻、开机自启、深色模式

本版本新增（详见 [新功能说明.md](新功能说明.md)）：

- **界面语言切换**：英语 / 简体中文 / 繁體中文，切换立即生效，无需重启
- 悬浮窗在**全屏应用时自动隐藏**，退出全屏后恢复
- 概览支持**按进程名搜索**、**仅显示流量前 N 名**
- 概览进程**右键菜单**：打开文件所在位置、复制进程名
- 历史支持**按进程名筛选**并查看**按日明细**
- 新增**流量明细导出**（进程名、日期、流量类型、当日累计、历史累计）

## 与官方版的差异

仅界面文案与界面区域设置，详见 [汉化说明.md](汉化说明.md)。需要特别说明的三点：

1. **CSV 表头与 JSON 字段名保持英文**（`Date,Adapter,Process,DownloadBytes,UploadBytes` 等），以免破坏已有的脚本与模板。
2. **数据目录、程序集名称、单实例标识未改动**，因此中文版与官方版共用同一份历史数据，且**不能同时运行**。若需完全隔离，见汉化说明第 4 节的三处改法。
3. **MSI 安装界面未汉化**：本项目只发布绿色单文件，不提供安装包。

## 已知问题

- 未做代码签名，可能触发 SmartScreen 或个别杀软的启发式拦截（自解压单文件 + ETW 抓包行为容易被误判）。可自行核对 SHA-256，或用源码构建。
- "检查更新"仍指向上游仓库，因此会提示官方新版本，下载链接指向官方英文安装包。
---

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（仅构建需要）。

```
build-zh-CN.cmd
```

产物：`dist\win-x64\OpenNetMeter.exe`

手动等价命令：

```
dotnet publish .\OpenNetMeter\OpenNetMeter.csproj -c Release -r win-x64 ^
  --self-contained true -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\win-x64
```

运行测试：

```
dotnet test .\OpenNetMeter.Tests\OpenNetMeter.Tests.csproj -c Release
```

## 项目结构

```
OpenNetMeter/                     应用主体（Avalonia 11，net10.0）
OpenNetMeter.Core/                跨平台视图模型基类
OpenNetMeter.PlatformAbstractions/ 平台能力抽象接口
OpenNetMeter.Tests/               单元测试
Installer/                        WiX 安装器工程（未汉化，未随本项目发布）
scripts/                          构建与发布脚本
汉化说明.md                        汉化范围、术语、未汉化项
发布指南.md                        发布到 GitHub 的操作步骤与检查清单
README.upstream.md                上游原始说明
```

## 许可与致谢

- 本项目遵循 **Apache License 2.0** 分发，完整条款见 [LICENSE](LICENSE)。
- 原始版权归 **Ashfaaq Riphque** 所有，见 [NOTICE](NOTICE)。
- 依据 Apache-2.0 第 4(b) 条，所有被修改的源文件顶部均加入"简体中文汉化修改"声明。
- 上游项目：https://github.com/Ashfaaq18/OpenNetMeter
