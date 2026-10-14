param([ValidateSet('osx-arm64','osx-x64')][string[]]$Runtime = @('osx-arm64','osx-x64'))
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskStamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
foreach ($taskRid in $Runtime) {
    $taskOutput = Join-Path $taskRoot ('artifacts\macos\'+$taskRid+'\'+$taskStamp)
    $taskApp = Join-Path $taskOutput 'Codex Switch.app'
    $taskBinary = Join-Path $taskApp 'Contents\MacOS'
    $taskResources = Join-Path $taskApp 'Contents\Resources'
    New-Item -ItemType Directory -Path $taskBinary,$taskResources | Out-Null
    & dotnet publish (Join-Path $taskRoot 'src/macos/CodexSwitch.Mac.csproj') -c Release -r $taskRid --self-contained true --nologo -p:UseSharedCompilation=false -p:UseAppHost=true -o $taskBinary
    if ($LASTEXITCODE -ne 0) { throw ('macOS publish failed: '+$taskRid) }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'src/macos/Info.plist') -Destination (Join-Path $taskApp 'Contents\Info.plist')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'assets/macos/app-icon.icns') -Destination $taskResources
    Copy-Item -LiteralPath (Join-Path $taskRoot 'licenses/Burn0Assets.LICENSE.txt') -Destination $taskResources
    Copy-Item -LiteralPath (Join-Path $taskRoot 'src/macos/entitlements.plist') -Destination $taskResources
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts\install-macos.sh') -Destination (Join-Path $taskOutput 'Install.command')
    & python (Join-Path $taskRoot 'scripts\zip-macos.py') $taskApp (Join-Path $taskOutput ('CodexSwitch-'+$taskRid+'.zip'))
    if ($LASTEXITCODE -ne 0) { throw ('macOS ZIP failed: '+$taskRid) }
    Write-Output ('Unsigned, untested bundle: '+$taskApp)
}
