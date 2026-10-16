$ErrorActionPreference = 'Stop'
$petRoot = Split-Path -Parent $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpfRoot = Join-Path $frameworkRoot 'WPF'
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $wpfRoot $_) }
$options = @('/nologo','/target:winexe','/platform:x64','/optimize+',('/out:' + (Join-Path $petRoot 'MilkFrog.exe')), ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')))
$iconFile = Join-Path $petRoot 'milk-frog.ico'
if (Test-Path -LiteralPath $iconFile) { $options += '/win32icon:' + $iconFile }
& (Join-Path $frameworkRoot 'csc.exe') @options @references (Join-Path $PSScriptRoot 'MilkFrog.cs')
if ($LASTEXITCODE -ne 0) { throw '奶蛙编译失败。' }
Write-Output (Join-Path $petRoot 'MilkFrog.exe')
