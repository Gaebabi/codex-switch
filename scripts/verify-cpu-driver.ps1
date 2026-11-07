param([Parameter(Mandatory=$true)][string]$Worker)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'Driver validation is restricted to disposable hosted Windows CI; never run on a personal desktop' }
$taskDirectory = Join-Path $env:RUNNER_TEMP ('cpu-driver-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskDirectory | Out-Null
$taskInstaller = Join-Path $taskDirectory 'PawnIO_setup.exe'
Invoke-WebRequest -Uri 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $taskInstaller
if ((Get-FileHash -LiteralPath $taskInstaller -Algorithm SHA256).Hash -ne '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032') { throw 'CPU driver installer hash mismatch' }
if ((Get-AuthenticodeSignature -LiteralPath $taskInstaller).Status -ne 'Valid') { throw 'CPU driver publisher signature is invalid' }
$taskProcess = Start-Process -FilePath $taskInstaller -ArgumentList @('-install','-silent') -WindowStyle Hidden -PassThru
if (!$taskProcess.WaitForExit(60000) -or $taskProcess.ExitCode -ne 0) { throw 'Signed CPU driver install failed' }
$taskDriver = Get-CimInstance Win32_SystemDriver -Filter "Name='PawnIO'"
if (!$taskDriver -or $taskDriver.State -ne 'Running') { throw 'Signed CPU driver is not running' }
Write-Output 'Pinned signed PawnIO 2.2.0 installed/running on disposable hosted Windows; no security policy altered.'
@{schema=1;version='2.2.0';installerSha256='1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032';authenticodeValid=$true;running=$true;scope='Disposable hosted Windows VM only; no personal driver installation or physical CPU acceptance'} | ConvertTo-Json -Compress | Set-Content -LiteralPath 'artifacts/cpu-sensor-driver.json' -Encoding utf8
& dotnet run --project tests/sensors/CodexSwitch.Sensors.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false -- $Worker --driver
if ($LASTEXITCODE -ne 0) { throw 'CPU driver/worker verification failed' }
