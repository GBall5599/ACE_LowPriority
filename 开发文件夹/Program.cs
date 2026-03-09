using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("ACE_LowPriority")]
[assembly: AssemblyDescription("ACE 低优先级工具")]
[assembly: AssemblyCompany("GBall5599")]
[assembly: AssemblyProduct("ACE_LowPriority")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

internal enum AppState
{
    Waiting,
    Running,
    Success,
    Failure
}

internal sealed class OperationResult
{
    public OperationResult(bool success, int lastProcessorIndex, int affectedCount, string errorMessage)
    {
        Success = success;
        LastProcessorIndex = lastProcessorIndex;
        AffectedCount = affectedCount;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; private set; }

    public int LastProcessorIndex { get; private set; }

    public int AffectedCount { get; private set; }

    public string ErrorMessage { get; private set; }
}

internal enum CloseChoice
{
    Cancel,
    HideToTray,
    Exit
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (!SingleInstanceManager.TryAcquire())
        {
            SingleInstanceManager.SignalExistingInstance();
            return;
        }

        try
        {
            if (!PrivilegeHelper.IsAdministrator())
            {
                try
                {
                    PrivilegeHelper.RelaunchElevated(args);
                }
                catch (Win32Exception ex)
                {
                    if (ex.NativeErrorCode == 1223)
                    {
                        MessageBox.Show("你已取消管理员授权，程序无法继续运行。", "ACE 低优先级工具", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        MessageBox.Show(string.Format("无法获取管理员权限：{0}", ex.Message), "ACE 低优先级工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format("程序启动失败：{0}", ex.Message), "ACE 低优先级工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return;
            }

            try
            {
                Application.Run(new MainForm(args));
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("程序启动失败：{0}", ex.Message), "ACE 低优先级工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            SingleInstanceManager.Release();
        }
    }
}

internal static class SingleInstanceManager
{
    private const string MutexName = @"Global\ACE_LowPriority_SingleInstance";
    private const string ActivationMessageName = "ACE_LowPriority_ActivateExistingInstance";

    private static Mutex _instanceMutex;
    private static readonly int _activationMessageId = RegisterWindowMessage(ActivationMessageName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    internal static int ActivationMessageId
    {
        get { return _activationMessageId; }
    }

    internal static bool TryAcquire()
    {
        bool createdNew;
        _instanceMutex = new Mutex(true, MutexName, out createdNew);
        return createdNew;
    }

    internal static void SignalExistingInstance()
    {
        if (_activationMessageId != 0)
        {
            PostMessage((IntPtr)0xFFFF, _activationMessageId, IntPtr.Zero, IntPtr.Zero);
        }
    }

    internal static void Release()
    {
        if (_instanceMutex == null)
        {
            return;
        }

        try
        {
            _instanceMutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _instanceMutex.Dispose();
        _instanceMutex = null;
    }
}

internal static class PrivilegeHelper
{
    internal static bool IsAdministrator()
    {
        WindowsIdentity identity = WindowsIdentity.GetCurrent();
        try
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        finally
        {
            identity.Dispose();
        }
    }

    internal static void RelaunchElevated(string[] args)
    {
        string executablePath = Process.GetCurrentProcess().MainModule.FileName;
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = executablePath;
        startInfo.Arguments = BuildArgumentString(args);
        startInfo.UseShellExecute = true;
        startInfo.Verb = "runas";
        startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
        Process.Start(startInfo);
    }

    private static string BuildArgumentString(string[] args)
    {
        List<string> parts = new List<string>();
        for (int index = 0; index < args.Length; index++)
        {
            parts.Add(QuoteArgument(args[index]));
        }

        return string.Join(" ", parts.ToArray());
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        bool requiresQuotes = false;
        for (int index = 0; index < argument.Length; index++)
        {
            char character = argument[index];
            if (char.IsWhiteSpace(character) || character == '"')
            {
                requiresQuotes = true;
                break;
            }
        }

        if (!requiresQuotes)
        {
            return argument;
        }

        return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

internal sealed class MainForm : Form
{
    private static readonly string[] Targets =
    {
        @"C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe",
        @"C:\Program Files\AntiCheatExpert\SGuard\x64\SGuardSvc64.exe"
    };

    private readonly RoundedPanel _cardPanel;
    private readonly RoundedPanel _detailPanel;
    private readonly StatusIconControl _statusIcon;
    private readonly Label _titleLabel;
    private readonly Label _descriptionLabel;
    private readonly Label _detailTitleLabel;
    private readonly TextBox _detailBodyTextBox;
    private readonly RoundedButton _actionButton;
    private readonly Label _footerHintLabel;
    private readonly System.Windows.Forms.Timer _loadingTimer;
    private readonly Label _windowTitleLabel;
    private readonly WindowCaptionButton _minimizeButton;
    private readonly WindowCaptionButton _closeButton;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _showWindowMenuItem;
    private readonly ToolStripMenuItem _runNowMenuItem;
    private readonly ToolStripMenuItem _exitMenuItem;

    private readonly bool _autoStart;
    private AppState _currentState;
    private int _loadingFrame;
    private bool _busy;
    private bool _allowExit;
    private bool _trayHintShown;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    public MainForm(string[] args)
    {
        _autoStart = HasArgument(args, "--auto-start");

        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "ACE 低优先级工具";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowIcon = false;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        ClientSize = new Size(430, 540);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(248, 250, 252);
        KeyPreview = true;

        _cardPanel = new RoundedPanel();
        _cardPanel.BackColor = Color.White;
        _cardPanel.BorderColor = Color.FromArgb(203, 213, 225);
        _cardPanel.CornerRadius = 28;

        _statusIcon = new StatusIconControl();

        _titleLabel = new Label();
        _titleLabel.AutoSize = false;
        _titleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _titleLabel.Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold, GraphicsUnit.Point);
        _titleLabel.ForeColor = Color.FromArgb(30, 41, 59);
        _titleLabel.BackColor = Color.Transparent;

        _descriptionLabel = new Label();
        _descriptionLabel.AutoSize = false;
        _descriptionLabel.TextAlign = ContentAlignment.TopCenter;
        _descriptionLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point);
        _descriptionLabel.ForeColor = Color.FromArgb(100, 116, 139);
        _descriptionLabel.BackColor = Color.Transparent;

        _detailPanel = new RoundedPanel();
        _detailPanel.BackColor = Color.FromArgb(254, 242, 242);
        _detailPanel.BorderColor = Color.FromArgb(254, 226, 226);
        _detailPanel.CornerRadius = 18;
        _detailPanel.Visible = false;

        _detailTitleLabel = new Label();
        _detailTitleLabel.AutoSize = false;
        _detailTitleLabel.Text = "错误详情：";
        _detailTitleLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        _detailTitleLabel.ForeColor = Color.FromArgb(220, 38, 38);
        _detailTitleLabel.BackColor = Color.Transparent;

        _detailBodyTextBox = new TextBox();
        _detailBodyTextBox.Multiline = true;
        _detailBodyTextBox.ReadOnly = true;
        _detailBodyTextBox.BorderStyle = BorderStyle.None;
        _detailBodyTextBox.ScrollBars = ScrollBars.Vertical;
        _detailBodyTextBox.WordWrap = true;
        _detailBodyTextBox.TabStop = false;
        _detailBodyTextBox.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        _detailBodyTextBox.ForeColor = Color.FromArgb(71, 85, 105);
        _detailBodyTextBox.BackColor = Color.FromArgb(254, 242, 242);

        _actionButton = new RoundedButton();
        _actionButton.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
        _actionButton.Click += ActionButton_Click;
        _actionButton.TabStop = true;

        _windowTitleLabel = new Label();
        _windowTitleLabel.AutoSize = false;
        _windowTitleLabel.Text = "ACE 低优先级工具";
        _windowTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _windowTitleLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        _windowTitleLabel.ForeColor = Color.FromArgb(51, 65, 85);
        _windowTitleLabel.BackColor = Color.Transparent;
        _windowTitleLabel.MouseDown += DragSurface_MouseDown;

        _minimizeButton = new WindowCaptionButton();
        _minimizeButton.ButtonKind = CaptionButtonKind.Minimize;
        _minimizeButton.Click += MinimizeButton_Click;

        _closeButton = new WindowCaptionButton();
        _closeButton.ButtonKind = CaptionButtonKind.Close;
        _closeButton.Click += CloseButton_Click;

        _footerHintLabel = new Label();
        _footerHintLabel.AutoSize = false;
        _footerHintLabel.TextAlign = ContentAlignment.TopCenter;
        _footerHintLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        _footerHintLabel.ForeColor = Color.FromArgb(148, 163, 184);
        _footerHintLabel.BackColor = Color.Transparent;

        _cardPanel.Controls.Add(_statusIcon);
        _cardPanel.Controls.Add(_titleLabel);
        _cardPanel.Controls.Add(_descriptionLabel);
        _detailPanel.Controls.Add(_detailTitleLabel);
        _detailPanel.Controls.Add(_detailBodyTextBox);
        _cardPanel.Controls.Add(_detailPanel);

        Controls.Add(_cardPanel);
        Controls.Add(_actionButton);
        Controls.Add(_footerHintLabel);
        Controls.Add(_windowTitleLabel);
        Controls.Add(_minimizeButton);
        Controls.Add(_closeButton);

        _loadingTimer = new System.Windows.Forms.Timer();
        _loadingTimer.Interval = 350;
        _loadingTimer.Tick += LoadingTimer_Tick;

        _trayMenu = new ContextMenuStrip();
        _showWindowMenuItem = new ToolStripMenuItem("显示主界面");
        _runNowMenuItem = new ToolStripMenuItem("立即执行一次");
        _exitMenuItem = new ToolStripMenuItem("退出程序");

        _showWindowMenuItem.Click += ShowWindowMenuItem_Click;
        _runNowMenuItem.Click += RunNowMenuItem_Click;
        _exitMenuItem.Click += ExitMenuItem_Click;

        _trayMenu.Items.AddRange(new ToolStripItem[]
        {
            _showWindowMenuItem,
            _runNowMenuItem,
            new ToolStripSeparator(),
            _exitMenuItem
        });

        _notifyIcon = new NotifyIcon();
        _notifyIcon.Text = "ACE 低优先级工具";
        _notifyIcon.Visible = true;
        _notifyIcon.ContextMenuStrip = _trayMenu;
        _notifyIcon.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
        try
        {
            _notifyIcon.Icon = Icon ?? SystemIcons.Application;
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        Resize += MainForm_Resize;
        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
        MouseDown += DragSurface_MouseDown;

        SetState(AppState.Waiting, null, null);
        LayoutControls();
        UpdateFormRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (LinearGradientBrush backgroundBrush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(248, 250, 252), Color.FromArgb(226, 232, 240), 135F))
        {
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
        }

        using (SolidBrush accentBrushA = new SolidBrush(Color.FromArgb(28, 59, 130, 246)))
        {
            e.Graphics.FillEllipse(accentBrushA, -40, 40, 220, 220);
        }

        using (SolidBrush accentBrushB = new SolidBrush(Color.FromArgb(20, 236, 91, 19)))
        {
            e.Graphics.FillEllipse(accentBrushB, ClientSize.Width - 220, ClientSize.Height - 250, 260, 260);
        }

        DrawCardShadow(e.Graphics, _cardPanel.Bounds, 28);
        using (GraphicsPath borderPath = RoundedPanel.CreateRoundedPath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 28))
        using (Pen borderPen = new Pen(Color.FromArgb(148, 163, 184)))
        {
            borderPen.Width = 2F;
            e.Graphics.DrawPath(borderPen, borderPath);
        }
        base.OnPaint(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == SingleInstanceManager.ActivationMessageId)
        {
            RestoreFromTray();
            Activate();
            return;
        }

        base.WndProc(ref message);
    }

    private void MainForm_Shown(object sender, EventArgs e)
    {
        if (_autoStart)
        {
            BeginOperation();
        }
    }

    private void MainForm_Resize(object sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray();
            return;
        }

        UpdateFormRegion();
        LayoutControls();
        Invalidate();
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (_allowExit || e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
        {
            return;
        }

        CloseChoice choice = ClosePromptDialog.ShowChoice(this);
        if (choice == CloseChoice.Exit)
        {
            _allowExit = true;
            return;
        }

        e.Cancel = true;
        if (choice == CloseChoice.HideToTray)
        {
            HideToTray();
        }
    }

    private void LoadingTimer_Tick(object sender, EventArgs e)
    {
        if (_currentState != AppState.Running)
        {
            return;
        }

        _loadingFrame = (_loadingFrame + 1) % 4;
        if (_loadingFrame == 0)
        {
            _actionButton.Text = "处理中";
        }
        else if (_loadingFrame == 1)
        {
            _actionButton.Text = "处理中.";
        }
        else if (_loadingFrame == 2)
        {
            _actionButton.Text = "处理中..";
        }
        else
        {
            _actionButton.Text = "处理中...";
        }
    }

    private void ActionButton_Click(object sender, EventArgs e)
    {
        if (_currentState == AppState.Success)
        {
            HideToTray();
            return;
        }

        if (_currentState == AppState.Waiting || _currentState == AppState.Failure)
        {
            BeginOperation();
        }
    }

    private void BeginOperation()
    {
        if (_busy)
        {
            return;
        }

        if (!PrivilegeHelper.IsAdministrator())
        {
            ShowFailure("当前程序未以管理员权限运行，请重新启动程序。", "权限不足，无法修改目标进程。程序启动时会自动请求管理员权限。");
            return;
        }

        _busy = true;
        SetState(AppState.Running, "正在设置目标进程的优先级和 CPU 亲和性，请稍候...", null);
        ThreadPool.QueueUserWorkItem(PerformOperation);
    }

    private void PerformOperation(object state)
    {
        OperationResult result;

        try
        {
            int logicalProcessorCount = Environment.ProcessorCount;
            if (logicalProcessorCount < 1)
            {
                throw new InvalidOperationException("未检测到可用的逻辑处理器。");
            }

            if (logicalProcessorCount > 64)
            {
                throw new InvalidOperationException(string.Format("当前检测到 {0} 个逻辑处理器，程序目前仅支持最多 64 个。", logicalProcessorCount));
            }

            int lastProcessorIndex = logicalProcessorCount - 1;
            long affinityMask = 1L << lastProcessorIndex;
            int affectedCount = 0;

            foreach (string targetPath in Targets)
            {
                IList<Process> processes = GetTargetProcesses(targetPath);
                for (int index = 0; index < processes.Count; index++)
                {
                    SetTargetProcessState(processes[index], affinityMask);
                    affectedCount++;
                }
            }

            result = new OperationResult(true, lastProcessorIndex, affectedCount, null);
        }
        catch (Exception ex)
        {
            result = new OperationResult(false, -1, 0, ex.Message);
        }

        try
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    _busy = false;
                    if (result.Success)
                    {
                        ShowSuccess(result);

                        if (!Visible || WindowState == FormWindowState.Minimized)
                        {
                            ShowTrayBalloon("操作成功", string.Format("已成功设置 {0} 个目标进程。", result.AffectedCount), ToolTipIcon.Info);
                        }
                    }
                    else
                    {
                        ShowFailure("处理过程中出现问题，请查看下方错误详情后重试。", result.ErrorMessage);
                        RestoreFromTray();
                        Activate();
                        ShowTrayBalloon("操作失败", "处理失败，已打开主界面显示错误详情。", ToolTipIcon.Error);
                    }
                });
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ShowSuccess(OperationResult result)
    {
        string description = string.Format("已成功设置 {0} 个目标进程，仅使用 CPU {1}。", result.AffectedCount, result.LastProcessorIndex);
        SetState(AppState.Success, description, null);
    }

    private void ShowFailure(string description, string errorMessage)
    {
        SetState(AppState.Failure, description, errorMessage);
    }

    private void NotifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            RestoreFromTray();
        }
    }

    private void ShowWindowMenuItem_Click(object sender, EventArgs e)
    {
        RestoreFromTray();
    }

    private void RunNowMenuItem_Click(object sender, EventArgs e)
    {
        BeginOperation();
    }

    private void ExitMenuItem_Click(object sender, EventArgs e)
    {
        _allowExit = true;
        Close();
    }

    private void MinimizeButton_Click(object sender, EventArgs e)
    {
        WindowState = FormWindowState.Minimized;
    }

    private void CloseButton_Click(object sender, EventArgs e)
    {
        Close();
    }

    private void DragSurface_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    private void HideToTray()
    {
        if (IsDisposed)
        {
            return;
        }

        Hide();
        ShowInTaskbar = false;

        if (!_trayHintShown)
        {
            _trayHintShown = true;
            ShowTrayBalloon("已最小化到托盘", "程序正在后台常驻，可从托盘图标恢复或执行操作。", ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        if (IsDisposed)
        {
            return;
        }

        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    private void ShowTrayBalloon(string title, string text, ToolTipIcon icon)
    {
    }

    private void UpdateFormRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        using (GraphicsPath path = RoundedPanel.CreateRoundedPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 28))
        {
            Region = new Region(path);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
            }
        }

        base.Dispose(disposing);
    }




    private void SetState(AppState state, string description, string detailMessage)
    {
        _currentState = state;
        _detailPanel.Visible = false;
        _descriptionLabel.Visible = true;
        _detailBodyTextBox.Text = string.Empty;
        _loadingFrame = 0;
        _loadingTimer.Stop();

        if (state == AppState.Waiting)
        {
            _statusIcon.VisualState = AppState.Waiting;
            _titleLabel.Text = "等待启动";
            _descriptionLabel.Text = "点击下方按钮开始任务";
            _actionButton.Enabled = true;
            _actionButton.Text = "启动";
            _actionButton.NormalColor = Color.FromArgb(59, 130, 246);
            _actionButton.HoverColor = Color.FromArgb(37, 99, 235);
            _actionButton.DisabledColor = Color.FromArgb(147, 197, 253);
            _footerHintLabel.Text = "点击按钮开始运行";
            AcceptButton = _actionButton;
        }
        else if (state == AppState.Running)
        {
            _statusIcon.VisualState = AppState.Running;
            _titleLabel.Text = "正在处理中";
            _descriptionLabel.Text = description;
            _actionButton.Enabled = false;
            _actionButton.Text = "处理中";
            _actionButton.NormalColor = Color.FromArgb(59, 130, 246);
            _actionButton.HoverColor = Color.FromArgb(59, 130, 246);
            _actionButton.DisabledColor = Color.FromArgb(147, 197, 253);
            _footerHintLabel.Text = "请稍候，程序正在处理目标进程";
            AcceptButton = null;
            _loadingTimer.Start();
        }
        else if (state == AppState.Success)
        {
            _statusIcon.VisualState = AppState.Success;
            _titleLabel.Text = "操作成功";
            _descriptionLabel.Text = description;
            _actionButton.Enabled = true;
            _actionButton.Text = "隐藏到托盘";
            _actionButton.NormalColor = Color.FromArgb(100, 116, 139);
            _actionButton.HoverColor = Color.FromArgb(71, 85, 105);
            _actionButton.DisabledColor = Color.FromArgb(148, 163, 184);
            _footerHintLabel.Text = "按 Enter 键或点击按钮即可隐藏到托盘";
            AcceptButton = _actionButton;
        }
        else
        {
            _statusIcon.VisualState = AppState.Failure;
            _titleLabel.Text = "操作失败";
            _descriptionLabel.Text = description;
            _detailPanel.Visible = true;
            _detailBodyTextBox.Text = detailMessage ?? string.Empty;
            _actionButton.Enabled = true;
            _actionButton.Text = "重试";
            _actionButton.NormalColor = Color.FromArgb(236, 91, 19);
            _actionButton.HoverColor = Color.FromArgb(214, 77, 11);
            _actionButton.DisabledColor = Color.FromArgb(253, 186, 116);
            _footerHintLabel.Text = "点击按钮重新尝试";
            AcceptButton = _actionButton;
        }

        LayoutControls();
        Invalidate();
    }

    private void LayoutControls()
    {
        int cardWidth = Math.Min(ClientSize.Width - 44, 372);
        int cardHeight = _currentState == AppState.Failure ? 354 : 252;
        int cardLeft = (ClientSize.Width - cardWidth) / 2;
        int cardTop = 68;

        _windowTitleLabel.Bounds = new Rectangle(18, 12, 180, 28);
        _closeButton.Bounds = new Rectangle(ClientSize.Width - 18 - 26, 12, 26, 26);
        _minimizeButton.Bounds = new Rectangle(_closeButton.Left - 6 - 26, 12, 26, 26);

        _cardPanel.Bounds = new Rectangle(cardLeft, cardTop, cardWidth, cardHeight);

        int iconSize = 80;
        _statusIcon.Bounds = new Rectangle((_cardPanel.Width - iconSize) / 2, 24, iconSize, iconSize);

        _titleLabel.Bounds = new Rectangle(20, _statusIcon.Bottom + 14, _cardPanel.Width - 40, 38);

        if (_currentState == AppState.Failure)
        {
            _descriptionLabel.Bounds = new Rectangle(28, _titleLabel.Bottom + 6, _cardPanel.Width - 56, 40);
            _detailPanel.Bounds = new Rectangle(20, _descriptionLabel.Bottom + 10, _cardPanel.Width - 40, 116);
            _detailTitleLabel.Bounds = new Rectangle(14, 12, _detailPanel.Width - 28, 22);
            _detailBodyTextBox.Bounds = new Rectangle(14, 38, _detailPanel.Width - 28, 64);
        }
        else
        {
            _descriptionLabel.Bounds = new Rectangle(28, _titleLabel.Bottom + 8, _cardPanel.Width - 56, 48);
        }

        int buttonWidth = Math.Max(150, cardWidth / 2);
        int buttonLeft = (ClientSize.Width - buttonWidth) / 2;
        int buttonBottomMargin = 24;
        int buttonTop = ClientSize.Height - buttonBottomMargin - 52;
        int footerTop = buttonTop - 34;

        _actionButton.Bounds = new Rectangle(buttonLeft, buttonTop, buttonWidth, 52);
        _footerHintLabel.Bounds = new Rectangle(cardLeft + 8, footerTop, cardWidth - 16, 24);
    }

    private static bool HasArgument(string[] args, string expected)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }




    private static IList<Process> GetTargetProcesses(string executablePath)
    {
        string processName = Path.GetFileNameWithoutExtension(executablePath);
        Process[] processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            throw new InvalidOperationException(string.Format("目标进程未运行：{0}", executablePath));
        }

        List<Process> exactPathMatches = new List<Process>();
        List<Process> unknownPathMatches = new List<Process>();

        for (int index = 0; index < processes.Length; index++)
        {
            Process process = processes[index];
            string resolvedPath = GetReadableProcessPath(process);
            if (string.IsNullOrEmpty(resolvedPath))
            {
                unknownPathMatches.Add(process);
                continue;
            }

            if (string.Equals(resolvedPath, executablePath, StringComparison.OrdinalIgnoreCase))
            {
                exactPathMatches.Add(process);
            }
        }

        if (exactPathMatches.Count > 0)
        {
            return exactPathMatches;
        }

        if (unknownPathMatches.Count > 0)
        {
            return unknownPathMatches;
        }

        throw new InvalidOperationException(string.Format("已找到同名进程 {0}，但路径与目标不匹配。", processName));
    }

    private static string GetReadableProcessPath(Process process)
    {
        try
        {
            return process.MainModule.FileName;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void SetTargetProcessState(Process process, long affinityMask)
    {
        process.PriorityClass = ProcessPriorityClass.Idle;
        process.ProcessorAffinity = (IntPtr)affinityMask;
        process.Refresh();
    }

    private static void DrawCardShadow(Graphics graphics, Rectangle bounds, int cornerRadius)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        for (int layer = 0; layer < 5; layer++)
        {
            Rectangle shadowBounds = new Rectangle(bounds.X - layer, bounds.Y + 4 + layer, bounds.Width + layer * 2, bounds.Height + layer * 2);
            int alpha = 24 - layer * 4;
            if (alpha < 0)
            {
                alpha = 0;
            }

            using (GraphicsPath path = RoundedPanel.CreateRoundedPath(shadowBounds, cornerRadius + layer))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, 15, 23, 42)))
            {
                graphics.FillPath(brush, path);
            }
        }
    }
}

