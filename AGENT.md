# 项目代理说明

## 项目概述

本项目是一个面向 Windows 的单文件桌面工具，目标是把 ACE（AntiCheatExpert）相关进程固定调整为低优先级，并将 CPU 亲和性限制到最后一个逻辑处理器上，以尽量降低其对前台游戏或其他程序的性能干扰。

当前源码集中在 `开发文件夹/Program.cs`，构建后输出到 `exe文件夹/ACE_LowPriority.exe`。项目不是标准的 Visual Studio 解决方案，而是通过 PowerShell 脚本直接调用 C# CodeDOM 编译成 `winexe`。

## 当前实际功能

基于 `开发文件夹` 的现有实现，项目目前已经具备以下能力：

- 启动时检查管理员权限，不足时使用 `runas` 重新拉起自身。
- 使用全局互斥锁保证单实例运行；重复启动时会向已运行实例发送激活消息。
- 图形界面支持 4 种状态：等待、处理中、成功、失败。
- 点击按钮后会把以下固定目标进程设置为 `Idle` 优先级，并把 CPU 亲和性设置为最后一个逻辑处理器：
  - `C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe`
  - `C:\Program Files\AntiCheatExpert\SGuard\x64\SGuardSvc64.exe`
- 支持系统托盘常驻、托盘右键菜单、双击托盘图标恢复窗口。
- 支持“开机自启”和“自动执行”两个设置项。
- 自动执行开启后，每 5 秒轮询一次目标进程；当检测到新的目标进程实例时自动再次执行设置。
- 关闭主窗口时弹出对话框，让用户选择“隐藏到托盘”或“直接退出”。

## 具体实现方式

### 1. 启动与权限处理

- 程序入口在 `Program.Main`。
- 启动后先执行 `SingleInstanceManager.TryAcquire()`，通过命名互斥锁 `Global\ACE_LowPriority_SingleInstance` 限制单实例。
- 如果已有实例存在，则通过 `RegisterWindowMessage + PostMessage(HWND_BROADCAST)` 发送激活消息，让已有窗口恢复显示。
- 权限部分由 `PrivilegeHelper` 负责：
  - `IsAdministrator()` 判断当前是否为管理员。
  - `RelaunchElevated()` 使用 `ProcessStartInfo.Verb = "runas"` 以管理员权限重启当前 exe。

### 2. 进程识别与状态修改

- 目标进程列表写死在 `MainForm.Targets` 常量数组中。
- 处理逻辑在 `BeginOperation()` -> `PerformOperation()`：
  - 读取 `Environment.ProcessorCount`。
  - 计算最后一个逻辑处理器的编号 `logicalProcessorCount - 1`。
  - 生成亲和性掩码 `1L << lastProcessorIndex`。
  - 遍历两个目标进程路径，调用 `GetTargetProcesses()` 找到匹配进程。
- 进程匹配策略：
  - 先按进程名 `Process.GetProcessesByName()` 查找。
  - 再读取 `process.MainModule.FileName` 尝试确认完整路径。
  - 优先使用“路径完全匹配”的结果。
  - 如果路径无法读取，则保留为兜底候选。
- 真正的修改发生在 `SetTargetProcessState()`：
  - `process.PriorityClass = ProcessPriorityClass.Idle`
  - `process.ProcessorAffinity = (IntPtr)affinityMask`

### 3. 自动执行机制

- 设置由 `AppSettings` 管理，保存位置为：
  - `%AppData%\ACE_LowPriority\settings.ini`
- 当前只保存两个布尔值：
  - `StartWithWindows`
  - `AutoExecute`
- 自动执行由 `_autoExecuteTimer` 驱动，轮询周期固定为 5000 ms。
- `CheckAutoExecuteTargets()` 会：
  - 收集当前所有目标进程 PID。
  - 与 `_processedProcessIds` 比较。
  - 只有当检测到“新的目标实例”时，才再次触发 `BeginOperation()`。
- 如果目标进程全部退出，已处理 PID 集合会被清空，等待下一轮重新触发。

### 4. 开机自启实现

- 开机自启由 `StartupRegistrationManager` 实现。
- 写入注册表位置：
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- 注册值名固定为：
  - `ACE_LowPriority`
- 写入命令行为：
  - `"当前程序路径" --start-in-tray`

### 5. 界面与交互实现

- 整个界面基于 WinForms 自绘，没有使用设计器文件。
- 主窗口 `MainForm` 采用无边框窗体，拖动依赖 `ReleaseCapture + SendMessage` 模拟标题栏拖拽。
- UI 大部分控件是代码绘制：
  - `RoundedPanel`：圆角卡片/容器
  - `RoundedButton`：圆角按钮
  - `WindowCaptionButton`：标题栏最小化、设置、关闭按钮
  - `StatusIconControl`：等待/成功/失败状态图标
- 状态切换通过 `SetState()` 集中管理，统一更新标题、说明文案、按钮样式和错误详情区。
- 处理过程放在线程池 `ThreadPool.QueueUserWorkItem` 中执行，完成后通过 `BeginInvoke()` 回到 UI 线程刷新界面。
- 托盘部分使用 `NotifyIcon + ContextMenuStrip` 实现，支持：
  - 显示主界面
  - 立即执行一次
  - 退出程序

### 6. 构建与发布方式

- 构建脚本是 `开发文件夹/build-exe.ps1`。
- 它会先把 `红温猫.jpg` 转成临时 `.ico`，再把图标嵌入最终 exe。
- 编译器使用 `Microsoft.CSharp.CSharpCodeProvider`，不是 `dotnet build`。
- 编译参数包含：
  - `/target:winexe`
  - `/platform:x64`
  - `/optimize+`
  - `/win32icon`
- 入口源码只有 `Program.cs`，引用的程序集主要是：
  - `System.dll`
  - `System.Core.dll`
  - `System.Drawing.dll`
  - `System.Windows.Forms.dll`
- `开发文件夹/run-set-ace-process.cmd` 用于在仓库内递归查找 `ACE_LowPriority.exe` 并启动它。

## 目录职责

- `开发文件夹/Program.cs`：主程序全部逻辑与 UI 实现。
- `开发文件夹/build-exe.ps1`：打包编译脚本。
- `开发文件夹/set-ace-process.ps1`：更早期的 PowerShell 版实现，逻辑与现版 exe 基本一致。
- `开发文件夹/UI`：HTML + 截图形式的界面设计参考稿，不参与程序运行。
- `开发文件夹/TODO.md`：迭代计划，部分内容已经被源码实现，部分仍是后续规划。
- `exe文件夹`：构建产物目录。

## 当前代码状态说明

以下内容是按现有源码观察到的实际情况，后续维护时应注意：

- `--start-in-tray` 参数会被解析，但当前 `MainForm` 中没有真正根据该参数在启动时自动隐藏到托盘。
- `--auto-start` 参数也会被解析，并会在窗口显示后立即执行一次，但当前开机自启写入的命令行并没有带这个参数。
- `ShowTrayBalloon()` 当前是空实现，代码中虽然调用了“托盘气泡提示”，实际不会显示。
- 目标进程路径、优先级策略、CPU 选择策略目前全部写死，尚未做成可配置规则。

## 适合后续继续维护的方向

如果继续开发，最直接的演进点是：

- 把目标进程、优先级、CPU 亲和性做成配置项，而不是硬编码。
- 补齐 `--start-in-tray` 的真实行为，使开机自启真正静默常驻。
- 实现日志记录，便于排查“路径不匹配”“权限不足”“进程瞬时退出”等失败场景。
- 为自动执行增加更稳妥的重试和退避机制，避免目标进程刚启动时设置失败。
