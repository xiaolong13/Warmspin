# 构建脚本：把 src 下的 C# + XAML 编译成单个 exe
# 依赖：Windows 自带的 .NET Framework 4.x（系统自带 csc.exe），不需要装 SDK
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File build.ps1
#   powershell -ExecutionPolicy Bypass -File build.ps1 -NoManifest   # 开发调试用（不要求管理员）
param(
    [string]$OutDir = "$PSScriptRoot\dist",
    [switch]$DebugBuild,
    [switch]$NoManifest
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$srcDir = Join-Path $root 'src'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw '找不到 csc.exe，需要 .NET Framework 4.x' }

# WPF 不在 csc.rsp 的默认引用里，需要手动加 GAC 路径
function Find-GacAssembly([string]$name, [string]$token) {
    $dirs = @(
        'C:\Windows\Microsoft.Net\assembly\GAC_MSIL',
        'C:\Windows\Microsoft.Net\assembly\GAC_64',
        'C:\Windows\Microsoft.Net\assembly\GAC_32'
    )
    foreach ($d in $dirs) {
        $p = Join-Path $d "$name\v4.0_4.0.0.0__$token\$name.dll"
        if (Test-Path $p) { return $p }
    }
    return $null
}

$refs = @(
    (Find-GacAssembly 'PresentationFramework' '31bf3856ad364e35'),
    (Find-GacAssembly 'PresentationCore' '31bf3856ad364e35'),
    (Find-GacAssembly 'WindowsBase' '31bf3856ad364e35'),
    (Find-GacAssembly 'System.Xaml' 'b77a5c561934e089'),
    'System.Core.dll',
    'Microsoft.CSharp.dll'
)
foreach ($r in $refs) {
    if (-not $r) { throw '缺少 WPF 程序集引用' }
}

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$exe = Join-Path $OutDir 'Warmspin.exe'
if (Test-Path $exe) { Remove-Item $exe -Force }

$sources = @(Get-ChildItem -Path (Join-Path $srcDir '*.cs') -File | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'src 下没有 .cs 文件' }

$cscArgs = @('/nologo', '/target:winexe', "/out:$exe", '/optimize+', '/platform:anycpu', '/utf8output')
if ($DebugBuild) { $cscArgs += '/debug+' } else { $cscArgs += '/debug-' }
foreach ($r in $refs) { $cscArgs += "/r:$r" }
$cscArgs += '/resource:' + (Join-Path $srcDir 'Ui.xaml') + ',Ui.xaml'

$icon = Join-Path $root 'assets\icon.ico'
if (Test-Path $icon) { $cscArgs += "/win32icon:$icon" }

$manifest = Join-Path $srcDir 'app.manifest'
if ((Test-Path $manifest) -and -not $NoManifest) { $cscArgs += "/win32manifest:$manifest" }

$cscArgs += $sources

Write-Host '编译中…' -ForegroundColor Cyan
$output = & $csc @cscArgs 2>&1
if (-not (Test-Path $exe)) {
    Write-Host '编译失败：' -ForegroundColor Red
    $output | ForEach-Object { Write-Host $_ }
    exit 1
}
$output | Where-Object { $_ -match 'warning' } | ForEach-Object { Write-Host $_ -ForegroundColor DarkYellow }

$size = [math]::Round((Get-Item $exe).Length / 1KB, 0)
Write-Host "编译完成：$exe  ($size KB)" -ForegroundColor Green
