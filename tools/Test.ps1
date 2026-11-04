param([switch]$Visual)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $taskRoot
try {
    & dotnet run --project Tests/BiliPlay.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw '核心测试失败。' }
    & dotnet run --project Tests/Windows/BiliPlay.Windows.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Windows 原生测试失败。' }
    & dotnet run --project Tests/Windows/BiliPlay.Windows.Tests.csproj -c Release -p:DevelopmentBuild=true
    if ($LASTEXITCODE -ne 0) { throw '开发环境隔离测试失败。' }
    if ($Visual) {
        $output = Join-Path $taskRoot 'artifacts/visual-app'
        $resultDirectory = Join-Path $taskRoot 'artifacts/visual-check'
        $resultFile = Join-Path $resultDirectory 'result.json'
        & dotnet publish BiliPlay.csproj -c Release -p:Platform=x64 -p:WindowsPackageType=None -p:DevelopmentBuild=true -p:EnableVisualChecks=true -r win-x64 --self-contained false -o $output
        if ($LASTEXITCODE -ne 0) { throw '界面测试版本构建失败。' }
        New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
        if (Test-Path -LiteralPath $resultFile) { Remove-Item -LiteralPath $resultFile }
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $output 'Bili_Play.exe'))
        $start.UseShellExecute = $false
        $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        $start.ArgumentList.Add('--visual-check')
        $start.ArgumentList.Add($resultDirectory)
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(60000)) {
                $process.Kill()
                $process.WaitForExit(5000) | Out-Null
                throw '界面测试超时。'
            }
            if (-not (Test-Path -LiteralPath $resultFile)) { throw '没有界面测试结果，请先关闭工作区中运行的应用。' }
            $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
            if (-not $result.Success) { throw $result.Error }
            if ($process.ExitCode -ne 0) { throw "界面测试进程异常退出：$($process.ExitCode)" }
            Write-Output 'PASS native WinUI visual regression'
        } finally { $process.Dispose() }
    }
} finally {
    try { & (Join-Path $PSScriptRoot 'Clean-Generated.ps1') }
    finally { Pop-Location }
}
