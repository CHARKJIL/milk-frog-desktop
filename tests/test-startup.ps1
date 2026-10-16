$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repoRoot 'work\startup test'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$petExecutable = Join-Path $testRoot 'MilkFrog.exe'
Copy-Item -LiteralPath (Join-Path $repoRoot 'windows\MilkFrog.exe') -Destination $petExecutable
$testExecutable = Join-Path $testRoot 'StartupRegistrationTest.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler '/nologo' '/target:exe' '/platform:x64' ('/reference:' + $petExecutable) ('/out:' + $testExecutable) (Join-Path $PSScriptRoot 'StartupRegistrationTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Startup test compilation failed.' }
& $testExecutable
if ($LASTEXITCODE -ne 0) { throw 'Startup registration integration test failed.' }
