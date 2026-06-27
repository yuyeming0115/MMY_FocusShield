# FocusShield V3 开发文档

## 项目概述

FocusShield 是一个 Windows 桌面小工具，用于在专注场景下强制输入法保持英文，避免 `Shift`/`Space` 误触导致快捷键失效或输入法弹出。

### 技术栈
- .NET 8 + WPF (C# 12)
- Win32 API：RegisterHotKey、SetInputScope、ImmSetConversionStatus、WinEventHook
- Hardcodet.NotifyIcon.Wpf（系统托盘）
- Extended.Wpf.Toolkit（ColorPicker）

## V2 → V3 重构记录

### 核心改进

#### 1. 白名单 → 黑名单语义转换
**问题**：原版"白名单"概念反直觉，用户不理解哪些软件会被影响。

**解决方案**：
- 改为"黑名单"：列表中的软件强制英文输入
- 默认内置常见 DCC 软件：`blender, maya, 3dsmax, zbrush, houdini, cinema4d, substancepainter`
- 配置自动迁移：旧版 `DccList` 在 `ConfigManager.Load()` 时自动复制到 `Blacklist`

**关键代码**：
```csharp
// ConfigManager.cs
public HashSet<string> Blacklist { get; set; } = new(StringComparer.OrdinalIgnoreCase)
{
    "blender", "maya", "3dsmax", "zbrush", "houdini", "cinema4d", "substancepainter"
};

public static Config Load()
{
    // 自动迁移旧版 DccList → Blacklist
    if (File.Exists(Path) && oldConfig.DccList?.Count > 0 && config.Blacklist.Count == 0)
    {
        config.Blacklist = new HashSet<string>(oldConfig.DccList, StringComparer.OrdinalIgnoreCase);
        Save(config);
    }
}
```

#### 2. 进程选择器重做
**问题**：原版下拉列表体验差，无法搜索，显示服务进程。

**解决方案**：
- 新建 `ProcessPickerWindow.xaml/cs`
- 只显示有主窗口的进程（自动排除服务进程）
- `ExtractAssociatedIcon` 提取 exe 图标
- 搜索框实时过滤
- 双击或选中后点「添加」确认
- 支持拖拽 .lnk/.exe 文件添加

**关键代码**：
```csharp
// 只显示有主窗口的进程
var processes = Process.GetProcesses()
    .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
    .GroupBy(p => p.ProcessName.ToLower())
    .Select(g => g.First())
    .OrderBy(p => p.ProcessName)
    .ToList();

// 提取图标
var icon = System.Drawing.Icon.ExtractAssociatedIcon(process.MainModule?.FileName ?? "");
```

#### 3. 光棒交互增强
**问题**：光棒只能显示状态，无法交互。

**解决方案**：
- 单击：切换开关（200ms 延迟判定，不与拖拽冲突）
- 双击：打开设置面板
- 右键：弹出菜单（开启/关闭、设置、退出）
- 视觉状态：开启时 Opacity=1 全发光，关闭时 Opacity=0.25 变暗

**关键代码**：
```csharp
// OverlayWindow.xaml.cs
private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    if (e.ClickCount == 1)
    {
        // 200ms 后切换，给拖拽留时间
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (s, _) => { timer.Stop(); Toggle(); };
        timer.Start();
    }
    else if (e.ClickCount == 2)
    {
        OpenSettings();
    }
    e.Handled = true;
}
```

#### 4. 设置面板简化
**问题**：原版使用 Expander 折叠，用户找不到功能。

**解决方案**：
- 全展开布局（去掉 Expander 折叠）
- 分区清晰：屏蔽列表 / 快捷键 / 个性化
- 并排排列相关控件（快捷键、语言+颜色）
- 拖拽区：虚线框 + 淡蓝背景 `#F7F8FC`

## 遇到的问题和解决方案

### 问题 1：图标加载崩溃 + 单文件发布图标丢失
**症状**：
1. 开发模式下多种方式加载 .ico 文件都崩溃（URI/BitmapFrame/IconBitmapDecoder）
2. 单文件发布（`PublishSingleFile`）后，文件管理器/任务栏/窗口标题栏/托盘图标全部丢失

**尝试过的失败方案**：
1. `new BitmapImage(new Uri("pack://application:,,,/FocusShield.ico"))` — 找不到资源
2. `BitmapFrame.Create(new Uri(...))` — 同样找不到资源
3. `IconBitmapDecoder` — WPF 对 .ico 解码方式敏感
4. 单文件发布后靠 `Path.Combine(exeDir, "FocusShield.ico")` 读文件 — 找不到（exe 运行时解压到临时目录）

**最终成功方案（双重策略）**：

**步骤 1：.csproj 配置**
```xml
<PropertyGroup>
  <ApplicationIcon>FocusShield.ico</ApplicationIcon>
</PropertyGroup>

<ItemGroup>
  <!-- 内嵌到程序集（单文件发布模式用） -->
  <EmbeddedResource Include="FocusShield.ico">
    <LogicalName>FocusShield.ico</LogicalName>
  </EmbeddedResource>
  <!-- 复制到输出目录（开发模式用） -->
  <Content Include="FocusShield.ico">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

**步骤 2：运行时加载（优先内嵌资源，回退文件路径）**
```csharp
// MainWindow.xaml.cs 构造函数
try
{
    System.Drawing.Icon? sdIcon = null;

    // 策略1：从程序集内嵌资源读取（单文件发布模式首选）
    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
    using (var stream = assembly.GetManifestResourceStream("FocusShield.ico"))
    {
        if (stream != null)
        {
            sdIcon = new System.Drawing.Icon(stream);
        }
    }

    // 策略2：从文件路径回退（开发模式）
    if (sdIcon == null)
    {
        string? exeDir = System.IO.Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        if (exeDir != null)
        {
            string icoPath = System.IO.Path.Combine(exeDir, "FocusShield.ico");
            if (System.IO.File.Exists(icoPath))
            {
                sdIcon = new System.Drawing.Icon(icoPath);
            }
        }
    }

    if (sdIcon != null)
    {
        // 窗口标题栏图标（WPF ImageSource）
        var wpfIcon = Imaging.CreateBitmapSourceFromHIcon(
            sdIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        this.Icon = wpfIcon;
        // 托盘图标（Hardcodet 直接接受 System.Drawing.Icon）
        TrayIcon.Icon = sdIcon; // 注意：托盘持有引用，此处不 Dispose
    }
}
catch (Exception ex) { App.Log($"Icon load skipped: {ex.Message}"); }
```

**关键知识点**：
- `<ApplicationIcon>`：设置 exe 文件原生图标（文件管理器、任务栏显示）
- `<EmbeddedResource>`：将 .ico 嵌入程序集，运行时通过 `GetManifestResourceStream()` 读取
- 单文件发布时，exe 运行会解压到临时目录，`MainModule.FileName` 指向临时路径，旁边没有 .ico 文件
- 托盘图标（`TaskbarIcon`）的 `.Icon` 属性接受 `System.Drawing.Icon` 类型，不是 WPF 的 `ImageSource`
- 托盘持有 `System.Drawing.Icon` 引用期间**不能 Dispose**，否则托盘图标会消失

### 问题 2：启动黑屏闪现
**症状**：应用启动时会出现黑屏闪一下才消失。

**原因**：`StartupUri` 会自动 Show() 窗口，直到 `Window_Loaded` 才 Hide()

**解决方案**：
1. 移除 App.xaml 中的 `StartupUri="MainWindow.xaml"`
2. 在 App.xaml.cs 中手动创建 MainWindow，且不调用 `Show()`
3. MainWindow.xaml 设置 `Visibility="Hidden"`

**关键代码**：
```xml
<!-- App.xaml -->
<Application x:Class="FocusShield.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 不要设置 StartupUri -->
</Application>
```

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    var mw = new MainWindow();
    // 不调用 mw.Show()，窗口默认隐藏
    base.OnStartup(e);
}
```

### 问题 3：设置面板全黑
**症状**：打开设置面板，内容区域全黑，看不到任何控件。

**原因**：
1. 窗口背景没设对（默认透明）
2. 启动黑屏问题导致窗口状态异常

**解决方案**：
- MainWindow.xaml 根 Grid 设置 `Background="#FAFAFA"`
- 确保窗口 `Visibility="Hidden"` 而不是 `Collapsed`

### 问题 4：XAML 语法错误
**症状**：编译报错 `BorderDashArray` 不是 WPF 有效属性

**原因**：手写 XAML 时记错了属性名

**解决方案**：
- `Border` 不支持虚线边框
- 改用 `Rectangle` 做虚线边框：
```xml
<Rectangle Stroke="#4A90D9" StrokeThickness="2" StrokeDashArray="4 2"
           RadiusX="8" RadiusY="8" Fill="Transparent"/>
```

### 问题 5：C# 版本兼容性问题
**症状**：`ContextMenu?.IsOpen = true` 编译报错

**原因**：C# 12 不支持空条件赋值（C# 14 才支持）

**解决方案**：
```csharp
// C# 12 兼容写法
if (ContextMenu != null)
{
    ContextMenu.IsOpen = true;
}
```

### 问题 6：全局异常处理
**问题**：应用崩溃时看不到任何错误信息，无法调试。

**解决方案**：
- 在 App.xaml.cs 中添加全局异常处理
- 日志写到程序目录（不要用桌面，沙箱会拦截）

**关键代码**：
```csharp
private static readonly string LogPath = 
    System.IO.Path.Combine(AppContext.BaseDirectory, "FocusShield_debug.log");

protected override void OnStartup(StartupEventArgs e)
{
    this.DispatcherUnhandledException += (s, args) =>
    {
        Log($"【全局异常】{args.Exception}");
        MessageBox.Show($"发生错误：\n{args.Exception.Message}", "FocusShield 错误", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
        args.Handled = true;
        this.Shutdown();
    };
    
    Log("=== FocusShield 启动 ===");
    // ...
}

public static void Log(string message)
{
    try
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        File.AppendAllText(LogPath, line);
    }
    catch { }
}
```

## 项目结构

```
FocusShieldV3/
├── App.xaml                    # 应用入口（无 StartupUri）
├── App.xaml.cs                 # 全局异常处理 + 日志
├── MainWindow.xaml             # 设置面板（全展开布局）
├── MainWindow.xaml.cs          # 主逻辑（黑名单管理、快捷键）
├── OverlayWindow.xaml          # 光棒窗口（屏幕边缘常驻）
├── OverlayWindow.xaml.cs       # 光棒交互（单击/双击/右键）
├── ProcessPickerWindow.xaml    # 进程选择器（带图标+搜索）
├── ProcessPickerWindow.xaml.cs # 进程列表 + 拖拽支持
├── FocusShield.ico             # 应用图标
├── FocusShield.csproj          # 项目文件
├── Properties/
│   └── AssemblyInfo.cs        # 程序集信息
├── Resources/                  # 资源文件（如有）
└── Core/
    ├── ConfigManager.cs        # 配置读写（JSON + 自动迁移）
    └── InputGuard.cs          # 输入法守护核心（Win32 API）
```

## 构建和发布

### 开发构建
```bash
cd FocusShieldV3
dotnet build
```

### 打包单文件 EXE
```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```

**参数说明**：
- `-c Release`：发布配置
- `-r win-x64`：目标运行时（64位 Windows）
- `--self-contained false`：不打包 .NET 运行时（用户需安装 .NET 8 Runtime）
- `-p:PublishSingleFile=true`：打包成单文件
- `-p:IncludeNativeLibrariesForSelfExtract=true`：包含原生库
- `-o ./publish`：输出目录

**注意**：
- 单文件模式下，`Process.GetCurrentProcess().MainModule?.FileName` 返回的是临时解压路径，不是程序实际位置
- 解决方案：使用 `AppContext.BaseDirectory` 获取程序目录

## 配置迁移

V3 会自动迁移 V2 的配置：
- 旧版 `DccList` → 新版 `Blacklist`
- 迁移只在首次运行时执行（检测 `Blacklist.Count == 0 && DccList?.Count > 0`）

**配置文件位置**：
- V2：`%AppData%\FocusShield\config.json`
- V3：`%AppData%\FocusShieldV3\config.json`

## 待优化项

1. **热键冲突检测**：当前不检测热键是否已被其他程序占用
2. **多显示器支持**：光棒只显示在主显示器
3. **进程自动识别**：根据窗口标题自动识别 DCC 软件
4. **配置同步**：多设备间同步黑名单配置
5. **日志轮转**：调试日志无限增长，需要定期清理

## 常见问题

### Q: 为什么不用 WPF 原生图标加载？
A: WPF 对 .ico 文件的解码支持不完善，特别是多尺寸 .ico 文件。`System.Drawing.Icon` 更可靠。

### Q: 为什么不用 `StartupUri`？
A: `StartupUri` 会自动 Show() 窗口，导致启动黑屏闪现。手动创建 MainWindow 可以更精细控制窗口状态。

### Q: 为什么日志写到程序目录而不是桌面？
A: 沙箱环境会拦截桌面文件写入。程序目录（`AppContext.BaseDirectory`）通常可写。

### Q: 如何调试 Win32 API 调用？
A: 使用 `Log()` 记录关键步骤，检查 `FocusShield_debug.log`。Win32 API 调用失败时通常不会抛异常，需要检查返回值。

---

**最后更新**：2026-06-27
**维护者**：EDY (yuyeming0115)
