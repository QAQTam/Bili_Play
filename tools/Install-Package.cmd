@echo off
setlocal
set "BILIPLAY_INSTALL_SCRIPT=%~f0"
set "BILIPLAY_INSTALL_VALIDATE_ONLY="
if /i "%~1"=="/check" set "BILIPLAY_INSTALL_VALIDATE_ONLY=1"
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -Command "$text = [IO.File]::ReadAllText($env:BILIPLAY_INSTALL_SCRIPT); & ([scriptblock]::Create(($text -split '(?m)^# POWERSHELL_PAYLOAD\r?$', 2)[1]))"
set "BILIPLAY_INSTALL_RESULT=%errorlevel%"
if not "%BILIPLAY_INSTALL_RESULT%"=="0" if not defined BILIPLAY_INSTALL_VALIDATE_ONLY pause
exit /b %BILIPLAY_INSTALL_RESULT%
# POWERSHELL_PAYLOAD
$ErrorActionPreference = 'Stop'
function Get-FileDigest([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
function Test-CertificateTrusted([string]$thumbprint) {
    $store = [Security.Cryptography.X509Certificates.X509Store]::new('TrustedPeople', 'LocalMachine')
    try {
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        return $store.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $thumbprint, $false).Count -gt 0
    } finally { $store.Dispose() }
}
function Confirm-Installation {
    Add-Type -AssemblyName System.Windows.Forms
    $notice = @'
__INSTALLATION_NOTICE__
'@
    return [System.Windows.Forms.MessageBox]::Show(
        $notice, 'Bili Play __VERSION__ - 安装前请阅读',
        [System.Windows.Forms.MessageBoxButtons]::OKCancel,
        [System.Windows.Forms.MessageBoxIcon]::Warning,
        [System.Windows.Forms.MessageBoxDefaultButton]::Button2
    ) -eq [System.Windows.Forms.DialogResult]::OK
}
try {
    $directory = [IO.Path]::GetDirectoryName($env:BILIPLAY_INSTALL_SCRIPT)
    $certificatePath = Join-Path $directory 'BiliPlay.cer'
    $packagePath = Join-Path $directory '__PACKAGE_NAME__'
    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw 'Keep this script, BiliPlay.cer and the MSIX in the same extracted folder.'
    }
    if ((Get-FileDigest $certificatePath) -cne '__CERT_SHA256__' -or
        (Get-FileDigest $packagePath) -cne '__PACKAGE_SHA256__') {
        throw 'The certificate or package has changed. Download the complete original release again.'
    }
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
    try {
        if ($certificate.HasPrivateKey -or $certificate.Subject -cne '__PUBLISHER__' -or
            $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) {
            throw 'The public signing certificate is invalid or expired.'
        }
        $thumbprint = $certificate.Thumbprint
    } finally { $certificate.Dispose() }
    if ($env:BILIPLAY_INSTALL_VALIDATE_ONLY -eq '1') {
        Write-Host 'PASS installer preflight. No certificate imported; no installation started.'
        exit 0
    }
    if (-not (Confirm-Installation)) {
        Write-Host '已取消。未导入证书，也未启动安装。'
        exit 0
    }
    if (-not (Test-CertificateTrusted $thumbprint)) {
        Write-Host 'Approve the Windows administrator prompt to trust the Bili Play signing certificate.'
        $quotedPath = $certificatePath.Replace("'", "''")
        $elevatedScript = @"
`$ErrorActionPreference = 'Stop'
function Get-FileDigest { ${function:Get-FileDigest} }
try {
    `$path = '$quotedPath'
    if ((Get-FileDigest `$path) -cne '__CERT_SHA256__') { throw 'Certificate changed.' }
    `$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(`$path)
    `$store = [Security.Cryptography.X509Certificates.X509Store]::new('TrustedPeople', 'LocalMachine')
    try {
        `$store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        `$store.Add(`$certificate)
    } finally { `$store.Dispose(); `$certificate.Dispose() }
    exit 0
} catch { Write-Error `$_; exit 1 }
"@
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($elevatedScript))
        $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
        try { if ($process.ExitCode -ne 0) { throw 'The certificate could not be imported.' } }
        finally { $process.Dispose() }
    }
    if (-not (Test-CertificateTrusted $thumbprint)) { throw 'Certificate trust could not be confirmed.' }
    if ((Get-FileDigest $packagePath) -cne '__PACKAGE_SHA256__') { throw 'Package changed.' }
    Start-Process -FilePath $packagePath
    Write-Host 'Windows App Installer is open. Choose Install to continue.'
    exit 0
} catch {
    Write-Host ('Installation stopped: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
