# FocusShield V3 开发文档

## 项目概述
FocusShield 是一个 Windows 桌面小工具，用于在专注场景下强制输入法保持英文状态。

## 技术栈
- **框架**: .NET 8 WPF
- **UI**: WPF XAML
- **第三方库**: 
  - Hardcodet.NotifyIcon.Wpf (托盘图标)
  - Xceed.Wpf.Toolkit (ColorPicker, IntegerUpDown等控件)

## 项目结构
```
FocusShieldV3/
├── App.xaml.cs                    # 应用程序入口
├── MainWindow.xaml.cs             # 设置窗口
├── OverlayWindow.xaml.cs          # 荧光棒窗口（核心功能）
├── ProcessPickerWindow.xaml.cs    # 进程选择窗口
├── Core/
│   ├── ConfigManager.cs           # 配置管理
│   └── InputGuard.cs             # 输入法守护
└── FocusShield.csproj
```

## 核心功能实现

### 1. 荧光棒拖拽功能
**问题**: 初始实现使用 DispatcherTimer + DragMove()，导致拖拽无响应

**原因**: 
- DragMove() 会阻塞 UI 线程
- 定时器逻辑过于复杂，导致事件冲突

**解决方案**:
```csharp
// 简化版拖拽逻辑
private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    _isDragging = true;
    _dragStartPoint = e.GetPosition(this);
    this.CaptureMouse();  // 捕获鼠标
}

private void Window_MouseMove(object sender, MouseEventArgs e)
{
    if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
    {
        var delta = e.GetPosition(this) - _dragStartPoint;
        this.Left += delta.X;
        this.Top += delta.Y;
    }
}

private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
{
    if (_isDragging)
    {
        _isDragging = false;
        this.ReleaseMouseCapture();
        SnapToEdge();  // 吸附到边缘
    }
}
```

**经验**:
- WPF 拖拽不要使用 DragMove()，而是手动捕获鼠标 + 更新位置
- 事件绑定要在 XAML 中明确声明（MouseMove 事件容易遗漏）

### 2. 发光效果实现
**实现方式**: 使用 `DropShadowEffect`

```xml
<Border.Effect>
    <DropShadowEffect x:Name="GlowEffect" 
                     Color="Red" 
                     BlurRadius="20" 
                     ShadowDepth="0" 
                     Opacity="1.0"/>
</Border.Effect>
```

**可配置参数**:
- `BlurRadius`: 光晕模糊半径（5-50）
- `Opacity`: 光晕强度（0.1-1.0）
- `Color`: 光晕颜色（从配置读取）

**开启/关闭状态**:
- 开启: Opacity=1.0, BlurRadius=20, 背景不透明
- 关闭: Opacity=0.0, 背景半透明(0.25)

### 3. 边缘吸附功能
**实现逻辑**:
```csharp
private void SnapToEdge()
{
    double screenWidth = SystemParameters.WorkArea.Width;
    double currentLeft = this.Left;
    double windowWidth = this.Width;

    // 判断吸附到哪一侧
    if (currentLeft + (windowWidth / 2) < screenWidth / 2)
    {
        this.Left = 0;  // 吸附到左边缘
        GlowBorder.CornerRadius = new CornerRadius(0, 5, 5, 0);
    }
    else
    {
        this.Left = screenWidth - windowWidth;  // 吸附到右边缘
        GlowBorder.CornerRadius = new CornerRadius(5, 0, 0, 5);
    }
}
```

### 4. 配置系统设计
**配置类**: `AppConfig`

**可配置项**:
```csharp
public class AppConfig
{
    public List<string> Blacklist { get; set; }  // 屏蔽进程列表
    public HotkeySetting HotkeySwitch { get; set; }  // 开关热键
    public HotkeySetting HotkeySettings { get; set; }  // 设置热键
    public string GlowColor { get; set; } = "#FFFF0000";  // 荧光棒颜色
    public double GlowWidth { get; set; } = 10;  // 荧光棒宽度
    public double GlowHeight { get; set; } = 120;  // 荧光棒高度
    public double GlowBlurRadius { get; set; } = 20;  // 光晕模糊半径
    public double GlowIntensity { get; set; } = 1.0;  // 光晕强度
    public bool AutoStart { get; set; } = false;  // 开机自启
    public string Language { get; set; } = "zh-CN";  // 语言
}
```

**配置持久化**: JSON 格式，保存到 `%AppData%\FocusShield\config.json`

### 5. 实时预览功能
**实现方式**: 在设置界面修改参数时，立即更新荧光棒

```csharp
private void NumGlowSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
{
    // 更新配置
    ConfigManager.Current.GlowWidth = NumGlowWidth.Value ?? 10;
    ConfigManager.Current.GlowHeight = NumGlowHeight.Value ?? 120;
    
    // 实时更新荧光棒
    _overlay?.SetSize(ConfigManager.Current.GlowWidth, ConfigManager.Current.GlowHeight);
}
```

**关键**: 保持 OverlayWindow 实例引用（`_overlay`），而不是每次都创建新实例

## 常见问题及解决方案

### 问题1: 编译警告（null 引用）
**现象**: 
```
warning CS8600: 将 null 文本或可能的 null 值转换为不可为 null 类型
warning CS8602: 解引用可能出现空引用
```

