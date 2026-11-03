param([switch]$IncludePublishOutput)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($marker in @('BiliPlay.csproj', 'Core/BiliPlay.Core.csproj', 'Tests/BiliPlay.Tests.csproj')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskRoot $marker) -PathType Leaf)) {
        throw '未找到完整项目标记，停止清理。'
    }
}
$relativePaths = @('artifacts','bin','obj','Core/bin','Core/obj','Tests/bin','Tests/obj','Tests/Windows/bin','Tests/Windows/obj','TestResults')
if ($IncludePublishOutput) { $relativePaths += @('dist','publish','AppPackages','BundleArtifacts') }
$targets = @()
$files = 0
$bytes = 0L
# Validate every target before deleting anything. Never follow a link or leave the project.
foreach ($relative in $relativePaths) {
    $full = [IO.Path]::GetFullPath((Join-Path $taskRoot $relative))
    if (-not $full.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw '生成目录超出项目范围，停止清理。'
    }
    $ancestor = $full
    while ($ancestor -and $ancestor.StartsWith($taskRoot, [StringComparison]::OrdinalIgnoreCase)) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw '生成目录或其父目录包含链接，停止清理。'
        }
        if ($ancestor -eq $taskRoot) { break }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    if (-not (Test-Path -LiteralPath $full)) { continue }
    if (-not (Get-Item -LiteralPath $full -Force).PSIsContainer) { throw '生成目录位置存在普通文件，停止清理。' }
    $items = @(Get-Item -LiteralPath $full -Force) + @(Get-ChildItem -LiteralPath $full -Recurse -Force)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        throw '生成目录包含链接，停止清理。'
    }
    $targets += $full
    foreach ($item in $items | Where-Object { -not $_.PSIsContainer }) { $files++; $bytes += $item.Length }
}
if (Get-Command Get-AppxPackage -ErrorAction SilentlyContinue) {
    foreach ($package in @(Get-AppxPackage -Name BiliPlay.Desktop -ErrorAction Stop)) {
        foreach ($target in $targets) {
            if ($package.InstallLocation -eq $target -or
                $package.InstallLocation.StartsWith($target + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
                $target.StartsWith($package.InstallLocation.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw '待清理目录仍被已安装应用使用，停止清理。'
            }
        }
    }
}
foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
    try { $executable = $process.Path } catch { continue }
    if (-not $executable) { continue }
    foreach ($target in $targets) {
        if ($executable.StartsWith($target + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw ('生成目录中仍有程序运行，停止清理。进程 ID：' + $process.Id)
        }
    }
}
foreach ($target in $targets) { Remove-Item -LiteralPath $target -Recurse -Force }
Write-Output ('CLEAN generated directories=' + $targets.Count + ', files=' + $files + ', bytes=' + $bytes)
