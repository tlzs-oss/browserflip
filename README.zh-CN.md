# BrowserFlip

[English](README.md) | [日本語](README.ja.md) | **简体中文**

[![CI](https://github.com/tlzs-oss/browserflip/actions/workflows/ci.yml/badge.svg)](https://github.com/tlzs-oss/browserflip/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> **One Click. Your Browser.**

BrowserFlip 是一款小巧的开源 Windows 10/11 桌面应用:列出电脑上已安装的浏览器,显示当前的默认浏览器,并一键直达 Windows 设置中对应的页面来更改它。

![BrowserFlip 截图](docs/images/screenshot.png)

![BrowserFlip 紧凑工具栏](docs/images/compact-bar.png)

> 截图将在首个版本发布前补充。

> **状态:** 预发布(0.1.0 MVP 与 0.2.0 桌面功能开发中),尚未发布任何正式版本。

## 功能

- **自动检测浏览器** — 读取 Windows 标准注册信息(`Software\Clients\StartMenuInternet` 和 `Software\RegisteredApplications`,包括当前用户/本机、64 位/32 位视图)。不硬编码任何可执行文件路径。
- **清晰的卡片列表** — 双列卡片显示浏览器图标、名称和"当前默认"标记,整张卡片均可点击。
- **一键打开设置** — 在 Windows 11(已安装 2023 年 4 月更新或更高版本)上打开该浏览器专属的"默认应用"页面,其他情况打开"默认应用"首页。
- **实时状态** — 从设置返回后重新读取 `http` / `https` 默认值并标出新的默认浏览器。
- **不一致警告** — 当 `http` 与 `https` 由不同应用处理时给出提示。
- **友好的提示** — 对已卸载的浏览器（卡片显示为不可用）、注册不完整的浏览器（打开“默认应用”首页并给出提示）、组策略限制以及取消更改等情况分别说明。
- **深色模式**(跟随 Windows 应用主题)、**Per-Monitor V2 高 DPI**、**键盘导航 / 屏幕阅读器名称**。
- **English、日本語、简体中文** — 根据 Windows 语言自动选择,可在运行时切换。

0.2 新功能(尚未发布):

- **专属图标** — 地球加箭头的原创设计(MIT),用于 EXE、窗口、任务栏和通知区域。
- **置顶显示** — 通过图钉按钮切换;全屏应用或演示运行期间会暂时取消置顶。
- **紧凑工具栏** — 列出浏览器的小型横向 / 纵向工具栏,突出显示默认浏览器。
- **停靠到屏幕边缘** — 停靠到左、右、上、下边缘,或拖动到边缘附近自动吸附。不会改变工作区。
- **自动隐藏** — 停靠时工具栏会收起,只留下一个小标签;将鼠标悬停在标签上或点击标签(或使用通知区域图标)即可恢复。
- **通知区域图标** — 左键单击显示,右键单击打开菜单。可选功能,不使用也能使用全部功能。
- **多显示器 / 每显示器 DPI** — 在显示器、分辨率、缩放比例、任务栏变化或睡眠唤醒后重新定位,不会消失在屏幕外。

## 工作原理

为什么一键之后是打开 Windows 设置,而不是直接静默切换?

因为 **Windows 会保护你的默认浏览器选择**。从 Windows 8 开始,该选择与一个保护哈希一同保存,Windows 有意要求用户在设置中亲自确认更改。直接改写注册表或伪造哈希的工具是在对抗 Windows 的安全机制,随时可能因 Windows 更新而失效,也经常被识别为恶意软件。

BrowserFlip 只使用受支持的方式:

1. 从注册表 **检测** 浏览器(只读)。
2. 通过有文档的 `AssocQueryStringW` API **读取** 当前的 `http` / `https` 处理程序。
3. 在 Windows 支持时(Windows 11 21H2 内部版本 22000.1817 及以上、22H2 内部版本 22621.1555 及以上,或 23H2 及更高版本)**打开** `ms-settings:defaultapps?registeredAppUser=<名称>` / `registeredAppMachine=<名称>`,否则打开 `ms-settings:defaultapps`。
4. 由 **你** 在设置中点击"设为默认值" — 绝不跳过 Windows 的确认。
5. 返回后 **检查** 更改并更新卡片。

示意图请参阅 [docs/architecture.md](docs/architecture.md)(日文)。

## 系统要求

- Windows 10 版本 1809(内部版本 17763)或更高版本,或 Windows 11
  - 浏览器专属设置页需要已安装 2023 年 4 月累积更新的 Windows 11(21H2 为 22000.1817 及以上,22H2 为 22621.1555 及以上)或 23H2 及更高版本;Windows 10 和较旧的 Windows 11 打开通用的"默认应用"页面。
- x64 或 Arm64
- 无需管理员权限
- 从源码构建需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)

## 安装

**计划** 在首个版本中于 [Releases](https://github.com/tlzs-oss/browserflip/releases) 页面提供 `win-x64` 和 `win-arm64` 的自包含单文件 `.zip`,并附带 `SHA256SUMS`。

在 PowerShell 中校验:

```powershell
Get-FileHash .\BrowserFlip-win-x64.zip -Algorithm SHA256
# 与 SHA256SUMS 中对应的行比较
```

> 发布的二进制文件暂未进行代码签名,Windows SmartScreen 可能会显示警告。

## 从源码构建

```powershell
git clone https://github.com/tlzs-oss/browserflip.git
cd browserflip
dotnet build BrowserFlip.slnx -c Release
dotnet test BrowserFlip.slnx
dotnet run --project src/BrowserFlip.App
```

## 使用方法

1. 启动 BrowserFlip。已安装的浏览器以卡片形式显示,当前默认浏览器带有标记。
2. 点击(或按 Enter / Space)想要使用的浏览器。
3. Windows 设置随即打开。点击"设为默认值"(Windows 11),或在"Web 浏览器"下选择该浏览器(Windows 10)。
4. 切回 BrowserFlip,标记会移动到新的默认浏览器。如果没有更改,BrowserFlip 会给出提示。

紧凑工具栏、停靠、自动隐藏和通知区域图标的用法请参阅使用说明(日文):[docs/user-guide.ja.md](docs/user-guide.ja.md)。

## 常见问题

**为什么不能不打开设置就直接更改默认浏览器?**
Windows 有意要求用户亲自确认默认应用的更改,BrowserFlip 绝不会绕过这一点。参见 [工作原理](#工作原理)。

**我的浏览器没有出现在列表中。**
BrowserFlip 只列出在 `StartMenuInternet` 下注册并声明了 `http`/`https` URL 关联的浏览器。不进行注册的便携版不会显示。重新运行浏览器安装程序或使用浏览器自身的"设为默认"功能通常会创建注册信息。通过应用用户模型 ID(`registeredAUMID`)注册的打包版(MSIX / Microsoft Store)浏览器目前不会被检测到(计划支持)。

**某张卡片显示为灰色。**
注册信息仍然存在,但找不到浏览器的可执行文件,可能已被卸载。

**设置打开的是通用"默认应用"页面,而不是浏览器专属页面。**
这会发生在 Windows 10 或未安装 2023 年 4 月更新的 Windows 11 上,或者浏览器未在 `RegisteredApplications` 中注册时。请在该页面中手动选择浏览器。

**提示 http 与 https 使用了不同的应用。**
在设置中打开该浏览器的页面,并将其设为两者的默认值(Windows 11 的"设为默认值"按钮会同时设置)。

**我的电脑由组织管理。**
如果组策略隐藏了设置或"默认应用"页面,BrowserFlip 会显示提示而不是打开页面。如果配置了默认关联策略,你的选择可能会在下次登录时被还原。

**需要管理员权限吗?**
不需要,它从不请求提升权限。

## 隐私

- **无遥测**,不做分析,不发送崩溃报告。
- **无网络访问**,BrowserFlip 不会连接互联网。
- 仅 **读取** 注册表并启动 `ms-settings:` URI,从不写入注册表。
- 唯一写入的文件是 `%LOCALAPPDATA%\BrowserFlip\settings.json`,用于保存语言和显示设置(主题、显示模式、置顶、停靠、自动隐藏、通知区域选项、窗口和工具栏位置)。删除该文件即可恢复默认设置。

## 路线图

后续阶段计划(尚未实现):检测打包版(MSIX / Store)浏览器、全局热键、启动时检查、便携版、安装程序、更新通知,以及自动化方式的调研(仅限 Windows 提供受支持机制的情况)。

## 参与贡献

欢迎贡献!请阅读 [CONTRIBUTING.md](CONTRIBUTING.md) 和 [行为准则](CODE_OF_CONDUCT.md)。安全问题请参阅 [SECURITY.md](SECURITY.md)。

更多文档(日文):[使用说明](docs/user-guide.ja.md) · [架构](docs/architecture.md) · [v0.2 设计](docs/desktop-design.ja.md) · [需求](docs/requirements.md) · [测试](docs/testing.md) · [相关开源项目调研](docs/oss-research.md) · [更新日志](CHANGELOG.md)

## 许可证

[MIT](LICENSE) © 2026 TLZS株式会社 and BrowserFlip contributors

BrowserFlip 与 Microsoft 及任何浏览器厂商均无关联。产品名称为其各自所有者的商标。
