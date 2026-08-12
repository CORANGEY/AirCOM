using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using AirCOM.App.Services;
using AirCOM.Client.A.Services;
using AirCOM.Client.B.Services;
using AirCOM.Core.Engine;
using AirCOM.Core.Protocol;
using AirCOM.Core.Serial;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirCOM.App.ViewModels;

/// <summary>
/// Unified work view model for both roles. A single window with role-gated
/// controls: A-side fills B's IP+port and connects (uses a com0com virtual port);
/// B-side picks a real serial port and listens.
/// </summary>
public partial class WorkViewModel : ObservableObject, IDisposable
{
    private readonly AppRole _role;
    private readonly Window _window;
    private AEngineHost? _aHost;
    private BEngineHost? _bHost;

    public WorkViewModel(AppRole role, Window window)
    {
        _role = role;
        _window = window;

        // Load persisted settings (shared across versions via %AppData%\AirCOM).
        var s = AppSettings.Load();
        RemoteHost = string.IsNullOrEmpty(s.LastRemoteHost) ? "192.168.137.2" : s.LastRemoteHost;
        ListenPort = s.LastTcpPort > 0 ? s.LastTcpPort : 51000;
        RemotePort = s.LastTcpPort > 0 ? s.LastTcpPort : 51000;
        BaudRate = s.LastBaudRate > 0 ? s.LastBaudRate : 115200;

        if (role == AppRole.BSide)
        {
            RefreshPorts();
            // Restore last selected port if it still exists.
            SelectedPort = (!string.IsNullOrEmpty(s.LastSelectedPort) && Ports.Contains(s.LastSelectedPort))
                ? s.LastSelectedPort
                : (Ports.FirstOrDefault() ?? "");
        }
        VirtualPortDisplay = "待分配";
        Com0comStatus = "";
        UpdateActionLabel();
    }

    [ObservableProperty] private ObservableCollection<string> _ports = new();
    [ObservableProperty] private string _selectedPort = "";
    [ObservableProperty] private string _remoteHost = "";
    [ObservableProperty] private int _remotePort = 51000;
    [ObservableProperty] private int _listenPort = 51000;
    [ObservableProperty] private int _baudRate = 115200;
    [ObservableProperty] private string _virtualPortDisplay = "";
    [ObservableProperty] private string _com0comStatus = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "未启动";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _actionButtonLabel = "启动";
    [ObservableProperty] private System.Windows.Media.Brush _actionButtonColor = System.Windows.Media.Brushes.DodgerBlue;

    public bool IsASide => _role == AppRole.ASide;
    public bool IsBSide => _role == AppRole.BSide;
    public string RoleDisplay => IsASide ? "A 端（使用远程串口）" : "B 端（共享本地串口）";
    public string WindowTitle => $"AirCOM - {RoleDisplay}";

    partial void OnIsRunningChanged(bool value) => UpdateActionLabel();

    private void UpdateActionLabel()
    {
        if (IsASide)
        {
            ActionButtonLabel = IsRunning ? "断开" : "连接";
            ActionButtonColor = IsRunning ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.DodgerBlue;
        }
        else
        {
            ActionButtonLabel = IsRunning ? "停止" : "启动监听";
            ActionButtonColor = IsRunning ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.SeaGreen;
        }
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        var current = SelectedPort;
        Ports = new ObservableCollection<string>(SerialPortEnumerator.GetPortNames());
        if (Ports.Contains(current)) SelectedPort = current;
        else SelectedPort = Ports.FirstOrDefault() ?? "";
    }

    [RelayCommand]
    private async Task ActionAsync()
    {
        if (IsRunning)
        {
            await StopInternalAsync();
            return;
        }

        if (IsASide) await StartASideAsync();
        else await StartBSideAsync();
    }

