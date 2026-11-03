# Run the isolated, unpackaged development build without changing Start menu registration.
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'BiliPlay.csproj'
& winapp run $projectPath --configuration Release --arch x64 -p WindowsPackageType=None -p DevelopmentBuild=true --detach
exit $LASTEXITCODE
