# AirCOM - com0com 安装与配置脚本
# 必须在【管理员】PowerShell 中运行。
#
# 用法（管理员 PowerShell 里）：
#   Set-ExecutionPolicy -Scope Process Bypass -Force
#   & "E:\file\AirCOM\tools\com0com\install-com0com.ps1"
#
# 本脚本依次完成：
#   1. 开启 Windows 测试签名模式，需重启生效
#   2. 静默安装 com0com 驱动
#   3. 创建虚拟串口对 COM10 与 COM11
#   4. 配置 EmuBR（波特率模拟）+ EmuOverrun（溢出丢弃）+ 控制线映射
#   5. 验证安装结果

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"
$Installer = Join-Path $PSScriptRoot "com0com-setup-x64.exe"

function Write-Step($msg) { Write-Host ""; Write-Host ">>> $msg" -ForegroundColor Cyan }
function Write-Ok($msg) { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "    !!  $msg" -ForegroundColor Yellow }

if (-not (Test-Path $Installer)) {
    Write-Error "找不到安装包: $Installer"
    exit 1
}

$NeedReboot = $false

# com0com 可能装到 64 位或 32 位 Program Files，自动探测。
$Setupc = @(
    (Join-Path $env:ProgramFiles "com0com\setupc.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "com0com\setupc.exe")
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $Setupc) {
    # 32 位环境变量可能为空，兜底用硬编码路径。
    if (Test-Path "C:\Program Files (x86)\com0com\setupc.exe") {
        $Setupc = "C:\Program Files (x86)\com0com\setupc.exe"
    } elseif (Test-Path "C:\Program Files\com0com\setupc.exe") {
        $Setupc = "C:\Program Files\com0com\setupc.exe"
    }
}

# 1. 测试签名模式
Write-Step "检查 / 开启测试签名模式"
$ts = bcdedit /enum "{current}" | Select-String -Pattern "testsigning" -SimpleMatch
if ($ts -and $ts.ToString() -match "Yes") {
    Write-Ok "测试签名已开启，跳过"
} else {
    bcdedit /set testsigning on | Out-Null
    Write-Ok "已开启测试签名（重启后生效）"
    $NeedReboot = $true
}

# 2. 安装 com0com 驱动
Write-Step "安装 com0com 驱动"
if ($Setupc) {
    Write-Ok "com0com 已安装于: $Setupc，跳过"
} else {
    Write-Host "    运行静默安装... (可能弹出驱动安装提示，请允许)"
    Start-Process -FilePath $Installer -ArgumentList "/S" -Wait
    # 安装后重新探测路径
    if (Test-Path "C:\Program Files (x86)\com0com\setupc.exe") {
        $Setupc = "C:\Program Files (x86)\com0com\setupc.exe"
    } elseif (Test-Path "C:\Program Files\com0com\setupc.exe") {
        $Setupc = "C:\Program Files\com0com\setupc.exe"
    }
    if (-not $Setupc) {
        Write-Error "静默安装后仍找不到 setupc.exe。请尝试手动双击运行安装包后重跑本脚本。"
        exit 1
    }
    Write-Ok "com0com 安装完成于: $Setupc"
}

# 3. 创建虚拟串口对 COM10 与 COM11
Write-Step "创建虚拟串口对 COM10 与 COM11"
$existing = & $Setupc list 2>&1
if ($existing -match "COM10") {
    Write-Ok "COM10/COM11 口对已存在，跳过创建"
} else {
    & $Setupc install COM10 COM11 | Out-Null
    Write-Ok "口对创建完成"
}

# 4. 配置 EmuBR / EmuOverrun / 控制线映射
Write-Step "配置端口参数（EmuBR / 控制线映射）"
$list = & $Setupc list 2>&1
Write-Host "    当前口对: $list"

foreach ($port in @("CNCA0", "CNCB0")) {
    & $Setupc change $port EmuBR=yes EmuOverrun=yes 2>&1 | Out-Null
}
Write-Ok "EmuBR / EmuOverrun 已开启"

try { & $Setupc change CNCA0 MapOD=ON 2>&1 | Out-Null } catch { }
try { & $Setupc change CNCB0 MapOD=ON 2>&1 | Out-Null } catch { }
Write-Ok "控制线映射已配置"

# 5. 验证
Write-Step "验证安装结果"
$final = & $Setupc list 2>&1
Write-Host "    setupc list 输出:"
Write-Host $final

if ($final -match "COM10" -and $final -match "COM11") {
    Write-Ok "COM10 与 COM11 均已就绪"
} else {
    Write-Warn2 "口对创建似乎未成功，请检查上方输出"
}

Write-Host ""
Write-Host "========== 安装流程完成 ==========" -ForegroundColor Green
if ($NeedReboot) {
    Write-Host "[必须重启电脑] 测试签名模式与驱动才会生效" -ForegroundColor Yellow
    Write-Host "重启后再次运行本脚本可验证。" -ForegroundColor Yellow
} else {
    Write-Host "测试签名此前已开启，驱动应可直接加载。" -ForegroundColor Green
}
