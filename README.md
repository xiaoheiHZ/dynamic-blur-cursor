# 动态模糊光标

一个 Windows 桌面小工具，提供鼠标动态模糊和文本插入光标的 Springy 弹簧拖尾。

## 功能

- 三种拖影样式：跟随指针和柔光、仅跟随指针、仅柔光线条。
- 金色加粗的文本插入光标带有柔光，支持 Neovide、Subtle、Zed 三种预设。打字或移动插入点时，拖尾按阻尼弹簧拉伸并收束。
- 鼠标拖影按所选颜色绘制指针轮廓，支持浏览器的 I 形指针等黑白或反色指针。
- 调节拖影的不透明度、消散时间、大小、间距和柔光颜色。
- 调节指针倾斜幅度；设为 0° 可以关闭倾斜。
- 通过系统托盘打开设置、暂停或退出，并可选择在当前用户登录 Windows 后自动启动。
- 设置保存在当前用户的本地应用数据目录；无需管理员权限。

## 下载和使用

从仓库的 [Releases](https://github.com/xiaoheiHZ/dynamic-blur-cursor/releases) 页面下载 `CursorBlur-Windows-x64.exe` 并运行。双击系统托盘图标打开设置；关闭设置窗口不会退出程序。右键托盘图标可以暂停或退出。

「文本光标动画」默认使用 Neovide，可单独关闭或切换预设。在插入点停住时显示金色光标。鼠标拖影的颜色和不透明度在个性化设置里调节。托盘的暂停按钮会暂停两种光标效果。

### 触控板

右键托盘图标，打开「触控板…」并选择「应用 macOS 风格预设」。预设启用双指自然滚动、捏合缩放、单指轻点与连点拖拽、双指轻点右键以及触控板自己的指针加速，并关闭角落右键区。可用「还原预设前设置」撤销。触控板指针速度可以单独调节，三指和四指手势在同一窗口提供的 Windows 设置入口里配置。

一键预设和速度调节需要 Windows 11 24H2 或更新版本及精确式触控板。应用预设后由 Windows 处理触控板输入，退出本工具后设置继续生效。

首次启动时 Windows 可能会提示文件来自互联网。程序为 x64 版本，发布包包含 .NET 运行时，不需要单独安装 .NET。

如果开启自启动后移动或重命名程序，请从新位置启动，并在设置中重新启用自启动。

## 使用限制

- 倾斜作用于 Windows 的系统箭头和手形指针；应用自行绘制或使用私有指针时可能不生效。动态指针倾斜时可能暂时显示为静态帧。
- 独占全屏应用和 Windows 安全桌面可能不会显示拖影。
- 程序会在退出时恢复系统指针；另有独立的恢复进程，应对主程序异常退出。
- 文本动画通过 Windows 原生插入光标坐标或应用发布的 MSAA 插入光标事件获取位置。只自行绘制插入光标、且未向 Windows 提供坐标的编辑器无法触发此效果。

## 从源码构建

需要 Windows x64 和 .NET 8 SDK（或支持 .NET 8 的更新版 SDK）：

```powershell
dotnet publish CursorBlur.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
```

生成的 `publish\CursorBlur.exe` 是可独立运行的文件。图标源于仓库内的 `make-icon.ps1`，运行脚本可以重新生成 `app.ico`。

## 致谢

四角阻尼弹簧与预设移植自 tqsy 的 [Springy Cursor 0.2.5](https://marketplace.visualstudio.com/items?itemName=TQSY.springy-cursor)，原项目基于 LengineerC 的 [Neovide-Cursor](https://github.com/LengineerC/vscode-neovide-cursor)。版权声明见 [SpringyCursor-LICENSE.txt](SpringyCursor-LICENSE.txt)。
