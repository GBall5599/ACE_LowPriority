# ACE 低优先级工具

这是一个 Windows 图形界面程序，用来调整 AntiCheatExpert 相关进程的运行属性。

目标进程：

- `C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe`
- `C:\Program Files\AntiCheatExpert\SGuard\x64\SGuardSvc64.exe`

程序功能：

- 将目标进程优先级设置为 **低**（Windows 对应 `Idle`）
- 将 CPU 亲和性限制为 **最后一个逻辑处理器**
- 使用图形界面展示 **等待启动 / 正在处理 / 操作成功 / 操作失败** 四种状态
- 启动时自动请求管理员权限
- 操作成功后可直接按 `Enter` 键退出程序

## 主要文件

- `ACE_LowPriority.exe`：主程序
- `Program.cs`：图形界面和处理逻辑源码
- `build-exe.ps1`：重新编译 `exe` 的脚本
- `run-set-ace-process.cmd`：兼容启动入口，会调用 `ACE_LowPriority.exe`
- `UI`：界面设计参考稿
- `任务描述.txt`：原始任务说明

## 使用方法

### 直接运行

双击运行：

- `ACE_LowPriority.exe`

程序启动时会先请求管理员权限，通过后再显示等待界面。

### 操作流程

1. 点击 `启动`
2. 如果当前不是管理员权限，程序会弹出 UAC 提权
3. 提权成功后自动开始处理目标进程
4. 处理完成后显示成功或失败界面
5. 成功时可以点击 `退出`，也可以直接按 `Enter` 键关闭程序

## 当前界面设计

程序界面已经按 `UI` 目录中的设计稿调整：

- 等待启动：居中卡片 + 启动按钮
- 正在处理：处理中状态和按钮反馈
- 操作成功：绿色成功图标 + 退出按钮
- 操作失败：红色失败图标 + 错误详情 + 重试按钮

## 重新编译

如果修改了 `Program.cs`，可以用下面的命令重新生成程序：

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build-exe.ps1
```

默认输出文件：

- `ACE_LowPriority.exe`

## 重要说明

- 程序**不会启动目标程序**，只会处理**已经运行中的目标进程**
- 如果目标进程没有运行，会显示失败界面并提示错误原因
- 如果系统逻辑处理器数量大于 64，会直接显示失败信息
- 如果存在多个同名进程，会优先尝试按完整路径匹配；如果路径无法读取，则退回按进程名处理

