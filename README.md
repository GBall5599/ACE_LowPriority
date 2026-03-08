# ACE 优先级调低脚本

这个项目用于在 **Windows** 下快速调整 AntiCheatExpert 相关进程的运行属性。

脚本会对以下目标进程进行处理：

- `C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe`
- `C:\Program Files\AntiCheatExpert\SGuard\x64\SGuardSvc64.exe`

处理内容：

- 将进程优先级设置为 **低**（Windows 对应 `Idle`）
- 将 CPU 亲和性限制为 **最后一个逻辑处理器**（也就是 CPU 编号最大的那个线程）

## 文件说明

- `run-set-ace-process.cmd`：双击即可运行的入口
- `set-ace-process.ps1`：实际执行逻辑的 PowerShell 脚本
- `任务描述.txt`：原始任务说明

## 运行要求

- 操作系统：Windows
- 权限要求：**管理员权限**
- PowerShell：Windows 自带 PowerShell 即可

> 脚本会自动尝试以管理员权限重新启动自己。

## 使用方法

### 方式一：直接双击运行

直接运行：

- `run-set-ace-process.cmd`

### 方式二：手动用 PowerShell 运行

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\set-ace-process.ps1
```

## 脚本执行逻辑

脚本会按下面的流程工作：

1. 检查当前是否为管理员权限
2. 如果不是管理员，则弹出 UAC 提权
3. 获取当前机器的逻辑处理器数量
4. 选择最后一个逻辑处理器
5. 查找已运行的目标进程
6. 将目标进程优先级设置为 `Idle`
7. 将目标进程 CPU 亲和性限制到最后一个逻辑处理器
8. 输出成功或失败结果
9. 等待按下回车后退出

## 重要说明

- 这个脚本**不会启动目标程序**，只会处理**已经在运行中的进程**。
- 如果目标进程没有运行，脚本会打印失败信息并退出。
- 如果系统逻辑处理器数量大于 64，脚本会直接报错退出。
- 如果存在多个同名进程，脚本会优先尝试按完整路径精确匹配；如果路径无法读取，则退回按进程名处理。

## 成功示例

```text
Running as administrator.
Target logical processor: CPU 31
Priority target: low priority (Idle)

SUCCESS: C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe (PID 12345) -> Idle priority, CPU 31 only
SUCCESS: C:\Program Files\AntiCheatExpert\SGuard\x64\SGuardSvc64.exe (PID 23456) -> Idle priority, CPU 31 only

All target processes were updated successfully.
```

## 失败示例

```text
FAILED: Target process is not running: C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe
```

## 可按需扩展

如果后续需要，可以继续扩展成以下版本：

- 启动后先等待几秒再检测进程
- 循环等待目标进程出现后再设置
- 输出中文提示
- 生成日志文件
- 开机自动运行
