param(
    [Parameter(Position = 0)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$MSBuildPath = 'E:\VS\MSBuild\Current\Bin\MSBuild.exe'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

#通过餐厅项目引用按依赖顺序编译两个模组，不启动游戏或执行部署。
$project = Join-Path $PSScriptRoot 'RimSimRestaurantExtension\1.6\Source\RimSimRestaurantExtension\RimSimRestaurantExtension.csproj'
if (-not (Test-Path -LiteralPath $MSBuildPath -PathType Leaf)) {
    throw "找不到 MSBuild，请使用 -MSBuildPath 指定位置：$MSBuildPath"
}
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "找不到餐厅工程：$project"
}
& $MSBuildPath $project /t:Build /p:Configuration=$Configuration /p:Platform=AnyCPU /nr:false /nologo /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "框架与餐厅编译失败，退出码：$LASTEXITCODE"
}
Write-Host '[SUCCESS] 框架与餐厅扩展编译完成。'