**解决方案**: 使用 null-forgiving 操作符 `!`
```csharp
dynamic shell = Activator.CreateInstance(shellType)!;
dynamic shortcut = shell.CreateShortcut(lnkPath)!;
```

### 问题2: 进程锁定导致编译失败
**现象**: 
```
error MSB3027: 无法将文件复制到... 文件被"FocusShield (PID)"锁定
```

**原因**: 上一次运行的 FocusShield.exe 进程未关闭

**解决方案**:
```bash
taskkill /F /IM FocusShield.exe
```

**预防**: 开发时使用 Debug 模式，退出时确保进程完全关闭

### 问题3: XAML 事件绑定遗漏
**现象**: 事件处理方法已定义，但运行时不触发

**原因**: XAML 中未声明事件绑定

**解决方案**: 确保在 XAML 根元素中添加事件绑定
```xml
<Window ...
        MouseLeftButtonDown="Window_MouseLeftButtonDown"
        MouseLeftButtonUp="Window_MouseLeftButtonUp"
        MouseMove="Window_MouseMove"
        MouseDoubleClick="Window_MouseDoubleClick"
        MouseRightButtonDown="Window_MouseRightButtonDown">
```

### 问题4: Git 推送失败（403 错误）
**现象**: 
```
fatal: unable to access 'https://github.com/...': The requested URL returned error: 403
```

**原因**: GitHub 认证失败（token 过期或权限不足）

**解决方案**:
1. 检查 GitHub token 是否有效
2. 使用 SSH 方式推送：`git remote set-url origin git@github.com:...`
3. 重新生成 GitHub Personal Access Token

## 打包部署

### 自包含打包（推荐）
```bash
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

**优点**: 
- 用户无需安装 .NET 8 Runtime
- 单个 EXE 文件，易于分发

**缺点**:
- 文件较大（约 150MB）

### 框架依赖打包
```bash
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

**优点**: 文件小

**缺点**: 用户需要安装 .NET 8 Runtime

### 打包输出
- **输出目录**: `FocusShieldV3/publish/`
- **主程序**: `FocusShield.exe`
- **配置文件**: `%AppData%\FocusShield\config.json`

## 开发流程

### 1. 功能开发流程
1. 创建功能分支：`git checkout -b feature/xxx`
2. 实现功能代码
3. 本地测试
4. 提交代码（中文提交信息）
5. 推送到远程：`git push origin feature/xxx`
6. 创建 Pull Request
7. 代码审查通过后合并到 main

### 2. 提交信息规范
使用中文描述，格式：
```
<类型>: <主题>

<正文>（可选）
```

**类型**:
- `feat`: 新功能
- `fix`: 修复 bug
- `docs`: 文档更新
- `style`: 代码格式（不影响功能）
- `refactor`: 重构
- `test`: 测试相关
- `chore`: 构建/工具链相关

**示例**:
```
feat: 实现荧光棒尺寸自定义功能

- 在AppConfig中添加GlowWidth和GlowHeight属性
- 在设置界面添加数值输入框
- 实时预览尺寸变化
```

### 3. 版本号管理
- V1: 初始版本（旧版本代码/）
- V2: 重构版本（未发布）
- V3: 当前版本（FocusShieldV3/）

## 测试清单

### 功能测试
- [ ] 荧光棒显示/隐藏
- [ ] 拖拽移动荧光棒
- [ ] 边缘吸附功能
- [ ] 左键单击切换状态
- [ ] 双击打开设置
- [ ] 右键菜单
- [ ] 颜色选择
- [ ] 尺寸调整
- [ ] 光晕参数调整
- [ ] 快捷键设置
- [ ] 进程屏蔽
- [ ] 开机自启

### 兼容性测试
- [ ] Windows 10
- [ ] Windows 11
- [ ] 不同 DPI 缩放比例
- [ ] 多显示器环境

## 性能优化

### 当前优化点
1. **荧光棒窗口**: 使用 `AllowsTransparency="True"` 实现透明效果
2. **输入法守护**: 使用低级别钩子，最小化性能影响
3. **配置加载**: 启动时一次性加载，避免频繁 IO

### 未来优化方向
- [ ] 使用硬件加速渲染荧光棒（WriteableBitmap）
- [ ] 优化输入法守护逻辑，减少 CPU 占用
- [ ] 添加配置缓存机制

## 调试技巧

### 1. 查看托盘图标
托盘图标可能不会立即显示，检查：
- `TaskbarIcon` 控件的 `Visibility` 属性
- 托盘区域是否隐藏了图标

### 2. 调试荧光棒窗口
荧光棒窗口是透明的，调试时可以：
- 临时设置 `Background="Red"` 使窗口可见
- 使用 `System.Diagnostics.Debug.WriteLine()` 输出日志

### 3. 查看配置文件中
配置文件位置: `%AppData%\FocusShield\config.json`

## 发布检查清单

- [ ] 版本号更新
- [ ] 编译通过（0 错误 0 警告）
- [ ] 功能测试通过
- [ ] 文档更新
- [ ] 代码审查完成
- [ ] 打包测试通过
- [ ] GitHub Release 创建

## 联系方式
- GitHub: https://github.com/yuyeming0115/MMY_FocusShield
- Issue 报告: https://github.com/yuyeming0115/MMY_FocusShield/issues

---
**最后更新**: 2026-06-27
**维护者**: yuyeming0115
