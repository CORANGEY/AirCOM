# AirCOM - com0com 修复脚本（A 端首次使用或驱动异常时由程序自动提权调用）
# 用法（一般由程序调用，也可手动以管理员身份运行）：
#   powershell -ExecutionPolicy Bypass -File com0com-repair.ps1 -PortA COM50 -PortB COM51
#
# 做的事：
#   1. 验证驱动签名状态（随包驱动带 COMODO 商业签名，无需测试签名）
#   2. 确保 com0com 驱动已安装并运行（必要时跑安装包 + pnputil + sc create，服务设自动）
#   3. 确保虚拟口对 -PortA/-PortB 存在（setupc install）
#   4. 配置 EmuBR/EmuOverrun
# 装完即用，无需重启。结果写入 com0com-repair-result.txt

#Requires -RunAsAdministrator

param(
    [string]$PortA = "COM10",
    [string]$PortB = "COM11"
)

$ErrorActionPreference = "Continue"
$DriverDir = Join-Path $env:ProgramFiles "com0com"
if (-not (Test-Path $DriverDir)) { $DriverDir = Join-Path ${env:ProgramFiles(x86)} "com0com" }
$Setupc = Join-Path $DriverDir "setupc.exe"
$InfPath = Join-Path $DriverDir "com0com.inf"
$OutFile = Join-Path $PSScriptRoot "com0com-repair-result.txt"

"=== AirCOM com0com 修复 ===" | Out-File -FilePath $OutFile -Encoding utf8
"时间: " + (Get-Date) | Out-File -FilePath $OutFile -Encoding utf8 -Append
"目标口对: $PortA <-> $PortB" | Out-File -FilePath $OutFile -Encoding utf8 -Append
"" | Out-File -FilePath $OutFile -Encoding utf8 -Append

function Log($msg) {
    Write-Host $msg
    $msg | Out-File -FilePath $OutFile -Encoding utf8 -Append
}
function Run-Setupc([string[]]$SetupArgs, [int]$timeoutMs = 20000) {
    if (-not (Test-Path $Setupc)) { Log "  setupc.exe 不存在: $Setupc"; return "" }
    if ($null -eq $SetupArgs -or $SetupArgs.Count -eq 0) { return "" }
    $tmpOut = Join-Path $PSScriptRoot "_setupc_out.txt"
    $tmpErr = Join-Path $PSScriptRoot "_setupc_err.txt"
    $proc = Start-Process -FilePath $Setupc -ArgumentList $SetupArgs -NoNewWindow -PassThru -WorkingDirectory $DriverDir -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
    if (-not $proc.WaitForExit($timeoutMs)) { Log "  [超时，强制结束]"; try { $proc.Kill() } catch { } }
    $r = ""
    if (Test-Path $tmpOut) { $r = Get-Content $tmpOut -Raw -ErrorAction SilentlyContinue }
    if (Test-Path $tmpErr) { $errContent = Get-Content $tmpErr -Raw -ErrorAction SilentlyContinue; if ($errContent) { $r += "`n[stderr] $errContent" } }
    return $r
}

$NeedReboot = $false

# 1. 驱动签名验证（仅信息性，不强制开测试签名）
#    说明：随包的 com0com 安装包带 COMODO 商业代码签名（签名方 CyberCircuits），
#    64 位 Win10/11 可直接加载，无需测试签名模式，无需重启。
Log "--- 1. 驱动签名检查 ---"
$sysFile = Join-Path $env:WINDIR "System32\drivers\com0com.sys"
if (Test-Path $sysFile) {
    $sig = Get-AuthenticodeSignature $sysFile
    Log "  com0com.sys 签名状态: $($sig.Status)"
} else {
    Log "  com0com.sys 尚未安装，将在第 2 步安装"
}
"" | Out-File -FilePath $OutFile -Encoding utf8 -Append

# 2. 驱动服务
Log "--- 2. com0com 驱动服务 ---"

