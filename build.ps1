param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'dist'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw '需要 Windows x64 和 .NET Framework 4.x C# 编译器。' }
New-Item -ItemType Directory -Force -Path $out | Out-Null
function Compile([string]$Project, [string]$Name, [string]$Target, [string[]]$References) {
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $root "src\$Project") -Recurse -Filter '*.cs' | ForEach-Object FullName)
    $options = @('/nologo','/platform:x64','/unsafe',"/target:$Target",("/out:" + (Join-Path $out $Name)))
    foreach ($reference in $References) { $options += "/r:$reference" }
    & $csc @options @sources
    if ($LASTEXITCODE -ne 0) { throw "编译失败：$Project" }
    $config = Join-Path $root "src\$Project\app.config"
    if (Test-Path -LiteralPath $config) { Copy-Item -LiteralPath $config -Destination (Join-Path $out "$Name.config") -Force }
}
Compile 'BridgeInitFix' 'BridgeInitFix.dll' 'library' @()
Compile 'NativeBridge' 'native-fff-bridge.exe' 'exe' @('System.Runtime.Serialization.dll',(Join-Path $out 'BridgeInitFix.dll'))
Compile 'Worker' 'PixelShiftWorker.exe' 'exe' @('System.Web.Extensions.dll')
Compile 'App' 'Phocus像素位移合并-v0.4.exe' 'winexe' @('System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll')
Compile 'Launcher' '启动像素位移合并.exe' 'winexe' @('System.Windows.Forms.dll')
Copy-Item -LiteralPath (Join-Path $root 'src\Worker\app.config') -Destination (Join-Path $out '启动像素位移合并.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $out '使用说明.md') -Force
if ($Test) {
    $testExe = Join-Path $out 'VerifyTests.exe'
    & $csc /nologo /platform:x64 /r:System.Web.Extensions.dll "/out:$testExe" (Join-Path $root 'tests\VerifyTests.cs')
    if ($LASTEXITCODE -ne 0) { throw '测试程序编译失败' }
    & $testExe $out
    if ($LASTEXITCODE -ne 0) { throw '测试失败' }
}
Write-Host "编译完成：$out"
