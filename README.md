# ACE_LowPriority

这是一个可直接运行的 Windows 小工具，用来调整 AntiCheatExpert 相关进程的运行属性。

## 文件说明

- `ACE_LowPriority.exe`：主程序，双击即可运行

## 软件功能

程序会对以下目标进程进行处理：

- `C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe`
- `C:\Program Files\AntiCheatExpert\SGuard\x64\SGuardSvc64.exe`

处理内容：

- 将进程优先级设置为 **低**（Windows 对应 `Idle`）
- 将 CPU 亲和性限制到 **最后一个逻辑处理器**（CPU 编号最大的那个线程）

## 使用方法

1. 双击运行 `ACE_LowPriority.exe`
2. 程序启动时会自动请求管理员权限
3. 进入界面后点击 `启动`
4. 程序会自动处理目标进程
5. 处理完成后：
   - 成功：显示绿色对勾，可以点击 `退出`，也可以直接按 `Enter` 关闭程序
   - 失败：显示错误信息，可以点击 `重试`

## 使用前提

请确认以下条件：

- 操作系统为 Windows
- 允许程序获取管理员权限
- 上述目标进程已经在运行中

## 注意事项

- 本程序**不会启动目标进程**，只会处理**已经运行中的进程**
- 如果目标进程未运行，会显示失败界面
- 如果你需要源码、UI 设计稿和构建脚本，请切换到 `source` 分支查看

## 分支说明

- `main`：仅保留最终可执行文件和使用说明
- `source`：保留源码、UI 设计稿、构建脚本和开发文件
