# Verify resources through the installed-package lookup path, not adjacent PRI files.
param(
    [Parameter(Mandatory)][string]$PriPath,
    [Parameter(Mandatory)][string]$PackageName
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'BiliPlay.csproj') -Raw
$buildTools = @($project.Project.ItemGroup.PackageReference | Where-Object { $_.Include -eq 'Microsoft.Windows.SDK.BuildTools' })[0].Version
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
$tools = Join-Path $nuget ('microsoft.windows.sdk.buildtools/' + $buildTools + '/bin')
$makepri = Get-ChildItem -Path (Join-Path $tools '*/x64/makepri.exe') -File | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makepri) { throw '缺少 Windows SDK makepri，无法验证安装包资源。' }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('BiliPlay-pri-' + [Guid]::NewGuid().ToString('N') + '.xml')
try {
    & $makepri.FullName dump /if ([IO.Path]::GetFullPath($PriPath)) /of $temporary /o *> $null
    if ($LASTEXITCODE -ne 0) { throw '安装包资源索引无法解析。' }
    [xml]$index = Get-Content -LiteralPath $temporary -Raw
    $uris = @($index.SelectNodes('//NamedResource') | ForEach-Object { $_.GetAttribute('uri') })
    foreach ($resource in @('App.xbf','MainWindow.xbf','MainPage.xbf','NowPlayingIndicator.xbf','Microsoft.UI.Xaml/Themes/themeresources.xbf')) {
        $expected = 'ms-resource://' + $PackageName + '/Files/' + $resource
        if ($expected -notin $uris) { throw ('安装包主资源索引缺少：' + $resource + '。拒绝交付可能启动崩溃的安装包。') }
    }
    Write-Output 'PASS packaged XAML and bundled WinUI theme resources in the main PRI'
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
