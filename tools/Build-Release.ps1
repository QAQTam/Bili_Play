param([switch]$Production)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$portable = Join-Path $taskRoot $(if ($Production) { 'dist/Bili_Play-Release-win-x64' } else { 'dist/Bili_Play-win-x64' })
function Clear-ReleaseFolder([string]$path) {
    $full=[IO.Path]::GetFullPath($path)
    if($full -ne $portable -or -not $full.StartsWith((Join-Path $taskRoot 'dist')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '不安全的发布输出路径。' }
    if(Test-Path -LiteralPath $full) {
        $items=@((Get-Item -LiteralPath $full -Force)) + @(Get-ChildItem -LiteralPath $full -Recurse -Force)
        if(@($items|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count -gt 0) { throw '发布目录包含链接，停止清理。' }
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}
Push-Location $taskRoot
try {
    $developmentBuild = (-not $Production).ToString().ToLowerInvariant()
    $running=@(Get-Process Bili_Play -ErrorAction SilentlyContinue | Where-Object {$_.Path -and $_.Path.StartsWith((Join-Path $taskRoot 'dist')+'\',[StringComparison]::OrdinalIgnoreCase)})
    if($running.Count -gt 0) {throw '请先关闭 Bili Play，再生成发布文件。'}
    Clear-ReleaseFolder $portable
    & dotnet restore BiliPlay.csproj --locked-mode -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw '依赖还原失败。' }
    & dotnet publish BiliPlay.csproj -c Release -p:Platform=x64 -p:WindowsPackageType=None "-p:DevelopmentBuild=$developmentBuild" -p:EnableVisualChecks=false -p:DebugType=None -p:DebugSymbols=false -r win-x64 --self-contained false -o $portable --no-restore
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
    Write-Output $portable
} finally { Pop-Location }
