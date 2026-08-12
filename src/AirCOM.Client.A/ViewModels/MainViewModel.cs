using System.Net;
using System.Windows;
using AirCOM.Client.A.Services;
using AirCOM.Core.Engine;
using AirCOM.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirCOM.Client.A.ViewModels;

/// <summary>
/// A-side main view model. User enters the B-side IP+port, picks the local
/// com0com virtual port (COM11), and connects. The user's serial app on COM10
/// then talks through to the remote device.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private AEngineHost? _host;

    public MainViewModel()
    {
        RemoteHost = "127.0.0.1";
        RemotePort = 51000;
        VirtualPortName = "COM11";
        BaudRate = 115200;
    }

    [ObservableProperty] private string _remoteHost = "127.0.0.1";
    [ObservableProperty] private int _remotePort = 51000;
    [ObservableProperty] private string _virtualPortName = "COM11";
    [ObservableProperty] private int _baudRate = 115200;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _logText = "";

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsConnected) return;
        if (!IPAddress.TryParse(RemoteHost, out var ip))
        {
            MessageBox.Show("IP 地址格式不正确");
            return;
        }

        try
        {
            _host = new AEngineHost();
            _host.StatsUpdated += OnStats;
            _host.Stopped += OnStopped;
            _host.ConnectionStateChanged += OnConnState;

            var p = new SerialParams((uint)BaudRate, 8, StopBitsKind.One, Parity.None, FlowControl.None);
            await _host.StartAsync(VirtualPortName, p, ip, RemotePort);
            IsConnected = true;
            StatusText = $"已连接 {RemoteHost}:{RemotePort}，虚拟口 {VirtualPortName}（应用请开 COM10）";
            AppendLog($"已连接 B 端 {RemoteHost}:{RemotePort}，虚拟口 {VirtualPortName}");
        }
        catch (Exception ex)
        {
            AppendLog($"连接失败：{ex.Message}");
            MessageBox.Show($"连接失败：{ex.Message}", "错误");
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
        _host = null;
        IsConnected = false;
        StatusText = "已断开";
        AppendLog("已断开");
    }

    private void OnStats(object? s, FlowStats stats) =>
        AppendLog($"流量统计：应用->网络 {stats.BytesSerialToNet} B，网络->应用 {stats.BytesNetToSerial} B");

    private void OnStopped(object? s, Exception? ex) =>
        AppendLog($"桥接停止：{(ex is null ? "正常" : ex.Message)}");

    private void OnConnState(object? s, bool connected) =>
        AppendLog(connected ? "连接已建立" : "连接已断开");

    private void AppendLog(string msg) => LogText += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";

    public void Dispose()
    {
        if (_host is not null) _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
