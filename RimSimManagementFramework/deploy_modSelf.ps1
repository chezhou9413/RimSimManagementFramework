param(
    [switch]$DryRun,
    [switch]$SkipBuild,
    [string]$TargetModsPath = 'E:\steam\steamapps\common\RimWorld\Mods',
    [string]$MSBuildPath = 'E:\VS\MSBuild\Current\Bin\MSBuild.exe'
)

#转调仓库统一部署入口，职责是保证从任意模组目录部署时都安装完整的一组程序集。
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '..\deploy_modSelf.ps1') @PSBoundParameters
