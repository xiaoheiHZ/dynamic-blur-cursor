# 动态模糊光标

一个 Windows 桌面小工具，让鼠标指针留下柔和的动态拖影。支持读取当前指针外观，还可以让系统箭头和手形指针随横向移动倾斜。

## 功能

- 三种拖影样式：跟随指针和柔光、仅跟随指针、仅柔光线条。
- 调节拖影的不透明度、消散时间、大小、间距和柔光颜色。
- 调节指针倾斜幅度；设为 0° 可以关闭倾斜。
- 通过系统托盘打开设置、暂停或退出，并可选择在当前用户登录 Windows 后自动启动。
- 设置保存在当前用户的本地应用数据目录；无需管理员权限。

## 下载和使用

从仓库的 [Releases](https://github.com/xiaoheiHZ/dynamic-blur-cursor/releases) 页面下载 `CursorBlur-Windows-x64.exe` 并运行。双击系统托盘图标打开设置；关闭设置窗口不会退出程序。右键托盘图标可以暂停或退出。

首次启动时 Windows 可能会提示文件来自互联网。程序为 x64 版本，发布包包含 .NET 运行时，不需要单独安装 .NET。

如果开启自启动后移动或重命名程序，请从新位置启动，并在设置中重新启用自启动。

## 使用限制

- 倾斜作用于 Windows 的系统箭头和手形指针；应用自行绘制或使用私有指针时可能不生效。动态指针倾斜时可能暂时显示为静态帧。
- 独占全屏应用和 Windows 安全桌面可能不会显示拖影。
- 程序会在退出时恢复系统指针；另有独立的恢复进程，应对主程序异常退出。

## 从源码构建

需要 Windows x64 和 .NET 8 SDK（或支持 .NET 8 的更新版 SDK）：

```powershell
dotnet publish CursorBlur.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
```

生成的 `publish\CursorBlur.exe` 是可独立运行的文件。图标源于仓库内的 `make-icon.ps1`，运行脚本可以重新生成 `app.ico`。
