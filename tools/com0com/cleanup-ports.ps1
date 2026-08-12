# AirCOM - 清理多余 com0com 虚拟口对
# 必须在【管理员】PowerShell 中运行。
# 用法：
#   & "E:\file\AirCOM\tools\com0com\cleanup-ports.ps1"
# 它会列出所有 com0com 口对，让你输入要保留的口对，删掉其余的。

#Requires -RunAsAdministrator

$ErrorActionPreference = "Continue"
Set-Location "C:\Program Files (x86)\com0com"
$Setupc = "C:\Program Files (x86)\com0com\setupc.exe"

if (-not (Test-Path $Setupc)) {
    Write-Host "找不到 setupc.exe，com0com 可能未安装" -ForegroundColor Red
    exit 1
}

Write-Host "=== 当前所有 com0com 口对 ===" -ForegroundColor Cyan
$list = & $Setupc list 2>&1
Write-Host $list
Write-Host ""

# 解析出口对列表（形如 CNCA0 PortName=COM50 / CNCB0 PortName=COM51 成对）
$pairs = @{}
$lines = $list -split "`n" | Where-Object { $_ -match 'CNC[AB]\d+\s+PortName=' }
foreach ($line in $lines) {
    if ($line -match '(CNC[AB]\d+)\s+PortName=(COM\d+)') {
        $cnc = $Matches[1]; $com = $Matches[2]
        $pairs[$cnc] = $com
    }
}

# 按 CNCAn/CNCBn 配对
$pairGroups = @{}
foreach ($cnc in $pairs.Keys | Sort-Object) {
    $num = $cnc -replace 'CNC[AB]',''
    if (-not $pairGroups.ContainsKey($num)) { $pairGroups[$num] = @() }
    $pairGroups[$num] += $cnc
}

Write-Host "=== 识别到的口对 ===" -ForegroundColor Cyan
foreach ($num in ($pairGroups.Keys | Sort-Object {[int]$_})) {
    $members = $pairGroups[$num]
    $comNames = ($members | ForEach-Object { $pairs[$_] }) -join " <-> "
    Write-Host ("  口对 {0}: {1}  ({2})" -f $num, ($members -join "/"), $comNames)
}
Write-Host ""

if ($pairGroups.Count -eq 0) {
    Write-Host "没有找到 com0com 口对" -ForegroundColor Yellow
    exit 0
}
if ($pairGroups.Count -eq 1) {
    Write-Host "只有一对口对，无需清理" -ForegroundColor Green
    exit 0
}

$keep = Read-Host "输入要保留的口对编号（如 0、1、2...），其余将被删除"
if (-not $keep -or -not ($pairGroups.ContainsKey($keep))) {
    Write-Host "输入无效，已取消" -ForegroundColor Yellow
    exit 0
}

foreach ($num in $pairGroups.Keys) {
    if ($num -eq $keep) { continue }
    foreach ($cnc in $pairGroups[$num]) {
        Write-Host "删除 $cnc ..." -ForegroundColor Yellow
        & $Setupc remove $cnc 2>&1 | Out-Null
    }
}

Write-Host ""
Write-Host "=== 清理后剩余口对 ===" -ForegroundColor Green
& $Setupc list 2>&1
Write-Host ""
Write-Host "清理完成" -ForegroundColor Green
Write-Host "提示：如果 AirCOM 的 settings 指向的口对被删了，下次启动会自动重新分配。" -ForegroundColor Cyan
