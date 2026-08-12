# AirCOM - 一键发布脚本
# 用法：
#   .\publish.ps1              # 用 Directory.Build.props 里的版本号打包
#   .\publish.ps1 -BumpMinor   # 次版本号 +1 后打包
#   .\publish.ps1 -BumpPatch   # 修订号 +1 后打包
#
# 产物：publish/v<版本>/AirCOM-v<版本>.zip + 解压目录
# 历史版本各自独立目录，互不覆盖。

param(
    [switch]$BumpMinor,
    [switch]$BumpPatch
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 1. 解析/递增版本号
$propsPath = Join-Path $root "Directory.Build.props"
$props = Get-Content $propsPath -Raw
if ($props -notmatch '<VersionPrefix>(\d+)\.(\d+)\.(\d+)</VersionPrefix>') {
    Write-Error "无法从 Directory.Build.props 解析版本号"
    exit 1
}
$major = [int]$Matches[1]; $minor = [int]$Matches[2]; $patch = [int]$Matches[3]
if ($BumpMinor) { $minor++; $patch = 0 }
elseif ($BumpPatch) { $patch++ }
$newVer = "$major.$minor.$patch"

if ($BumpMinor -or $BumpPatch) {
    $props = $props -replace '<VersionPrefix>\d+\.\d+\.\d+</VersionPrefix>', "<VersionPrefix>$newVer</VersionPrefix>"
    Set-Content -Path $propsPath -Value $props -NoNewline
    Write-Host "版本号已更新为 $newVer" -ForegroundColor Cyan
} else {
    Write-Host "当前版本号: $newVer" -ForegroundColor Cyan
}

# 2. 构建 + 发布
Write-Host "`n>>> 构建 + 发布 AirCOM.App..." -ForegroundColor Cyan
dotnet publish src/AirCOM.App/AirCOM.App.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false `
    -o "publish/_staging" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "发布失败"; exit 1 }

# 3. 清理 staging（删 pdb），拷入 com0com 安装包
Remove-Item "publish/_staging/*.pdb" -Force -ErrorAction SilentlyContinue
$installer = Join-Path $root "tools\com0com\com0com-setup-x64.exe"
if (Test-Path $installer) {
    Copy-Item $installer "publish/_staging/" -Force
}

# 4. 归档到 publish/v<版本>/
$verDir = Join-Path $root "publish\v$newVer"
if (Test-Path $verDir) { Remove-Item $verDir -Recurse -Force }
New-Item -ItemType Directory -Path $verDir | Out-Null

# 移动发布产物到版本目录
Move-Item "publish/_staging/*" $verDir -Force
Remove-Item "publish/_staging" -Force -ErrorAction SilentlyContinue

# 打 zip
$zipPath = Join-Path $verDir "AirCOM-v$newVer.zip"
Compress-Archive -Path (Join-Path $verDir '*') -DestinationPath $zipPath -Force

# 5. 完成
Write-Host "`n========== 发布完成 ==========" -ForegroundColor Green
Write-Host "版本: v$newVer"
Write-Host "目录: $verDir"
Write-Host "压缩包: AirCOM-v$newVer.zip"
$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host ("压缩包大小: {0:N1} MB" -f $zipSize)
