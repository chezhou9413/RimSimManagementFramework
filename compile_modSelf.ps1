param(
    [Parameter(Position = 0)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('1.7')]
    [string]$Version = '1.7',
    [string]$RimWorldPath = 'E:\steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'E:\steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

#通过解决方案按依赖顺序编译框架、餐厅和旅馆，不启动游戏或执行部署。
$project = Join-Path $PSScriptRoot 'RimSimManagementFramework\Source\SimManagementLib\SimManagementLib.slnx'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "找不到统一解决方案：$project"
}
foreach ($reference in @('Assembly-CSharp.dll', 'UnityStubs.dll', 'Waylith.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $RimWorldPath $reference) -PathType Leaf)) {
        throw "找不到 RimWorld 1.7 引用：$RimWorldPath\$reference"
    }
}
if (-not (Test-Path -LiteralPath $HarmonyPath -PathType Leaf)) {
    throw "找不到 Harmony 1.7 程序集：$HarmonyPath"
}
& dotnet build $project -c $Configuration --nologo -v minimal "-p:RimWorldPath=$RimWorldPath" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) {
    throw "框架、餐厅与旅馆编译失败，退出码：$LASTEXITCODE"
}
Write-Host "[SUCCESS] 框架、餐厅与旅馆扩展 $Version ($Configuration) 编译完成。"
