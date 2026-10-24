param([switch]$Verify)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts\win-x64'
& dotnet publish (Join-Path $taskRoot 'src/sensors/CodexSwitch.Sensors.csproj') -c Release -r win-x64 --self-contained true --nologo -p:UseSharedCompilation=false -o (Join-Path $taskOutput 'sensors')
if ($LASTEXITCODE -ne 0) { throw 'Native CPU sensor worker publish failed' }
$taskSensorHash = (Get-FileHash -LiteralPath (Join-Path $taskOutput 'sensors/CodexSwitch.Sensors.exe') -Algorithm SHA256).Hash
& dotnet publish (Join-Path $taskRoot 'src/windows/CodexSwitch.Windows.csproj') -c Release -r win-x64 --self-contained true --nologo -p:UseSharedCompilation=false "-p:CpuSensorWorkerSha256=$taskSensorHash" -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'licenses/PawnIO-Modules.LGPL-2.1.txt') -Destination (Join-Path $taskOutput 'sensors/PawnIO-Modules.LGPL-2.1.txt')
Copy-Item -LiteralPath (Join-Path $taskRoot 'src/sensors/modules/SOURCE.md') -Destination (Join-Path $taskOutput 'sensors/SOURCE.md')
Copy-Item -LiteralPath (Join-Path $taskRoot 'src/sensors/modules/source') -Destination (Join-Path $taskOutput 'sensors/module-source') -Recurse -Force
if ($Verify) {
    $taskReport = Join-Path $taskRoot 'artifacts\self-tests.json'
    $taskProcess = Start-Process -FilePath (Join-Path $taskOutput 'CodexSwitch.exe') -ArgumentList @('--self-test','--verify-readonly',('"'+$taskReport+'"')) -WindowStyle Hidden -PassThru
    if (!$taskProcess.WaitForExit(60000)) { throw 'Internal verification did not finish' }
    $taskResult = Get-Content -LiteralPath $taskReport -Raw | ConvertFrom-Json
    Write-Output ($taskResult | Select-Object version,passed,failed | ConvertTo-Json -Compress)
    if ($taskResult.failed -ne 0) {
        Write-Output ($taskResult.tests | Where-Object { -not $_.passed } | Select-Object name,failure | ConvertTo-Json -Compress)
    }
    if ($taskProcess.ExitCode -ne 0 -or $taskResult.failed -ne 0) { throw 'Internal verification failed' }
    $taskRecoveryProject = Join-Path $taskRoot 'tests/runtime-recovery/RuntimeRecovery.Tests.csproj'
    & dotnet run --project $taskRecoveryProject -c Release --no-launch-profile -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Windows runtime replacement regression failed' }
}
Write-Output ('Executable: '+(Join-Path $taskOutput 'CodexSwitch.exe'))
