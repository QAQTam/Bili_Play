# Update only the user-run launcher; leave the MSIX and public certificate unchanged.
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output -eq $root -or $output.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '安装器交付目录必须在工作区之外。' }
$ancestor = $output
while ($ancestor) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '交付路径不能包含链接。' }
    $ancestor = [IO.Path]::GetDirectoryName($ancestor)
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $root 'Package.appxmanifest') -Raw
$version = ([Version]$manifest.Package.Identity.Version).ToString(3)
$packageName = 'BiliPlay_' + $version + '_x64.msix'
$package = Join-Path $output $packageName
$publicCert = Join-Path $output 'BiliPlay.cer'
$launcher = Join-Path $output '一键安装.cmd'
$legacyInstructions = Join-Path $output '安装说明.txt'
$allowed = @($packageName, 'BiliPlay.cer', 'LICENSE.txt', '用户协议.txt', '一键安装.cmd', '安装说明.txt')
foreach ($item in Get-ChildItem -LiteralPath $output -Force) {
    if ($item.PSIsContainer -or $item.Name -notin $allowed -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '交付目录包含其他文件或链接，停止更新。' }
}
foreach ($name in @($packageName, 'BiliPlay.cer', 'LICENSE.txt', '用户协议.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $output $name) -PathType Leaf)) { throw ('缺少交付文件：' + $name) }
}
$notice = @'
安装说明

请先将整个分发文件夹解压，再运行本脚本。
1. 确认允许后，脚本会请求管理员权限，将随包公开签名证书导入本机的“受信任的人”。此操作会影响本机用户对该签名证书的信任。
2. 随后打开 Windows 安装界面，请点击“安装”继续；安装完成后从开始菜单打开 Bili Play，使用自己的哔哩哔哩账号扫码登录。
3. 从旧 BiliPlay.Desktop 版本切换时，由于包身份不同，本版本会作为另一个应用安装，无法覆盖旧版。脚本不会卸载旧版或迁移、删除账号数据。

用户协议与隐私政策见同目录“用户协议.txt”，使用前请自行查阅；软件首次进入时也会提示。

MIT 许可提醒
本软件采用 MIT License，完整条款见同目录 LICENSE.txt。软件按“现状”提供，不提供任何明示或默示担保；作者及版权所有者不对软件使用导致的索赔、损害或其他责任负责，具体以许可证原文及适用法律为准。复制、修改或分发软件时，应保留版权声明和许可声明。

是否已阅读以上说明，并允许导入签名证书、打开安装界面？
选择“确定”表示允许继续；选择“取消”或关闭窗口将退出，且不会执行安装操作。
'@
$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Install-Package.cmd') -Raw
$installer = $template.Replace('__PACKAGE_NAME__', $packageName).Replace('__CERT_SHA256__', (Get-FileHash -LiteralPath $publicCert -Algorithm SHA256).Hash).Replace('__PACKAGE_SHA256__', (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash).Replace('__PUBLISHER__', [string]$manifest.Package.Identity.Publisher).Replace('__VERSION__', $version).Replace('__INSTALLATION_NOTICE__', $notice)
if ($installer -match '__[A-Z_]+__') { throw '安装脚本仍有未替换参数。' }
# CMD reads only the ASCII bootstrap. Its PowerShell payload is explicitly read as UTF-8.
[IO.File]::WriteAllText($launcher, ($installer -replace '\r?\n', "`r`n"), [Text.UTF8Encoding]::new($false))
& $launcher /check
if ($LASTEXITCODE -ne 0) { throw '一键安装脚本的无安装预检失败。' }
if (Test-Path -LiteralPath $legacyInstructions -PathType Leaf) { Remove-Item -LiteralPath $legacyInstructions -Force }
Write-Output ('PASS five-file delivery and consent-first install launcher: ' + $output)