internal sealed class RoundedPanel : Panel
{
    public RoundedPanel()
    {
        DoubleBuffered = true;
        BorderColor = Color.Transparent;
        CornerRadius = 24;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    public Color BorderColor { get; set; }

    public int CornerRadius { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rectangle = new Rectangle(0, 0, Width - 1, Height - 1);

        using (GraphicsPath path = CreateRoundedPath(rectangle, CornerRadius))
        using (SolidBrush brush = new SolidBrush(BackColor))
        using (Pen pen = new Pen(BorderColor))
        {
            e.Graphics.FillPath(brush, path);
            if (BorderColor.A > 0)
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        base.OnPaint(e);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        if (Width > 0 && Height > 0)
        {
            using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region = new Region(path);
            }
        }
    }

    public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        GraphicsPath path = new GraphicsPath();

        if (diameter > bounds.Width)
        {
            diameter = bounds.Width;
        }

        if (diameter > bounds.Height)
        {
            diameter = bounds.Height;
        }

        Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        NormalColor = Color.FromArgb(59, 130, 246);
        HoverColor = Color.FromArgb(37, 99, 235);
        DisabledColor = Color.FromArgb(147, 197, 253);
        TabStop = true;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    public Color NormalColor { get; set; }

    public Color HoverColor { get; set; }

    public Color DisabledColor { get; set; }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rectangle = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fillColor = ResolveFillColor();

        using (GraphicsPath path = RoundedPanel.CreateRoundedPath(rectangle, 20))
        using (SolidBrush brush = new SolidBrush(fillColor))
        {
            pevent.Graphics.FillPath(brush, path);
        }

        TextRenderer.DrawText(pevent.Graphics, Text, Font, rectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventargs);
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(eventargs);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width > 0 && Height > 0)
        {
            using (GraphicsPath path = RoundedPanel.CreateRoundedPath(new Rectangle(0, 0, Width, Height), 20))
            {
                Region = new Region(path);
            }
        }
    }

    private Color ResolveFillColor()
    {
        if (!Enabled)
        {
            return DisabledColor;
        }

        if (_pressed)
        {
            return ControlPaint.Dark(HoverColor, 0.08F);
        }

        if (_hovered)
        {
            return HoverColor;
        }

        return NormalColor;
    }
}

