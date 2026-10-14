param([switch]$Verify)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts\win-x64'
& dotnet publish (Join-Path $taskRoot 'src/windows/CodexSwitch.Windows.csproj') -c Release -r win-x64 --self-contained true --nologo -p:UseSharedCompilation=false -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
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
}
Write-Output ('Executable: '+(Join-Path $taskOutput 'CodexSwitch.exe'))
