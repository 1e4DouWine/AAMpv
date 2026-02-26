# AvaloniaAppMPV

基于 Avalonia UI 框架和 libmpv 的~~跨平台~~视频播放器。

全部代码由 AI 编写

## 功能特性

- ~~跨平台支持 (Windows/Linux/macOS)~~

- 基于 MPV 的高性能视频播放

- MVVM 架构 (CommunityToolkit.Mvvm)

- 依赖注入 (Microsoft.Extensions.DependencyInjection)

## 技术栈

- .NET 10.0

- Avalonia UI 11.3.11

- libmpv

- CommunityToolkit.Mvvm 8.2.1

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

```bash
dotnet build
dotnet run
```

## 依赖

- [Avalonia UI](https://avaloniaui.net/) - 跨平台 XAML 框架

- [MPV](https://mpv.io/) - 免费开源媒体播放器

- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) - MVVM 工具包

## 许可证

MIT
