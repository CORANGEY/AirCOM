# AirCOM - 创建 com0com root 设备并配置虚拟口对
# 必须在【管理员】PowerShell 中运行。
# 用法：
#   Set-ExecutionPolicy -Scope Process Bypass -Force
#   & "E:\file\AirCOM\tools\com0com\create-ports.ps1"
#
# 前提：com0com 服务已运行（由 fix-driver.ps1 完成）。
# 本脚本：把服务改为自动启动 + 创建 root 设备实例 + 创建 COM10/COM11 口对 + 配置参数。

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"
$Setupc = "C:\Program Files (x86)\com0com\setupc.exe"
$InfPath = "C:\Program Files (x86)\com0com\com0com.inf"
$Com0comDir = "C:\Program Files (x86)\com0com"
$Out = "E:\file\AirCOM\tools\com0com\create-ports-result.txt"

# setupc.exe 在当前工作目录查找 com0com.inf，必须切到安装目录
Set-Location $Com0comDir

"=== 创建 com0com root 设备与口对 ===" | Out-File -FilePath $Out -Encoding utf8
"时间: " + (Get-Date) | Out-File -FilePath $Out -Encoding utf8 -Append
"" | Out-File -FilePath $Out -Encoding utf8 -Append

function Log($msg) {
    Write-Host $msg
    $msg | Out-File -FilePath $Out -Encoding utf8 -Append
}

# 1. 确保服务在运行并改为自动启动
Log "--- 1. 服务状态 ---"
$svc = Get-Service com0com -ErrorAction SilentlyContinue
if (-not $svc) {
    Log "  com0com 服务不存在，先用 fix-driver.ps1 修复"
    exit 1
}
Log "  当前状态: $($svc.Status), 启动类型: $($svc.StartType)"
if ($svc.StartType -ne "Automatic") {
    Log "  改为自动启动..."
    Set-Service com0com -StartupType Automatic -ErrorAction SilentlyContinue
    Log "  已改为 Automatic"
}
if ($svc.Status -ne "Running") {
    Log "  启动服务..."
    Start-Service com0com -ErrorAction SilentlyContinue
}
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 2. 创建 root 设备实例（这是 setupc install 的关键前置）
Log "--- 2. 创建 root\com0com 设备实例 ---"
try {
    # setupc install 正确语法: install PortName=COM10 PortName=COM11
    # 用 Start-Process 单字符串参数避免等号被拆分，并指定工作目录让 setupc 找到 inf
    Log "  尝试 setupc install PortName=COM10 PortName=COM11（带超时）..."
    $outFile = "E:\file\AirCOM\tools\com0com\setupc-install-out.txt"
    $errFile = "E:\file\AirCOM\tools\com0com\setupc-install-err.txt"
    # 用 cmd /c 包一层，把整个命令行原样传给 setupc，避免 PowerShell 拆分 PortName=COM10
    $cmdLine = '"{0}" install PortName=COM10 PortName=COM11' -f $Setupc
    $proc = Start-Process -FilePath "cmd.exe" -ArgumentList "/c",$cmdLine -NoNewWindow -PassThru -WorkingDirectory $Com0comDir -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    if (-not $proc.WaitForExit(15000)) {
        Log "  setupc install 超时(15s)，强制结束..."
        $proc.Kill()
        Log "  (setupc 可能已在等待交互输入)"
    }
    if (Test-Path $outFile) {
        $so = Get-Content $outFile -Raw -ErrorAction SilentlyContinue
        Log "  stdout: $so"
    }
    if (Test-Path $errFile) {
        $se = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        if ($se) { Log "  stderr: $se" }
    }
} catch {
    Log "  setupc install 异常: $_"
}
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 3. setupc list
Log "--- 3. setupc list ---"
try {
    $listOut = & $Setupc list 2>&1 | Out-String
    Log "  输出:"
    Log $listOut
} catch { Log "  出错: $_" }
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 4. 如果口对存在，配置参数
Log "--- 4. 配置 EmuBR / 控制线映射 ---"
if ($listOut -match "COM10") {
    foreach ($port in @("CNCA0", "CNCB0")) {
        try { & $Setupc change $port EmuBR=yes EmuOverrun=yes 2>&1 | Out-Null } catch { }
    }
    Log "  EmuBR/EmuOverrun 已配置"
    try { & $Setupc change CNCA0 MapOD=ON 2>&1 | Out-Null } catch { }
    try { & $Setupc change CNCB0 MapOD=ON 2>&1 | Out-Null } catch { }
    Log "  控制线映射已配置"
} else {
    Log "  口对未创建，跳过参数配置"
}
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 5. 设备枚举与端口
Log "--- 5. com0com 设备枚举 ---"
$devs = Get-WmiObject Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "com0com|CNCA|CNCB" }
if ($devs) { $devs | Select-Object Name, DeviceID | Format-Table -AutoSize | Out-String | ForEach-Object { Log $_ } }
else { Log "  未找到 com0com 设备" }
"" | Out-File -FilePath $Out -Encoding utf8 -Append

Log "--- 6. COM 端口列表 ---"
[System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object | ForEach-Object { Log "  $_" }

"" | Out-File -FilePath $Out -Encoding utf8 -Append
Log "=== 流程结束 ==="
Log "结果已写入: $Out"
