#Requires -Version 5.1
<#
    GitHub 访问优化（需要管理员权限）
    ------------------------------------------------------------
    为什么需要它：GitHub 网页在国内经常"半通不通"——DNS 正常、443 能连，
    但 TLS 握手后随机卡死，表现为登录/注册时"请求失败，请重试"。

    这个脚本做的事：
      1. 逐个测 GitHub 节点（强制指定 IP，绕过 DNS 轮询）
      2. 挑出响应最快的一个
      3. 备份 hosts，把这几个域名钉到该 IP
      4. 清 DNS 缓存并复测

    不装软件、不改系统设置，随时 -Restore 还原。
    注意：它只能提高"连上的概率"，不能把限速变没——真要稳定还是得用代理。

    用法（右键 → 以管理员身份运行）：
        powershell -ExecutionPolicy Bypass -File github-hosts.ps1
        powershell -ExecutionPolicy Bypass -File github-hosts.ps1 -Restore
#>
param(
    [switch]$Restore
)

$hosts    = Join-Path $env:WINDIR 'System32\drivers\etc\hosts'
$backup   = Join-Path $env:WINDIR 'System32\drivers\etc\hosts.warmspin.bak'
$startTag = '# warmspin-start'
$endTag   = '# warmspin-end'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    Write-Host '需要管理员权限，正在请求提权…' -ForegroundColor Yellow
    $argLine = '-NoProfile -ExecutionPolicy Bypass -NoExit -File "{0}"' -f $PSCommandPath
    if ($Restore) { $argLine += ' -Restore' }
    Start-Process powershell.exe -ArgumentList $argLine -Verb RunAs
    exit
}

function Remove-WarmspinBlock {
    param([string[]]$Lines)
    $out = New-Object System.Collections.ArrayList
    $skip = $false
    foreach ($l in $Lines) {
        if ($l -match [regex]::Escape($startTag)) { $skip = $true; continue }
        if ($l -match [regex]::Escape($endTag)) { $skip = $false; continue }
        if (-not $skip) { [void]$out.Add($l) }
    }
    return $out
}

# ---------------- 还原 ----------------
if ($Restore) {
    if (Test-Path $backup) {
        Copy-Item $backup $hosts -Force
        Remove-Item $backup -Force
        Write-Host '已用备份还原 hosts。' -ForegroundColor Green
    } else {
        $lines = Remove-WarmspinBlock (Get-Content $hosts)
        Set-Content -Path $hosts -Value ($lines -join "`r`n") -Encoding ASCII
        Write-Host '已删除脚本写入的条目。' -ForegroundColor Green
    }
    ipconfig /flushdns | Out-Null
    Write-Host 'DNS 缓存已清空。'
    Read-Host '按回车退出'
    exit
}

# ---------------- 测速 ----------------
function Measure-Node {
    param([string]$Ip, [int]$Seconds = 8)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $raw = & curl.exe -s -o NUL -w "%{http_code} %{size_download}" --max-time $Seconds `
        --resolve ("github.com:443:" + $Ip) 'https://github.com/' 2>&1
    $sw.Stop()
    $txt = ($raw | Out-String).Trim()
    $parts = $txt -split '\s+'
    $code  = if ($parts.Count -ge 1) { $parts[0] } else { '000' }
    $bytes = if ($parts.Count -ge 2) { [double]$parts[1] } else { 0 }
    $secs  = if ($sw.Elapsed.TotalSeconds -gt 0) { $sw.Elapsed.TotalSeconds } else { 1 }
    [pscustomobject]@{
        Ip   = $Ip
        Code = $code
        KB   = [math]::Round($bytes / 1KB, 0)
        KBps = [math]::Round($bytes / 1KB / $secs, 1)
    }
}

Write-Host ''
Write-Host '正在测试 GitHub 节点（每个 8 秒，共约 40 秒）…' -ForegroundColor Cyan

$candidates = @('20.27.177.113', '20.205.243.166', '20.200.245.247', '140.82.112.3', '140.82.121.4')
$results = @()
foreach ($ip in $candidates) {
    Write-Host ('  ' + $ip.PadRight(18)) -NoNewline
    $r = Measure-Node -Ip $ip
    $results += $r
    $color = if ($r.Code -eq '200') { 'Green' } else { 'DarkGray' }
    Write-Host ('HTTP ' + $r.Code + '   ' + $r.KB + ' KB   ' + $r.KBps + ' KB/s') -ForegroundColor $color
}

$ok = @($results | Where-Object { $_.Code -eq '200' } | Sort-Object -Property KBps -Descending)
if ($ok.Count -eq 0) {
    Write-Host ''
    Write-Host '所有节点都连不上。这会儿到 GitHub 是被彻底阻断了，hosts 改不了这个。' -ForegroundColor Red
    Write-Host '建议：换手机热点试试，或者用代理。' -ForegroundColor Red
    Read-Host '按回车退出'
    exit
}

$best = $ok[0]
Write-Host ''
Write-Host ('最快节点：' + $best.Ip + '   ' + $best.KBps + ' KB/s') -ForegroundColor Green

# ---------------- 写 hosts ----------------
if (-not (Test-Path $backup)) { Copy-Item $hosts $backup -Force }
$lines = Remove-WarmspinBlock (Get-Content $hosts)
$block = @(
    $startTag,
    ($best.Ip + ' github.com'),
    ($best.Ip + ' api.github.com'),
    ($best.Ip + ' codeload.github.com'),
    ($best.Ip + ' gist.github.com'),
    '185.199.108.133 raw.githubusercontent.com',
    '185.199.108.133 objects.githubusercontent.com',
    '185.199.108.133 avatars.githubusercontent.com',
    $endTag
)
Set-Content -Path $hosts -Value (($lines -join "`r`n") + "`r`n" + ($block -join "`r`n")) -Encoding ASCII
ipconfig /flushdns | Out-Null

Write-Host ''
Write-Host '已写入 hosts 并清空 DNS 缓存。' -ForegroundColor Green
Write-Host ''
Write-Host '正在复测（走 DNS，不再强制 IP）…' -ForegroundColor Cyan
$check = & curl.exe -s -o NUL -w "%{http_code} %{time_total}" --max-time 15 https://github.com/ 2>&1
Write-Host ('  github.com        ->  ' + (($check | Out-String).Trim())) -ForegroundColor Green
$check2 = & curl.exe -s -o NUL -w "%{http_code} %{time_total}" --max-time 15 https://github.com/signup 2>&1
Write-Host ('  github.com/signup ->  ' + (($check2 | Out-String).Trim())) -ForegroundColor Green

Write-Host ''
Write-Host '现在可以回浏览器刷新、重新输入验证码了。' -ForegroundColor Cyan
Write-Host '如果注册还是失败，说明线路抖动太厉害，换手机热点注册最省事。' -ForegroundColor Yellow
Write-Host ''
Write-Host '想还原：' -ForegroundColor DarkGray
Write-Host ('  powershell -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Restore') -ForegroundColor DarkGray
Read-Host '按回车退出'