internal enum CaptionButtonKind
{
    Minimize,
    Close
}

internal sealed class WindowCaptionButton : Control
{
    private bool _hovered;
    private bool _pressed;
    private CaptionButtonKind _buttonKind;

    public WindowCaptionButton()
    {
        Size = new Size(26, 26);
        Cursor = Cursors.Hand;
        ForeColor = Color.FromArgb(37, 99, 235);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public CaptionButtonKind ButtonKind
    {
        get { return _buttonKind; }
        set
        {
            _buttonKind = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

        Color fillColor = Color.Transparent;
        if (_pressed)
        {
            fillColor = ButtonKind == CaptionButtonKind.Close ? Color.FromArgb(255, 254, 226, 226) : Color.FromArgb(255, 219, 234, 254);
        }
        else if (_hovered)
        {
            fillColor = ButtonKind == CaptionButtonKind.Close ? Color.FromArgb(255, 254, 242, 242) : Color.FromArgb(255, 239, 246, 255);
        }

        using (GraphicsPath path = RoundedPanel.CreateRoundedPath(rect, 10))
        {
            if (fillColor.A > 0)
            {
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

        }

        using (Pen pen = new Pen(ButtonKind == CaptionButtonKind.Close ? Color.FromArgb(234, 88, 12) : ForeColor, 1.8F))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;

            if (ButtonKind == CaptionButtonKind.Minimize)
            {
                float y = Height * 0.62F;
                e.Graphics.DrawLine(pen, Width * 0.32F, y, Width * 0.68F, y);
            }
            else
            {
                e.Graphics.DrawLine(pen, Width * 0.34F, Height * 0.34F, Width * 0.66F, Height * 0.66F);
                e.Graphics.DrawLine(pen, Width * 0.66F, Height * 0.34F, Width * 0.34F, Height * 0.66F);
            }
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width > 0 && Height > 0)
        {
            using (GraphicsPath path = RoundedPanel.CreateRoundedPath(new Rectangle(0, 0, Width, Height), 10))
            {
                Region = new Region(path);
            }
        }
    }
}

internal sealed class ClosePromptDialog : Form
{
    private readonly Label _titleLabel;
    private readonly Label _messageLabel;
    private readonly RoundedButton _hideButton;
    private readonly RoundedButton _exitButton;
    private readonly WindowCaptionButton _closeButton;
    private CloseChoice _choice;

    private ClosePromptDialog()
    {
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "退出提示";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(368, 198);
        BackColor = Color.FromArgb(239, 246, 255);
        DoubleBuffered = true;

        _titleLabel = new Label();
        _titleLabel.Text = "是否关闭程序？";
        _titleLabel.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point);
        _titleLabel.ForeColor = Color.FromArgb(30, 41, 59);
        _titleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _titleLabel.AutoSize = false;
        _titleLabel.BackColor = Color.Transparent;

        _messageLabel = new Label();
        _messageLabel.Text = "你可以隐藏到托盘继续后台运行，或直接关闭程序。";
        _messageLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        _messageLabel.ForeColor = Color.FromArgb(100, 116, 139);
        _messageLabel.TextAlign = ContentAlignment.MiddleCenter;
        _messageLabel.AutoSize = false;
        _messageLabel.BackColor = Color.Transparent;

        _hideButton = new RoundedButton();
        _hideButton.Text = "隐藏到托盘";
        _hideButton.NormalColor = Color.FromArgb(59, 130, 246);
        _hideButton.HoverColor = Color.FromArgb(37, 99, 235);
        _hideButton.DisabledColor = Color.FromArgb(147, 197, 253);
        _hideButton.Click += delegate { _choice = CloseChoice.HideToTray; DialogResult = DialogResult.OK; Close(); };

        _exitButton = new RoundedButton();
        _exitButton.Text = "关闭程序";
        _exitButton.NormalColor = Color.FromArgb(234, 88, 12);
        _exitButton.HoverColor = Color.FromArgb(194, 65, 12);
        _exitButton.DisabledColor = Color.FromArgb(253, 186, 116);
        _exitButton.Click += delegate { _choice = CloseChoice.Exit; DialogResult = DialogResult.OK; Close(); };

        _closeButton = new WindowCaptionButton();
        _closeButton.ButtonKind = CaptionButtonKind.Close;
        _closeButton.Click += delegate { _choice = CloseChoice.Cancel; DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(_titleLabel);
        Controls.Add(_messageLabel);
        Controls.Add(_hideButton);
        Controls.Add(_exitButton);
        Controls.Add(_closeButton);

        AcceptButton = _hideButton;
        Shown += ClosePromptDialog_Shown;
        Resize += ClosePromptDialog_Resize;
        LayoutDialog();
        UpdateDialogRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (SolidBrush backgroundBrush = new SolidBrush(Color.FromArgb(239, 246, 255)))
        {
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
        }


        base.OnPaint(e);
    }

    private void ClosePromptDialog_Shown(object sender, EventArgs e)
    {
        _hideButton.Focus();
    }

    private void ClosePromptDialog_Resize(object sender, EventArgs e)
    {
        LayoutDialog();
        UpdateDialogRegion();
        Invalidate();
    }

    private void LayoutDialog()
    {
        _closeButton.Bounds = new Rectangle(ClientSize.Width - 16 - 26, 12, 26, 26);
        _titleLabel.Bounds = new Rectangle(24, 24, ClientSize.Width - 76, 32);
        _messageLabel.Bounds = new Rectangle(28, 66, ClientSize.Width - 68, 38);

        int buttonWidth = 92;
        int buttonHeight = 42;
        int spacing = 14;
        int totalWidth = buttonWidth * 2 + spacing;
        int left = (ClientSize.Width - totalWidth) / 2;
        int top = ClientSize.Height - 58;

        _hideButton.Bounds = new Rectangle(left, top, buttonWidth, buttonHeight);
        _exitButton.Bounds = new Rectangle(left + buttonWidth + spacing, top, buttonWidth, buttonHeight);
    }

    private void UpdateDialogRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        using (GraphicsPath path = RoundedPanel.CreateRoundedPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 28))
        {
            Region = new Region(path);
        }
    }

