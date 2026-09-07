param(
    [switch]$DryRun,
    [switch]$SkipBuild,
    [string]$TargetModsPath = 'E:\steam\steamapps\common\RimWorld\Mods',
    [string]$MSBuildPath = 'E:\VS\MSBuild\Current\Bin\MSBuild.exe'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

#校验模组内容，职责是确保每个待部署目录拥有元数据、加载配置和编译程序集。
function Assert-ModLayout {
    param([string]$Path, [string]$Assembly)
    foreach ($relative in @('About\About.xml', 'LoadFolders.xml', "1.6\Assemblies\$Assembly")) {
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

#清理本次部署产生的目录，职责是限制名称并保留正在使用的两个模组目录。
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

#建立两个独立模组的部署计划，职责是统一目标游戏并保留各自的发布标识。
if (-not (Test-Path -LiteralPath $TargetModsPath -PathType Container)) {
    throw "找不到游戏 Mods 目录：$TargetModsPath"
}
$TargetModsPath = (Resolve-Path -LiteralPath $TargetModsPath).Path.TrimEnd('\')
$plans = @(
    [pscustomobject]@{ Name = 'RimSimManagementFramework'; Assembly = 'SimManagementLib.dll' },
    [pscustomobject]@{ Name = 'RimSimRestaurantExtension'; Assembly = 'RimSimRestaurantExtension.dll' }
)
$stageNames = @()
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
    Write-Host '[DRY-RUN] 两个模组的部署路径已核对；未编译或复制文件。'
    return
}

#默认先构建两个程序集，职责是避免框架接口与餐厅引用版本不一致。
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'compile_modSelf.ps1') -Configuration Debug -MSBuildPath $MSBuildPath
}

#先准备两个完整目录，职责是在复制或验证失败时保持现有安装不变。
foreach ($plan in $plans) {
    Assert-ModLayout $plan.Source $plan.Assembly
    if (Test-Path -LiteralPath $plan.Backup) {
        throw "存在上次部署的备份，请先检查后再部署：$($plan.Backup)"
    }
    Remove-DeploymentStage $plan.Temporary
    New-Item -ItemType Directory -Path $plan.Temporary | Out-Null
    & robocopy $plan.Source $plan.Temporary /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XD Source obj bin .git .vs /XF 'compile_modSelf.*' 'deploy_modSelf.*' build.bat
    if ($LASTEXITCODE -gt 7) {
        throw "复制 $($plan.Name) 失败，退出码：$LASTEXITCODE"
    }
    $published = Join-Path $plan.Target 'About\PublishedFileId.txt'
    if (Test-Path -LiteralPath $published -PathType Leaf) {
        Copy-Item -LiteralPath $published -Destination (Join-Path $plan.Temporary 'About\PublishedFileId.txt') -Force
    }
    Assert-ModLayout $plan.Temporary $plan.Assembly
}

#一起切换两份安装，职责是在任一切换失败时恢复已移动的旧安装。
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

#完成后清理备份，职责是仅在两个模组均已安装成功后释放旧目录。
foreach ($plan in $plans) {
    Remove-DeploymentStage $plan.Backup
    Write-Host "[SUCCESS] 已部署：$($plan.Target)"
}
