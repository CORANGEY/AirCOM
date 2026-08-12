# AirCOM - COM10<->COM11 回环连通性测试
# 不需要管理员权限（打开 COM 端口普通权限即可）。
# 用法（任意 PowerShell）：
#   & "E:\file\AirCOM\tools\com0com\test-loopback.ps1"

Add-Type -AssemblyName System.IO.Ports

$msg = "AirCOM-loopback-test"
$msgBytes = [System.Text.Encoding]::ASCII.GetBytes($msg)

try {
    # 先关掉可能残留的端口句柄
    $rx = New-Object System.IO.Ports.SerialPort("COM11", 115200)
    $rx.ReadTimeout = 3000
    $rx.Open()
    Start-Sleep -Milliseconds 200

    $tx = New-Object System.IO.Ports.SerialPort("COM10", 115200)
    $tx.Open()
    Start-Sleep -Milliseconds 200

    # 发送
    $tx.Write($msgBytes, 0, $msgBytes.Length)
    Start-Sleep -Milliseconds 500

    # 接收
    $buf = New-Object byte[] 64
    $n = $rx.Read($buf, 0, 64)
    $text = [System.Text.Encoding]::ASCII.GetString($buf, 0, $n)

    Write-Host "发送: $msg"
    Write-Host "收到: $text"

    if ($text -eq $msg) {
        Write-Host "RESULT: PASS - COM10<->COM11 连通正常，com0com 工作中!" -ForegroundColor Green
    } else {
        Write-Host "RESULT: FAIL - 收到数据与发送不一致" -ForegroundColor Red
    }

    $tx.Close()
    $rx.Close()
}
catch {
    Write-Host "RESULT: ERROR - $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "可能原因:"
    Write-Host "  - COM10 或 COM11 不存在"
    Write-Host "  - 端口被其他程序占用"
    Write-Host "  - com0com 驱动未加载"
}