    public static CloseChoice ShowChoice(IWin32Window owner)
    {
        using (ClosePromptDialog dialog = new ClosePromptDialog())
        {
            dialog.ShowDialog(owner);
            return dialog._choice;
        }
    }
}

internal sealed class StatusIconControl : Control
{
    private AppState _visualState;

    public StatusIconControl()
    {
        Size = new Size(92, 92);
        VisualState = AppState.Waiting;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    public AppState VisualState
    {
        get { return _visualState; }
        set
        {
            if (_visualState == value)
            {
                return;
            }

            _visualState = value;
            Invalidate();
            Update();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle outer = new Rectangle(0, 0, Width - 1, Height - 1);

        Color backgroundColor;
        Color strokeColor;

        if (VisualState == AppState.Success)
        {
            backgroundColor = Color.FromArgb(236, 253, 245);
            strokeColor = Color.FromArgb(16, 185, 129);
        }
        else if (VisualState == AppState.Failure)
        {
            backgroundColor = Color.FromArgb(254, 242, 242);
            strokeColor = Color.FromArgb(239, 68, 68);
        }
        else if (VisualState == AppState.Running)
        {
            backgroundColor = Color.FromArgb(239, 246, 255);
            strokeColor = Color.FromArgb(59, 130, 246);
        }
        else
        {
            backgroundColor = Color.FromArgb(241, 245, 249);
            strokeColor = Color.FromArgb(148, 163, 184);
        }

        using (SolidBrush brush = new SolidBrush(backgroundColor))
        {
            e.Graphics.FillEllipse(brush, outer);
        }

        if (VisualState == AppState.Success)
        {
            DrawSuccessIcon(e.Graphics, strokeColor);
        }
        else if (VisualState == AppState.Failure)
        {
            DrawFailureIcon(e.Graphics, strokeColor);
        }
        else
        {
            DrawClockIcon(e.Graphics, strokeColor);
        }
    }

    private void DrawClockIcon(Graphics graphics, Color strokeColor)
    {
        float centerX = Width / 2F;
        float centerY = Height / 2F;
        float circleSize = Math.Min(Width, Height) * 0.5F;
        float circleLeft = centerX - circleSize / 2F;
        float circleTop = centerY - circleSize / 2F;
        float lineWidth = Math.Max(4F, Math.Min(Width, Height) * 0.055F);

        using (Pen pen = new Pen(strokeColor, lineWidth))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            graphics.DrawEllipse(pen, circleLeft, circleTop, circleSize, circleSize);
            graphics.DrawLine(pen, centerX, centerY, centerX, centerY - circleSize * 0.3F);
            graphics.DrawLine(pen, centerX, centerY, centerX + circleSize * 0.22F, centerY + circleSize * 0.12F);
        }
    }

    private void DrawSuccessIcon(Graphics graphics, Color strokeColor)
    {
        float lineWidth = Math.Max(6F, Math.Min(Width, Height) * 0.075F);
        PointF pointA = new PointF(Width * 0.30F, Height * 0.54F);
        PointF pointB = new PointF(Width * 0.44F, Height * 0.68F);
        PointF pointC = new PointF(Width * 0.71F, Height * 0.36F);

        using (Pen pen = new Pen(strokeColor, lineWidth))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            graphics.DrawLines(pen, new PointF[] { pointA, pointB, pointC });
        }
    }

    private void DrawFailureIcon(Graphics graphics, Color strokeColor)
    {
        float centerX = Width / 2F;
        float topY = Height * 0.3F;
        float bottomY = Height * 0.62F;
        float dotCenterY = Height * 0.78F;
        float dotSize = Math.Max(6F, Math.Min(Width, Height) * 0.08F);
        float lineWidth = Math.Max(5F, Math.Min(Width, Height) * 0.065F);

        using (Pen pen = new Pen(strokeColor, lineWidth))
        using (SolidBrush brush = new SolidBrush(strokeColor))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            graphics.DrawLine(pen, centerX, topY, centerX, bottomY);
            graphics.FillEllipse(brush, centerX - dotSize / 2F, dotCenterY - dotSize / 2F, dotSize, dotSize);
        }
    }
}
















