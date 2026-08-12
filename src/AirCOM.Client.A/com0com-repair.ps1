# AirCOM - com0com 修复脚本（A 端首次使用或驱动异常时由程序自动提权调用）
# 用法（一般由程序调用，也可手动以管理员身份运行）：
#   powershell -ExecutionPolicy Bypass -File com0com-repair.ps1 -PortA COM10 -PortB COM11
#
# 做的事：
#   1. 确保 com0com 驱动已安装并运行（必要时 pnputil + sc create + 启动）
#   2. 确保虚拟口对 -PortA/-PortB 存在（setupc install）
#   3. 配置 EmuBR/EmuOverrun
#   4. 检查测试签名模式（未开启则开启并提示重启）
# 结果写入 com0com-repair-result.txt

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

# 1. 测试签名
Log "--- 1. 测试签名模式 ---"
$ts = bcdedit /enum "{current}" 2>&1 | Select-String -Pattern "testsigning" -SimpleMatch
if ($ts -and $ts.ToString() -match "Yes") { Log "  已开启" }
else {
    Log "  未开启，执行 bcdedit /set testsigning on"
    bcdedit /set testsigning on | Out-Null
    $NeedReboot = $true
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
        & sc.exe create com0com type= kernel binPath= "System32\drivers\com0com.sys" start= demand 2>&1 | Out-String | ForEach-Object { Log $_ }
    } else { Log "  找不到 inf，安装失败" }
    $svc = Get-Service com0com -ErrorAction SilentlyContinue
}
if ($svc) {
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
if ($NeedReboot) {
    Log ">>> 必须重启电脑，测试签名和驱动才会生效 <<<"
    Log "重启后再次运行 AirCOM 即可正常使用。"
} else {
    Log "驱动已就绪，无需重启。"
}
Log "结果已写入: $OutFile"

if ($NeedReboot) {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show("com0com 已配置，但需要重启电脑让测试签名生效。重启后再次运行 AirCOM 即可。", "AirCOM - 需要重启", "OK", "Warning") | Out-Null
} else {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show("com0com 驱动已就绪，$PortA <-> $PortB 口对已创建。", "AirCOM - 修复完成", "OK", "Information") | Out-Null
}