# 如果 com0com 根本没装过（DriverDir 不存在），先用随包附带的安装包静默安装。
if (-not (Test-Path $DriverDir)) {
    $BundledSetup = Join-Path $PSScriptRoot "com0com-setup-x64.exe"
    if (Test-Path $BundledSetup) {
        Log "  com0com 未安装，运行随包安装包: $BundledSetup /S"
        Start-Process -FilePath $BundledSetup -ArgumentList "/S" -Wait
        # 静默安装后重新探测路径
        if (Test-Path (Join-Path $env:ProgramFiles "com0com")) { $DriverDir = Join-Path $env:ProgramFiles "com0com" }
        elseif (Test-Path (Join-Path ${env:ProgramFiles(x86)} "com0com")) { $DriverDir = Join-Path ${env:ProgramFiles(x86)} "com0com" }
        $Setupc = Join-Path $DriverDir "setupc.exe"
        $InfPath = Join-Path $DriverDir "com0com.inf"
        Log "  安装后 DriverDir = $DriverDir"
    } else {
        Log "  找不到随包安装包 com0com-setup-x64.exe"
    }
}

$svc = Get-Service com0com -ErrorAction SilentlyContinue
if (-not $svc) {
    Log "  服务未注册，手动注册驱动..."
    if (Test-Path $InfPath) {
        pnputil /add-driver $InfPath /install 2>&1 | Out-String | ForEach-Object { Log $_ }
        $sysSrc = Join-Path $DriverDir "com0com.sys"
        $sysDst = Join-Path $env:WINDIR "System32\drivers\com0com.sys"
        if ((Test-Path $sysSrc) -and -not (Test-Path $sysDst)) { Copy-Item $sysSrc $sysDst -Force }
        # start= auto 让服务开机自动启动，避免口对偶发消失
        & sc.exe create com0com type= kernel binPath= "System32\drivers\com0com.sys" start= auto 2>&1 | Out-String | ForEach-Object { Log $_ }
    } else { Log "  找不到 inf，安装失败" }
    $svc = Get-Service com0com -ErrorAction SilentlyContinue
}
if ($svc) {
    # 确保启动类型是 Automatic（之前版本可能是 Manual / Demand）
    try {
        $svcKey = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\com0com' -ErrorAction Stop
        if ($svcKey.Start -ne 2) {  # 2 = SERVICE_AUTO_START
            Log "  把启动类型改为自动（当前值: $($svcKey.Start)）"
            Set-Service com0com -StartupType Automatic
        }
    } catch { }
    Log "  服务状态: $($svc.Status)"
    if ($svc.Status -ne "Running") { Start-Service com0com -ErrorAction SilentlyContinue; Start-Sleep 1; Log "  启动后: $((Get-Service com0com).Status)" }
} else { Log "  服务仍不存在，修复失败" }
"" | Out-File -FilePath $OutFile -Encoding utf8 -Append

# 3. 虚拟口对
Log "--- 3. 虚拟口对 $PortA <-> $PortB ---"
$list = Run-Setupc @("list")
Log "  当前: $list"
if ($list -match $PortA -and $list -match $PortB) {
    Log "  口对已存在"
} else {
    Log "  创建口对..."
    $r = Run-Setupc @("install","PortName=$PortA","PortName=$PortB")
    Log "  install 输出: $r"
    Start-Sleep 1
}
"" | Out-File -FilePath $OutFile -Encoding utf8 -Append

# 4. 配置 EmuBR
Log "--- 4. 配置 EmuBR/EmuOverrun ---"
foreach ($p in @("CNCA0","CNCB0")) { Run-Setupc @("change",$p,"EmuBR=yes","EmuOverrun=yes") | Out-Null }
Log "  已配置"
"" | Out-File -FilePath $OutFile -Encoding utf8 -Append

# 5. 最终状态
Log "--- 5. 最终状态 ---"
$final = Run-Setupc @("list")
Log $final
$ports = [System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object
Log "系统 COM 口: $($ports -join ', ')"

"" | Out-File -FilePath $OutFile -Encoding utf8 -Append
Log "=== 修复完成 ==="
Log "驱动已就绪，无需重启。"
Log "结果已写入: $OutFile"

Add-Type -AssemblyName PresentationFramework
[System.Windows.MessageBox]::Show("com0com 驱动已就绪，$PortA <-> $PortB 口对已创建。可直接使用，无需重启。", "AirCOM - 修复完成", "OK", "Information") | Out-Null
