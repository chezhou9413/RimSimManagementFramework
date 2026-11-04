param(
    [switch]$DryRun,
    [switch]$SkipBuild,
    [ValidateSet('1.7')]
    [string]$Version = '1.7',
    [string]$TargetModsPath = 'E:\steam\steamapps\common\RimWorld\Mods',
    [string]$HarmonyPath = 'E:\steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

#校验待部署目录的元数据、加载配置和目标版本程序集。
function Assert-ModLayout {
    param([string]$Path, [string]$Assembly)
    foreach ($relative in @('About\About.xml', 'LoadFolders.xml', "$Version\Assemblies\$Assembly")) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $relative) -PathType Leaf)) {
            throw "模组缺少必要文件：$Path\$relative"
        }
    }
}

#限制部署路径，职责是只操作指定 Mods 目录内的直接子目录。
function Assert-DeploymentPath {
    param([string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ([System.IO.Directory]::GetParent($full).FullName -ne $TargetModsPath) {
        throw "部署路径超出 Mods 目录：$full"
    }
    if ((Test-Path -LiteralPath $full) -and
        ((Get-Item -LiteralPath $full -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw "部署目录不能是重解析点：$full"
    }
}

#清理本次部署产生的目录，限制名称并保留正在使用的模组目录。
function Remove-DeploymentStage {
    param([string]$Path)
    Assert-DeploymentPath $Path
    if ([System.IO.Path]::GetFileName($Path) -notin $stageNames) {
        throw "拒绝清理非部署临时目录：$Path"
    }
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

#建立三个独立模组的部署计划，统一目标游戏并保留各自的发布标识。
if (-not (Test-Path -LiteralPath $TargetModsPath -PathType Container)) {
    throw "找不到游戏 Mods 目录：$TargetModsPath"
}
$TargetModsPath = (Resolve-Path -LiteralPath $TargetModsPath).Path.TrimEnd('\')
$plans = @(
    [pscustomobject]@{ Name = 'RimSimManagementFramework'; Assembly = 'SimManagementLib.dll' },
    [pscustomobject]@{ Name = 'RimSimRestaurantExtension'; Assembly = 'RimSimRestaurantExtension.dll' },
    [pscustomobject]@{ Name = 'RimSimHotelExtension'; Assembly = 'RimSimHotelExtension.dll' }
)
$stageNames = @()
$legacyFolders = @('1.6', 'Content', 'Assemblies')
foreach ($plan in $plans) {
    $plan | Add-Member -NotePropertyName Source -NotePropertyValue (Join-Path $PSScriptRoot $plan.Name)
    $plan | Add-Member -NotePropertyName Target -NotePropertyValue (Join-Path $TargetModsPath $plan.Name)
    $plan | Add-Member -NotePropertyName Temporary -NotePropertyValue (Join-Path $TargetModsPath ($plan.Name + '.__deploy_tmp'))
    $plan | Add-Member -NotePropertyName Backup -NotePropertyValue (Join-Path $TargetModsPath ($plan.Name + '.__deploy_backup'))
    $plan | Add-Member -NotePropertyName PreviousMoved -NotePropertyValue $false
    $plan | Add-Member -NotePropertyName Activated -NotePropertyValue $false
    $stageNames += ($plan.Name + '.__deploy_tmp'), ($plan.Name + '.__deploy_backup')
    foreach ($path in @($plan.Target, $plan.Temporary, $plan.Backup)) { Assert-DeploymentPath $path }
    if (-not (Test-Path -LiteralPath (Join-Path $plan.Source 'About\About.xml') -PathType Leaf)) {
        throw "找不到开发模组目录：$($plan.Source)"
    }
    Write-Host "$($plan.Source) -> $($plan.Target)"
}

if ($DryRun) {
    Write-Host '[DRY-RUN] 三个模组的部署路径已核对；未编译或复制文件。'
    return
}

#运行中的游戏持有旧程序集，部署前要求先退出游戏。
$gameProcesses = @(Get-Process -Name RimWorld, RimWorldWin64 -ErrorAction SilentlyContinue)
if ($gameProcesses.Count -gt 0) {
    throw 'RimWorld 正在运行，请先退出游戏再部署框架与扩展。'
}

#按目标版本构建三个程序集，保持扩展引用的框架接口一致。
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'compile_modSelf.ps1') -Version $Version -RimWorldPath (Split-Path $TargetModsPath -Parent) -HarmonyPath $HarmonyPath
}

#先准备三个完整目录，在复制或验证失败时保持现有安装不变。
foreach ($plan in $plans) {
    Assert-ModLayout $plan.Source $plan.Assembly
    if (Test-Path -LiteralPath $plan.Backup) {
        throw "存在上次部署的备份，请先检查后再部署：$($plan.Backup)"
    }
    Remove-DeploymentStage $plan.Temporary
    New-Item -ItemType Directory -Path $plan.Temporary | Out-Null
    $excludedLegacyPaths = @($legacyFolders | ForEach-Object { Join-Path $plan.Source $_ })
    & robocopy $plan.Source $plan.Temporary /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XD Source obj bin .git .vs @excludedLegacyPaths /XF 'compile_modSelf.*' 'deploy_modSelf.*' build.bat
    if ($LASTEXITCODE -gt 7) {
        throw "复制 $($plan.Name) 失败，退出码：$LASTEXITCODE"
    }
    #旧版目录只沿用当前安装，不从开发项目部署或更新。
    foreach ($legacyFolder in $legacyFolders) {
        $installedLegacy = Join-Path $plan.Target $legacyFolder
        if (-not (Test-Path -LiteralPath $installedLegacy -PathType Container)) { continue }
        $stagedLegacy = Join-Path $plan.Temporary $legacyFolder
        & robocopy $installedLegacy $stagedLegacy /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -gt 7) {
            throw "保留已安装旧版目录失败：$installedLegacy，退出码：$LASTEXITCODE"
        }
    }
    $published = Join-Path $plan.Target 'About\PublishedFileId.txt'
    if (Test-Path -LiteralPath $published -PathType Leaf) {
        Copy-Item -LiteralPath $published -Destination (Join-Path $plan.Temporary 'About\PublishedFileId.txt') -Force
    }
    Assert-ModLayout $plan.Temporary $plan.Assembly
}

#一起切换三份安装，在任一切换失败时恢复已移动的旧安装。
try {
    foreach ($plan in $plans) {
        if (Test-Path -LiteralPath $plan.Target) {
            Rename-Item -LiteralPath $plan.Target -NewName ([System.IO.Path]::GetFileName($plan.Backup))
            $plan.PreviousMoved = $true
        }
        Rename-Item -LiteralPath $plan.Temporary -NewName $plan.Name
        $plan.Activated = $true
        Assert-ModLayout $plan.Target $plan.Assembly
    }
}
catch {
    for ($i = $plans.Count - 1; $i -ge 0; $i--) {
        $plan = $plans[$i]
        if ($plan.Activated) {
            Rename-Item -LiteralPath $plan.Target -NewName ([System.IO.Path]::GetFileName($plan.Temporary))
        }
        if ($plan.PreviousMoved) {
            Rename-Item -LiteralPath $plan.Backup -NewName $plan.Name
        }
    }
    throw
}

#完成后清理备份，仅在三个模组均已安装成功后释放旧目录。
foreach ($plan in $plans) {
    Remove-DeploymentStage $plan.Backup
    Write-Host "[SUCCESS] 已部署：$($plan.Target)"
}
