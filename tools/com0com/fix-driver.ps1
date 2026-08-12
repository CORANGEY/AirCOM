# AirCOM - 修复 com0com 驱动安装
# 必须在【管理员】PowerShell 中运行。
# 用法：
#   Set-ExecutionPolicy -Scope Process Bypass -Force
#   & "E:\file\AirCOM\tools\com0com\fix-driver.ps1"
#
# 背景：静默安装(/S)只解压了程序文件，没把内核驱动装进系统。
# 本脚本用 InfDefaultInstall 把驱动真正注册进系统，并验证。

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"
$Com0comDir = "C:\Program Files (x86)\com0com"
$InfPath = Join-Path $Com0comDir "com0com.inf"
$Setupc = Join-Path $Com0comDir "setupc.exe"
$Out = "E:\file\AirCOM\tools\com0com\fix-result.txt"

"=== com0com 驱动修复 ===" | Out-File -FilePath $Out -Encoding utf8
"时间: " + (Get-Date) | Out-File -FilePath $Out -Encoding utf8 -Append
"" | Out-File -FilePath $Out -Encoding utf8 -Append

function Log($msg) {
    Write-Host $msg
    $msg | Out-File -FilePath $Out -Encoding utf8 -Append
}

# 0. 前置检查
if (-not (Test-Path $InfPath)) { Log "错误：找不到 $InfPath"; exit 1 }

# 1. 用 pnputil 把驱动加入驱动仓库并尝试安装
Log "--- 1. pnputil 添加驱动到仓库 ---"
$pnputilOut = & pnputil /add-driver $InfPath /install 2>&1 | Out-String
Log $pnputilOut
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 2. 检查驱动文件是否到位
Log "--- 2. 检查驱动文件 ---"
$sysFile = "C:\Windows\System32\drivers\com0com.sys"
if (Test-Path $sysFile) {
    Log "  OK: $sysFile 存在"
} else {
    Log "  驱动文件未到位，尝试手动复制..."
    Copy-Item (Join-Path $Com0comDir "com0com.sys") $sysFile -Force -ErrorAction SilentlyContinue
    if (Test-Path $sysFile) { Log "  OK: 已复制 com0com.sys" }
    else { Log "  警告：复制失败" }
}
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 3. 检查服务
Log "--- 3. 检查 com0com 服务 ---"
$svc = Get-Service com0com -ErrorAction SilentlyContinue
if ($svc) {
    Log "  OK: 服务已注册，状态=$($svc.Status)，启动类型=$($svc.StartType)"
    if ($svc.Status -ne "Running") {
        Log "  尝试启动服务..."
        try { Start-Service com0com -ErrorAction Stop; Log "  OK: 服务已启动" }
        catch { Log "  启动失败: $_" }
    }
} else {
    Log "  服务未注册，尝试用 sc create 创建..."
    & sc.exe create com0com type= kernel binPath= "System32\drivers\com0com.sys" start= demand 2>&1 | Out-String | ForEach-Object { Log $_ }
    & sc.exe start com0com 2>&1 | Out-String | ForEach-Object { Log $_ }
}
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 4. setupc list
Log "--- 4. setupc list ---"
if (Test-Path $Setupc) {
    try {
        $listOut = & $Setupc list 2>&1 | Out-String
        Log "  setupc 输出:"
        Log $listOut
    } catch { Log "  setupc 出错: $_" }
} else { Log "  setupc.exe 不存在" }
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 5. 设备枚举
Log "--- 5. com0com 设备枚举 ---"
$devs = Get-WmiObject Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "com0com|CNCA|CNCB" }
if ($devs) { $devs | Select-Object Name, DeviceID | Format-Table -AutoSize | Out-String | ForEach-Object { Log $_ } }
else { Log "  未找到 com0com 设备" }
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 6. COM 端口
Log "--- 6. COM 端口列表 ---"
[System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object | ForEach-Object { Log "  $_" }

"" | Out-File -FilePath $Out -Encoding utf8 -Append
Log "=== 修复流程结束 ==="
Log "结果已写入: $Out"

Write-Host ""
Write-Host "如果上方 setupc list 仍为空、且 com0com 服务没起来，" -ForegroundColor Yellow
Write-Host "说明 pnputil 方式未能创建 root 设备实例。" -ForegroundColor Yellow
Write-Host "下一步：手动双击运行图形安装包走完整安装：" -ForegroundColor Cyan
Write-Host "  E:\file\AirCOM\tools\com0com\com0com-setup-x64.exe" -ForegroundColor White
Write-Host "选择 Repair(修复) 或先 Remove 再重装，安装时务必点'始终安装此驱动程序软件'。" -ForegroundColor Cyan
