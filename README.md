# AvaloniaAppMPV

基于 Avalonia UI 12 和 libmpv 的桌面视频播放器，当前主要面向 Windows。

全部代码由 AI 编写

## 功能特性

- 基于 MPV 的高性能视频播放
- MVVM 架构 (CommunityToolkit.Mvvm)
- 依赖注入 (Microsoft.Extensions.DependencyInjection)
- OpenGL 渲染链路，直接将 mpv 输出绘制到 Avalonia 控件中

## 技术栈

- .NET 10.0
- Avalonia UI 12.1.0
- libmpv
- CommunityToolkit.Mvvm 8.4.2
- Microsoft.Extensions.DependencyInjection 10.0.9

## 运行环境

- Windows x64
- .NET 10 SDK
- `3rdparty/mpv/libmpv-2.dll`

仓库包含可直接使用的 `libmpv-2.dll`。如需重新下载 CI 使用的版本，请先确保 `7z` 命令可用，然后执行：

```powershell
.\scripts\Download-Mpv.ps1
```

下载脚本当前使用 [zhongfly/mpv-winbuild](https://github.com/zhongfly/mpv-winbuild) 提供的 `mpv-dev-x86_64-v3-20260710-git-e5486b96d7.7z`。

## 项目结构

```text
AvaloniaAppMPV/
├── Core/
│   └── Playback/           # 播放核心契约与媒体信息模型
├── Infrastructure/
│   ├── Avalonia/           # Avalonia 相关基础设施实现
│   └── Mpv/                # libmpv 互操作与播放服务
├── UI/
│   ├── Common/             # UI 公共基类
│   ├── Controls/           # 自定义控件
│   ├── Dialogs/            # 弹窗视图
│   └── Main/               # 主窗口视图与 ViewModel
├── Assets/                 # 资源文件
├── scripts/                # 辅助脚本
└── 3rdparty/mpv/           # libmpv 库文件（本地下载）
```

## 构建与运行

```powershell
dotnet restore
dotnet build
dotnet run
```

如果需要生成和 CI 一致的 Windows 发布产物：

```powershell
dotnet publish .\AvaloniaAppMPV.csproj -c Release -r win-x64 --self-contained false -o .\publish\win-x64
```

## CI 与发布

CI 工作流在以下情况触发：

- 创建或重新打开 Pull Request，或向 Pull Request 推送新提交
- 向 `main` 分支推送提交

CI 包含两个依次执行的 job：

1. `Build`：还原依赖并执行 Release 编译
2. `Package win-x64`：下载 libmpv、发布 Windows x64 版本并上传 `AvaloniaAppMPV-win-x64` 构建产物

推送名称匹配 `v*` 的标签（例如 `v1.0.0`）会触发 Release 工作流，生成 `AvaloniaAppMPV-win-x64.zip` 并创建或更新对应的 GitHub Release。

## 依赖

- [Avalonia UI](https://avaloniaui.net/) - 跨平台 XAML 框架
- [MPV](https://mpv.io/) - 免费开源媒体播放器
- [zhongfly/mpv-winbuild](https://github.com/zhongfly/mpv-winbuild) - Windows libmpv 构建
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) - MVVM 工具包
- [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) - 依赖注入容器

## 许可证

MIT
