# RimSimFramework

本仓库共同维护模拟经营框架与餐厅扩展。两者是同级目录中的独立 Mod，共用 Git 历史、编译入口和部署脚本。

## 目录

```text
RimSimFramework/                 Git 仓库根目录
  RimSimManagementFramework/     框架 Mod
    About/
    Content/
    1.6/
    1.7/
    Source/SimManagementLib/
  RimSimRestaurantExtension/     餐厅 Mod
    About/
    1.6/
    1.7/
    Source/RimSimRestaurantExtension/
    Docs/
  compile_modSelf.bat
  compile_modSelf.ps1
  deploy_modSelf.bat
  deploy_modSelf.ps1
```

框架说明见 [RimSimManagementFramework/README.md](RimSimManagementFramework/README.md)，餐厅说明见 [RimSimRestaurantExtension/README.md](RimSimRestaurantExtension/README.md)。

## 编译与部署

在本目录执行 `compile_modSelf.bat`，默认使用 .NET 10 SDK 编译 RimWorld 1.7 的 Release 配置。餐厅项目引用框架项目，先构建框架，再构建餐厅。程序集分别输出到两个 Mod 自己的 `1.7/Assemblies` 目录。

两个 Mod 各自在根目录 `Source` 中维护唯一一份源码和 .NET 10 工程，后续只面向 RimWorld 1.7 开发与编译。`1.7` 包含独立的 Defs、语言、贴图和程序集，加载时不依赖 `1.6` 或 `Content`。`1.6` 的程序集与资源保留在原提交状态，框架的旧版仍加载 `Content`；这些旧版内容冻结，不再更新或重新编译。本次未提交的功能和修复仅进入 1.7。

执行 `deploy_modSelf.bat`，会先编译，再同时部署两个 Mod 到：

- `E:\steam\steamapps\common\RimWorld\Mods\RimSimManagementFramework`
- `E:\steam\steamapps\common\RimWorld\Mods\RimSimRestaurantExtension`

两个临时安装目录均复制并验证完毕后才切换正式目录，保留各自已有的创意工坊发布标识。源码、中间缓存与开发脚本不复制到安装目录。脚本不启动游戏。

两个 Mod 目录内部的编译和部署脚本也转调同一套根目录入口，部署任一入口都会同时处理框架与餐厅。

PowerShell 参数：

```powershell
.\compile_modSelf.ps1
.\compile_modSelf.ps1 -Configuration Debug
.\deploy_modSelf.ps1 -DryRun
.\deploy_modSelf.ps1 -SkipBuild
```

`-DryRun` 仅列出并核对两个 Mod 的路径；`-SkipBuild` 使用已有程序集部署两者。编译和部署入口只接受 1.7，默认编译 Release。部署保留冻结的 1.6 运行文件。游戏运行时会拒绝部署，请先退出游戏。

1.7 直接引用游戏根目录的 `Assembly-CSharp.dll`、`UnityStubs.dll`、`Waylith.dll`，默认游戏路径为 `E:\steam\steamapps\common\RimWorld`；Harmony 引用为 `E:\steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll`。这些依赖不复制进 Mod 的程序集目录。编译可用 `-RimWorldPath` 和 `-HarmonyPath` 指定位置。

部署支持 `-TargetModsPath` 指定其他游戏 Mods 目录，并从其父目录读取 1.7 引用；`-HarmonyPath` 可单独指定 Harmony。

游戏中仍需同时启用两个 Mod，并将框架排在餐厅之前。更新后需要重启游戏加载程序集。

## Git 管理

从本目录执行 Git 命令。目录整理通过路径移动保留原有提交历史、暂存内容和工作区修改，没有重建仓库或提交代码。源码、Defs、贴图、文档与 Mod 的程序集统一管理，`bin`、`obj` 和 IDE 缓存由根目录 `.gitignore` 排除。
