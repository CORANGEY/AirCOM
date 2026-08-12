# AirCOM - 单机回环测试准备（v2：带超时保护）
# 必须在【管理员】PowerShell 中运行。
# 用法：
#   Set-ExecutionPolicy -Scope Process Bypass -Force
#   & "E:\file\AirCOM\tools\com0com\setup-loopback.ps1"

#Requires -RunAsAdministrator

$ErrorActionPreference = "Continue"
Set-Location "C:\Program Files (x86)\com0com"
$Setupc = "C:\Program Files (x86)\com0com\setupc.exe"
$Out = "E:\file\AirCOM\tools\com0com\setup-loopback-result.txt"

"=== 单机回环准备 v2 ===" | Out-File -FilePath $Out -Encoding utf8
"时间: " + (Get-Date) | Out-File -FilePath $Out -Encoding utf8 -Append
"" | Out-File -FilePath $Out -Encoding utf8 -Append

function Log($msg) {
    Write-Host $msg
    $msg | Out-File -FilePath $Out -Encoding utf8 -Append
}

# 带超时运行 setupc 命令，避免 DIALOG 卡死
function Run-Setupc([string[]]$args, [int]$timeoutMs = 20000) {
    $tmpOut = "E:\file\AirCOM\tools\com0com\_setupc_tmp.txt"
    $proc = Start-Process -FilePath $Setupc -ArgumentList $args -NoNewWindow -PassThru `
        -WorkingDirectory "C:\Program Files (x86)\com0com" `
        -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpOut
    if (-not $proc.WaitForExit($timeoutMs)) {
        Log "  [超时 ${timeoutMs}ms，强制结束 setupc]"
        try { $proc.Kill() } catch { }
    }
    $result = ""
    if (Test-Path $tmpOut) {
        $result = Get-Content $tmpOut -Raw -ErrorAction SilentlyContinue
    }
    return $result
}

# 1. 启动服务
Log "--- 1. com0com 服务 ---"
$svc = Get-Service com0com -ErrorAction SilentlyContinue
if (-not $svc) { Log "  com0com 服务不存在"; exit 1 }
if ($svc.Status -ne "Running") {
    Log "  启动服务..."
    Start-Service com0com
    Start-Sleep -Seconds 1
}
Log "  服务状态: $((Get-Service com0com).Status)"
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 2. 列出现有口对
Log "--- 2. 现有口对 ---"
$listOut = Run-Setupc @("list")
Log $listOut
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 3. 创建 COM10<->COM11（如果没有）
Log "--- 3. 创建 COM10<->COM11 ---"
if ($listOut -match "COM10" -and $listOut -match "COM11") {
    Log "  已存在，跳过"
} else {
    $r = Run-Setupc @("install","PortName=COM10","PortName=COM11")
    Log $r
    Start-Sleep -Seconds 1
}
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 4. 创建 COM12<->COM13（回环用）
Log "--- 4. 创建 COM12<->COM13 ---"
if ($listOut -match "COM12" -and $listOut -match "COM13") {
    Log "  已存在，跳过"
} else {
    $r2 = Run-Setupc @("install","PortName=COM12","PortName=COM13")
    Log $r2
    Start-Sleep -Seconds 1
}
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 5. 配置 EmuBR/EmuOverrun
Log "--- 5. 配置 EmuBR/EmuOverrun ---"
foreach ($port in @("CNCA0","CNCB0","CNCA1","CNCB1")) {
    Run-Setupc @("change",$port,"EmuBR=yes","EmuOverrun=yes") | Out-Null
}
Log "  已配置"
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 6. 最终状态
Log "--- 6. 最终口对状态 ---"
$final = Run-Setupc @("list")
Log $final
"" | Out-File -FilePath $Out -Encoding utf8 -Append

Log "--- 7. COM 端口列表 ---"
[System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object | ForEach-Object { Log "  $_" }

"" | Out-File -FilePath $Out -Encoding utf8 -Append
Log "=== 完成 ==="
Log "回环测试端口："
Log "  COM10/COM11 - A端服务开 COM11，串口软件开 COM10"
Log "  COM12/COM13 - B端服务开 COM13，串口软件开 COM12"
