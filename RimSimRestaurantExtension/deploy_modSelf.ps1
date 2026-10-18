param(
    [switch]$DryRun,
    [switch]$SkipBuild,
    [ValidateSet('1.7')]
    [string]$Version = '1.7',
    [string]$TargetModsPath = 'E:\steam\steamapps\common\RimWorld\Mods',
    [string]$HarmonyPath = 'E:\steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll'
)

#转调仓库统一部署入口，职责是保证从任意模组目录部署时都安装完整的一组程序集。
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '..\deploy_modSelf.ps1') @PSBoundParameters
