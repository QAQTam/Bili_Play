param([switch]$Visual, [switch]$NativeAot)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $taskRoot
try {
    & dotnet run --project Tests/BiliPlay.Tests.csproj -c Release -p:DevelopmentBuild=true
    if ($LASTEXITCODE -ne 0) { throw '核心测试失败。' }
    & dotnet run --project Tests/Windows/BiliPlay.Windows.Tests.csproj -c Release -p:DevelopmentBuild=true
    if ($LASTEXITCODE -ne 0) { throw '开发环境隔离测试失败。' }
    if ($NativeAot) {
        $aotTestsOutput = Join-Path $taskRoot 'artifacts/aot-tests'
        & dotnet publish Tests/BiliPlay.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -p:IlcInstructionSet=base -p:DebugType=None -p:DebugSymbols=false -o $aotTestsOutput
        if ($LASTEXITCODE -ne 0) { throw 'Native AOT 核心测试编译失败。' }
        & (Join-Path $aotTestsOutput 'BiliPlay.Tests.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Native AOT 核心测试运行失败。' }
        Write-Output 'PASS Native AOT core test suite (zero-JIT verification)'
        $aotOutput = Join-Path $taskRoot 'artifacts/native-resolver'
        & dotnet publish Core/BiliPlay.Core.csproj -c Release -r win-x64 -p:NativeResolverExe=true -p:DebugType=None -p:DebugSymbols=false -o $aotOutput
        if ($LASTEXITCODE -ne 0) { throw 'Native AOT 音乐解析器构建失败。' }
        $resolverExe = Get-Item -LiteralPath (Join-Path $aotOutput 'BiliPlay.Resolver.exe')
        if ($resolverExe.Length -gt 10MB) { throw ('Native AOT 解析器体积超出预期：' + $resolverExe.Length) }
        $aotProbe = '{"op":"quit"}' | & $resolverExe.FullName --stdio
        if ($LASTEXITCODE -ne 0 -or $aotProbe -notmatch '"Ok":true') { throw 'Native AOT 音乐解析器自检失败。' }
        Write-Output ('PASS Native AOT standalone music resolver build (' + [Math]::Round($resolverExe.Length / 1MB, 2) + ' MB) and stdio smoke check')
    }
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
            Write-Output ("PASS native WinUI visual regression (Tray WorkingSet: " + $result.TrayWorkingSetBeforeMB + " MB -> " + $result.TrayWorkingSetAfterMB + " MB)")
        } finally { $process.Dispose() }
    }
} finally {
    try { & (Join-Path $PSScriptRoot 'Clean-Generated.ps1') }
    finally { Pop-Location }
}
