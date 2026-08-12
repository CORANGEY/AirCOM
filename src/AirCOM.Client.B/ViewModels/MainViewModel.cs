using System.Collections.ObjectModel;
using System.Windows;
using AirCOM.Client.B.Services;
using AirCOM.Core.Engine;
using AirCOM.Core.Protocol;
using AirCOM.Core.Serial;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirCOM.Client.B.ViewModels;

/// <summary>
/// B-side main view model. Lets the user pick a real COM port, a listen port,
/// and start the engine. The A-side connects to this listen port over TCP.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private BEngineHost? _host;

    public MainViewModel()
    {
        RefreshPorts();
        SelectedPort = Ports.FirstOrDefault() ?? "";
        ListenPort = 51000;
        BaudRate = 115200;
    }

    [ObservableProperty]
    private ObservableCollection<string> _ports = new();

    [ObservableProperty]
    private string _selectedPort = "";

    [ObservableProperty]
    private int _listenPort = 51000;

    [ObservableProperty]
    private int _baudRate = 115200;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "未启动";

    [ObservableProperty]
    private string _logText = "";

    [RelayCommand]
    private void RefreshPorts()
    {
        var current = SelectedPort;
        Ports = new ObservableCollection<string>(SerialPortEnumerator.GetPortNames());
        if (Ports.Contains(current)) SelectedPort = current;
        else SelectedPort = Ports.FirstOrDefault() ?? "";
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRunning) return;
        if (string.IsNullOrEmpty(SelectedPort))
        {
            MessageBox.Show("请先选择要共享的串口");
            return;
        }

        try
        {
            _host = new BEngineHost();
            _host.StatsUpdated += OnStats;
            _host.Stopped += OnStopped;
            _host.ConnectionStateChanged += OnConnState;

            var p = new SerialParams((uint)BaudRate, 8, StopBitsKind.One, Parity.None, FlowControl.None);
            await _host.StartAsync(SelectedPort, p, ListenPort);
            IsRunning = true;
            StatusText = $"监听中，等待 A 端连接（端口 {ListenPort}），共享 {SelectedPort}";
            AppendLog($"已启动：串口 {SelectedPort} @ {BaudRate}，监听 {ListenPort}");
        }
        catch (Exception ex)
        {
            AppendLog($"启动失败：{ex.Message}");
            MessageBox.Show($"启动失败：{ex.Message}", "错误");
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
        _host = null;
        IsRunning = false;
        StatusText = "已停止";
        AppendLog("已停止");
    }

    private void OnStats(object? s, FlowStats stats) =>
        AppendLog($"流量统计：串口→网络 {stats.BytesSerialToNet} B，网络→串口 {stats.BytesNetToSerial} B");

    private void OnStopped(object? s, Exception? ex) =>
        AppendLog($"桥接停止：{(ex is null ? "正常" : ex.Message)}");

    private void OnConnState(object? s, bool connected) =>
        AppendLog(connected ? "A 端已连接" : "A 端已断开");

    private void AppendLog(string msg)
    {
        LogText += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
    }

    public void Dispose()
    {
        if (_host is not null) _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
