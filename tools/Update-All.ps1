# Build and verify both profiles, deliver the installer, and clean generated output.
# Installation and package registration are performed only by the user.
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
try {
    & (Join-Path $PSScriptRoot 'Build-Release.ps1')
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') -OutputDirectory $OutputDirectory
} finally {
    & (Join-Path $PSScriptRoot 'Clean-Generated.ps1') -IncludePublishOutput
}
