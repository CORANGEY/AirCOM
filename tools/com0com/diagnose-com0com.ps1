# AirCOM - com0com 安装状态诊断脚本
# 必须在【管理员】PowerShell 中运行。
# 用法：
#   Set-ExecutionPolicy -Scope Process Bypass -Force
#   & "E:\file\AirCOM\tools\com0com\diagnose-com0com.ps1"
# 它会把诊断结果写到 E:\file\AirCOM\tools\com0com\diagnosis.txt

#Requires -RunAsAdministrator

$Out = "E:\file\AirCOM\tools\com0com\diagnosis.txt"
"=== AirCOM com0com 诊断报告 ===" | Out-File -FilePath $Out -Encoding utf8
"生成时间(本机): " + (Get-Date) | Out-File -FilePath $Out -Encoding utf8 -Append
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 1. 测试签名
"--- 1. 测试签名模式 ---" | Out-File -FilePath $Out -Encoding utf8 -Append
$ts = bcdedit /enum "{current}" 2>&1 | Select-String -Pattern "testsigning" -SimpleMatch
if ($ts) { ("bcdedit testsigning: " + $ts.ToString().Trim()) | Out-File -FilePath $Out -Encoding utf8 -Append }
else { "bcdedit testsigning: 未找到该字段(可能未开启)" | Out-File -FilePath $Out -Encoding utf8 -Append }
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 2. com0com 安装位置
"--- 2. com0com 安装位置 ---" | Out-File -FilePath $Out -Encoding utf8 -Append
$paths = @("C:\Program Files\com0com\setupc.exe", "C:\Program Files (x86)\com0com\setupc.exe")
foreach ($p in $paths) {
    ("  {0} => {1}" -f $p, (Test-Path $p)) | Out-File -FilePath $Out -Encoding utf8 -Append
}
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 3. com0com 驱动文件
"--- 3. com0com 驱动文件 ---" | Out-File -FilePath $Out -Encoding utf8 -Append
$drv = Get-ChildItem "C:\Windows\System32\drivers\com0com*" -ErrorAction SilentlyContinue
if ($drv) { $drv | ForEach-Object { ("  " + $_.Name + "  " + $_.Length + " bytes") | Out-File -FilePath $Out -Encoding utf8 -Append } }
else { "  未找到 com0com*.sys 驱动文件" | Out-File -FilePath $Out -Encoding utf8 -Append }
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 4. com0com 服务
"--- 4. com0com 服务 ---" | Out-File -FilePath $Out -Encoding utf8 -Append
$svc = Get-Service com0com -ErrorAction SilentlyContinue
if ($svc) { $svc | Format-List Name, Status, StartType | Out-String | Out-File -FilePath $Out -Encoding utf8 -Append }
else { "  com0com 服务未注册" | Out-File -FilePath $Out -Encoding utf8 -Append }
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 5. setupc list 输出
"--- 5. setupc list 输出 ---" | Out-File -FilePath $Out -Encoding utf8 -Append
$setupcPath = $null
if (Test-Path "C:\Program Files (x86)\com0com\setupc.exe") { $setupcPath = "C:\Program Files (x86)\com0com\setupc.exe" }
elseif (Test-Path "C:\Program Files\com0com\setupc.exe") { $setupcPath = "C:\Program Files\com0com\setupc.exe" }
if ($setupcPath) {
    "  使用 setupc: $setupcPath" | Out-File -FilePath $Out -Encoding utf8 -Append
    try {
        $listOut = & $setupcPath list 2>&1 | Out-String
        "  --- setupc list 输出开始 ---" | Out-File -FilePath $Out -Encoding utf8 -Append
        $listOut | Out-File -FilePath $Out -Encoding utf8 -Append
        "  --- setupc list 输出结束 ---" | Out-File -FilePath $Out -Encoding utf8 -Append
    } catch {
        "  setupc 运行出错: $_" | Out-File -FilePath $Out -Encoding utf8 -Append
    }
} else {
    "  未找到 setupc.exe" | Out-File -FilePath $Out -Encoding utf8 -Append
}
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 6. com0com 设备枚举
"--- 6. com0com 设备枚举 (Win32_PnPEntity) ---" | Out-File -FilePath $Out -Encoding utf8 -Append
$devs = Get-WmiObject Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "com0com|CNCA|CNCB" }
if ($devs) { $devs | Select-Object Name, DeviceID | Format-Table -AutoSize | Out-String | Out-File -FilePath $Out -Encoding utf8 -Append }
else { "  未找到 com0com 相关设备" | Out-File -FilePath $Out -Encoding utf8 -Append }
"" | Out-File -FilePath $Out -Encoding utf8 -Append

# 7. 当前所有 COM 端口
"--- 7. 当前 COM 端口列表 ---" | Out-File -FilePath $Out -Encoding utf8 -Append
[System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object | ForEach-Object { "  $_" } | Out-File -FilePath $Out -Encoding utf8 -Append

"" | Out-File -FilePath $Out -Encoding utf8 -Append
"=== 诊断结束 ===" | Out-File -FilePath $Out -Encoding utf8 -Append

Write-Host "诊断完成，结果已写入: $Out" -ForegroundColor Green
Write-Host "请把该文件路径告知 Claude。" -ForegroundColor Cyan