    private async Task StartASideAsync()
    {
        if (!IPAddress.TryParse(RemoteHost, out var ip))
        {
            AppendLog("IP 地址格式不正确");
            return;
        }

        // Pick a free COM pair automatically on first use; reuse the stored
        // assignment afterwards so the user's serial app keeps opening the same COM.
        var settings = AirCOM.App.Services.AppSettings.Load();
        if (string.IsNullOrEmpty(settings.UserPort) || string.IsNullOrEmpty(settings.ServicePort))
        {
            var mgr0 = new AirCOM.Client.A.Com0com.Com0comManager();
            var (a, b) = mgr0.FindFreePortPair();
            settings.UserPort = a;
            settings.ServicePort = b;
            settings.Save();
        }
        var userPort = settings.UserPort;
        var servicePort = settings.ServicePort;
        Com0comStatus = $"虚拟口：{userPort}（串口软件开此口）/ 服务用 {servicePort}";

        // Check com0com state; prompt + run elevated repair if not ready.
        var mgr = new AirCOM.Client.A.Com0com.Com0comManager();
        var state = mgr.CheckState(userPort, servicePort);
        if (state != AirCOM.Client.A.Com0com.Com0comState.Ready)
        {
            AppendLog($"com0com 未就绪（{state}），需要以管理员权限修复");
            Com0comStatus = $"状态：{state}，需提权修复";
            string hint = state switch
            {
                AirCOM.Client.A.Com0com.Com0comState.DriverNotInstalled => "com0com 驱动未安装。",
                AirCOM.Client.A.Com0com.Com0comState.ServiceStopped => "com0com 服务未运行。",
                AirCOM.Client.A.Com0com.Com0comState.PairMissing => $"虚拟口对 {userPort}/{servicePort} 不存在。",
                _ => "",
            };
            if (MessageBox.Show(
                $"{hint}\n\n即将以管理员权限运行修复脚本（会弹 UAC 提示）。\n随包的 com0com 驱动带商业签名，装完即可用，无需重启。\n\n继续吗？",
                "AirCOM - 需要修复 com0com", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                AppendLog("用户取消修复");
                Com0comStatus = "已取消";
                return;
            }

            AppendLog("启动提权修复脚本（请在 UAC 窗口点“是”）...");
            bool launched = mgr.LaunchElevatedRepair(userPort, servicePort);
            if (!launched)
            {
                AppendLog("修复脚本启动失败");
                Com0comStatus = "修复启动失败";
                return;
            }

            // Re-check after repair. (If a reboot was required, the user will have
            // been told so by the script; state will still not be Ready here.)
            state = mgr.CheckState(userPort, servicePort);
            if (state != AirCOM.Client.A.Com0com.Com0comState.Ready)
            {
                AppendLog($"修复后仍未就绪（{state}）。请检查 com0com 是否安装成功。");
                Com0comStatus = $"修复后：{state}（可能需重启）";
                return;
            }
        }

        Com0comStatus = "";
        AppendLog("com0com 就绪");

        try
        {
            _aHost = new AEngineHost();
            _aHost.StatsUpdated += OnStats;
            _aHost.Stopped += OnStopped;
            _aHost.ConnectionStateChanged += OnConnState;

            var p = new SerialParams((uint)BaudRate, 8, StopBitsKind.One, Parity.None, FlowControl.None);
            await _aHost.StartAsync(servicePort, p, ip, RemotePort);
            VirtualPortDisplay = $"{userPort}（串口软件开此口）↔ {servicePort}";
            IsRunning = true;
            StatusText = $"已连接 {RemoteHost}:{RemotePort}";
            AppendLog($"已连接 B 端 {RemoteHost}:{RemotePort}，串口软件请打开 {userPort}");
            SaveSettings(); // persist for next launch
        }
        catch (Exception ex)
        {
            string msg = FriendlyError(ex, RemoteHost, RemotePort);
            AppendLog($"连接失败：{msg}");
            Com0comStatus = $"失败：{msg}";
            _aHost?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _aHost = null;
        }
    }

    private async Task StartBSideAsync()
    {
        if (string.IsNullOrEmpty(SelectedPort))
        {
            AppendLog("请先选择要共享的串口");
            return;
        }

        try
        {
            _bHost = new BEngineHost();
            _bHost.StatsUpdated += OnStats;
            _bHost.Stopped += OnStopped;
            _bHost.ConnectionStateChanged += OnConnState;

            var p = new SerialParams((uint)BaudRate, 8, StopBitsKind.One, Parity.None, FlowControl.None);
            await _bHost.StartAsync(SelectedPort, p, ListenPort);
            IsRunning = true;
            StatusText = $"监听中（端口 {ListenPort}），共享 {SelectedPort}";
            AppendLog($"已启动：串口 {SelectedPort} @ {BaudRate}，监听 {ListenPort}");
            SaveSettings(); // persist for next launch
        }
        catch (Exception ex)
        {
            AppendLog($"启动失败：{FriendlyError(ex, SelectedPort, ListenPort)}");
            _bHost?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _bHost = null;
        }
    }

    private async Task StopInternalAsync()
    {
        if (_aHost is not null) { await _aHost.DisposeAsync(); _aHost = null; }
        if (_bHost is not null) { await _bHost.DisposeAsync(); _bHost = null; }
        IsRunning = false;
        StatusText = "已停止";
        AppendLog("已停止");
    }

    [RelayCommand]
    private void Back()
    {
        if (IsRunning)
        {
            if (MessageBox.Show("正在运行，确定返回角色选择？", "确认", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
                return;
            StopInternalAsync().GetAwaiter().GetResult();
        }
        var roleWin = new Views.RoleSelectWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        roleWin.Show();
        _window.Close();
    }

    private void OnStats(object? s, FlowStats stats) =>
        AppendLog($"流量：→网络 {stats.BytesSerialToNet} B，→串口 {stats.BytesNetToSerial} B");

    private void OnStopped(object? s, Exception? ex)
    {
        // Bridge stopped (peer disconnected or error). Reset UI to allow reconnect.
        // Runs on a background thread -> dispatch to UI thread.
        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            AppendLog($"桥接停止：{(ex is null ? "正常" : ex.Message)}");
            if (_aHost is not null) { try { _aHost.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { } _aHost = null; }
            if (_bHost is not null) { try { _bHost.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { } _bHost = null; }
            IsRunning = false;
            StatusText = "已断开（可重新连接）";
        }));
    }

    private void OnConnState(object? s, bool connected)
    {
        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            AppendLog(connected ? "对端已连接" : "对端已断开");
            if (!connected && IsRunning)
            {
                StatusText = "对端已断开（正在停止...）";
            }
        }));
    }

    private void AppendLog(string msg) => LogText += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";

    /// <summary>Translates common exceptions into clear Chinese hints.</summary>
    private string FriendlyError(Exception ex, string hostOrPort, int tcpPort)
    {
        string m = ex.Message ?? "";
        // Serial port in use ("拒绝访问" / "Access denied")
        if (ex is System.UnauthorizedAccessException || m.Contains("拒绝访问") || m.Contains("Access Denied"))
        {
            return $"串口 {SelectedPort} 被其他程序占用，请关闭占用它的程序后重试";
        }
        // TCP port in use
        if (ex is System.Net.Sockets.SocketException se &&
            (se.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse
             || m.Contains("通常只允许使用") || m.Contains("Only one usage")))
        {
            return $"监听端口 {tcpPort} 已被占用，请换个端口或关闭占用它的程序";
        }
        // A-side can't reach B-side (connection refused / timeout)
        if (ex is System.Net.Sockets.SocketException)
        {
            return $"无法连接到 {hostOrPort}:{tcpPort}。请检查：1) B 端是否已启动监听；2) IP/端口是否正确；3) 网络是否通；4) B 端防火墙是否放行";
        }
        return m;
    }

    /// <summary>Saves the current inputs (host/port/baud/selected port) so the next launch reuses them.</summary>
    private void SaveSettings()
    {
        try
        {
            var s = AppSettings.Load();
            // Preserve the assigned port pair (don't overwrite UserPort/ServicePort).
            if (IsASide) s.LastRemoteHost = RemoteHost;
            if (IsBSide) s.LastSelectedPort = SelectedPort;
            s.LastTcpPort = IsASide ? RemotePort : ListenPort;
            s.LastBaudRate = BaudRate;
            s.Save();
        }
        catch { /* settings persistence is best-effort */ }
    }

    public void Dispose()
    {
        _aHost?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _bHost?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
