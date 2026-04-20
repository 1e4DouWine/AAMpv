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

- Avalonia UI 12.0.1

- libmpv

- CommunityToolkit.Mvvm 8.4.2

- Microsoft.Extensions.DependencyInjection 10.0.6

## 运行环境

- Windows x64

- .NET 10 SDK

- `3rdparty/mpv/libmpv-2.dll`

仓库当前未包含 `libmpv-2.dll`。更新到脚本里指定的版本，可以执行：

```powershell
.\scripts\Download-Mpv.ps1
```

## 项目结构

```
AvaloniaAppMPV/
├── Models/              # 数据模型和 MPV 互操作
├── Views/               # Avalonia 视图
├── ViewModels/          # 视图模型
├── Services/            # 服务层
├── Assets/              # 资源文件
└── 3rdparty/mpv/        # libmpv 库文件
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

## 依赖

- [Avalonia UI](https://avaloniaui.net/) - 跨平台 XAML 框架

- [MPV](https://mpv.io/) - 免费开源媒体播放器

- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) - MVVM 工具包

- [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) - 依赖注入容器


## 许可证

MIT
