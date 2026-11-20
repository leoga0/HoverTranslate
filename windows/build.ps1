# HoverTranslate 构建脚本
# 依赖：Windows 自带的 .NET Framework 4.x 编译器，无需安装任何第三方工具

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 csc.exe，需要 .NET Framework 4.x' }

$fw = Split-Path -Parent $csc
$wpf = Join-Path $fw 'WPF'
if (-not (Test-Path $wpf)) { throw '找不到 WPF 目录，需要 .NET Framework 4.x 完整版' }

# 编译前先结束正在运行的旧版本，避免输出文件被占用
Get-Process HoverTranslate -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill() } catch { }
}
Start-Sleep -Milliseconds 400

$refs = @(
    'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Data.dll',
    'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll', 'Accessibility.dll',
    'PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll', 'System.Xaml.dll',
    'UIAutomationClient.dll', 'UIAutomationTypes.dll'
)

$refArgs = @()
foreach ($r in $refs) { $refArgs += "/r:$r" }

$sources = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
if (-not $sources) { throw 'src 目录下没有 .cs 文件' }

$out = Join-Path $dist 'HoverTranslate.exe'
$ico = Join-Path $root 'assets\app.ico'

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    '/utf8output',
    "/lib:$fw",
    "/lib:$wpf",
    "/out:$out"
) + $refArgs

if (Test-Path $ico) { $cscArgs += "/win32icon:$ico" }
$cscArgs += $sources

Write-Host '编译中 ...' -ForegroundColor Cyan
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

$fi = Get-Item $out
Write-Host ('编译完成: {0}  ({1:N0} KB)' -f $fi.FullName, ($fi.Length / 1KB)) -ForegroundColor Green
