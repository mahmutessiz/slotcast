[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '1.0.3',
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if (-not $InnoCompiler) {
    $compilerCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($compilerCommand) { $InnoCompiler = $compilerCommand.Source }
    else {
        $compilerCandidates = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )
        $InnoCompiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Install Inno Setup 6, or pass -InnoCompiler with the path to ISCC.exe.'
}

# A fresh staging folder prevents old files or local credentials entering a release.
$buildStamp = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$publishDirectory = Join-Path $projectRoot "artifacts\publish\win-x64\$Version-$buildStamp"
$installerDirectory = Join-Path $projectRoot 'artifacts\installer'
$project = Join-Path $projectRoot 'SlotCast.csproj'

& dotnet publish $project -p:PublishProfile=WindowsSelfContained "-p:Version=$Version" `
    -p:UseSharedCompilation=false --disable-build-servers -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }

$publishedConfig = Join-Path $publishDirectory 'appsettings.json'
$configuration = Get-Content -LiteralPath $publishedConfig -Raw | ConvertFrom-Json
if (-not [string]::IsNullOrWhiteSpace($configuration.AnimeScheduleApiToken)) {
    throw 'Move AnimeScheduleApiToken into ignored appsettings.local.json before packaging.'
}
if (Test-Path -LiteralPath (Join-Path $publishDirectory 'appsettings.local.json')) {
    throw 'Private local configuration must not be included in publish output.'
}

& $InnoCompiler '/Qp' "/DAppVersion=$Version" "/DPublishDir=$publishDirectory" `
    "/DInstallerDir=$installerDirectory" (Join-Path $projectRoot 'installer\SlotCast.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$installer = Join-Path $installerDirectory "SlotCast-Setup-$Version-win-x64.exe"
$checksum = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$installer.sha256", "$checksum  $([System.IO.Path]::GetFileName($installer))`r`n")
Write-Output "Installer: $installer"
Write-Output "Self-contained app: $(Join-Path $publishDirectory 'SlotCast.exe')"
Write-Output 'Private configuration is not included. Configure AnimeScheduleApiToken on the destination device.'
