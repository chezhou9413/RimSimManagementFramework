# RimSimFramework

本仓库共同维护模拟经营框架、餐厅扩展和旅馆扩展。三者是同级目录中的独立 Mod，共用 Git 历史、编译入口和部署脚本。餐厅和旅馆各自仅依赖框架，可以分别启用。

## 闲置与后台工作（1.7）

- 商店关闭时停止客流快照和经营指标的后台重算，沿用最近指标；重新营业后恢复，主动强制刷新仍可重算指标。设施占用格仅在建筑或区划变化后修补，管理界面复用营业校验缓存。
- 餐厅员工、座位和结账查询只遍历活跃记录。已结束用餐明细保留 5 个游戏日，活跃会话及其子订单不受截断影响；餐厅总览的金额按保留记录统计，框架经营账本不随明细清理而删除。
- 空传送带停止逐步运输和动画，停业或断电线路保留低频容器维护。已在用餐的食客仍可获得运输服务，上架新餐盘会唤醒线路；食品腐坏和损耗记账继续处理。线路连接图形随施工和拓扑变化更新。
- 旅馆按住客索引当前合同和钱包，复用店内前台缓存。休闲位置在区域内抽样并检查身边空位，保留客房、危险、预约和队列限制。补货的周期巡检按货柜数量均摊到 120 tick，库存通知仍即时进入处理队列。

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
  RimSimHotelExtension/          旅馆与温泉 Mod（仅 1.7）
    About/
    1.7/
    Source/RimSimHotelExtension/
  compile_modSelf.bat
  compile_modSelf.ps1
  deploy_modSelf.bat
  deploy_modSelf.ps1
```

框架说明见 [RimSimManagementFramework/README.md](RimSimManagementFramework/README.md)，餐厅说明见 [RimSimRestaurantExtension/README.md](RimSimRestaurantExtension/README.md)，旅馆说明见 [RimSimHotelExtension/README.md](RimSimHotelExtension/README.md)。

## 编译与部署

在本目录执行 `compile_modSelf.bat`，默认使用 .NET 10 SDK 编译 RimWorld 1.7 的 Release 配置。统一解决方案先构建框架，再构建两个扩展。程序集分别输出到三个 Mod 自己的 `1.7/Assemblies` 目录。

三个 Mod 各自在根目录 `Source` 中维护唯一一份源码和 .NET 10 工程，后续只面向 RimWorld 1.7 开发与编译。`1.7` 包含独立的 Defs、语言、贴图和程序集，加载时不依赖 `1.6` 或 `Content`。框架和餐厅既有 `1.6` 的程序集与资源保留在原提交状态，框架的旧版仍加载 `Content`；这些旧版内容冻结，不再更新或重新编译。旅馆不提供 1.6 内容。

执行 `deploy_modSelf.bat`，会先编译，再同时部署三个 Mod 到：

- `E:\steam\steamapps\common\RimWorld\Mods\RimSimManagementFramework`
- `E:\steam\steamapps\common\RimWorld\Mods\RimSimRestaurantExtension`
- `E:\steam\steamapps\common\RimWorld\Mods\RimSimHotelExtension`

三个临时安装目录均复制并验证完毕后才切换正式目录，保留各自已有的创意工坊发布标识。部署只更新 1.7；旧版的 `1.6`、`Content` 和根级 `Assemblies` 目录沿用当前安装，不从开发项目更新。源码、中间缓存与开发脚本不复制到安装目录。脚本不启动游戏。

三个 Mod 目录内部的编译和部署脚本也转调同一套根目录入口，部署任一入口都会同时处理框架、餐厅和旅馆。

PowerShell 参数：

```powershell
.\compile_modSelf.ps1
.\compile_modSelf.ps1 -Configuration Debug
.\deploy_modSelf.ps1 -DryRun
.\deploy_modSelf.ps1 -SkipBuild
```

`-DryRun` 仅列出并核对三个 Mod 的路径；`-SkipBuild` 使用已有程序集部署。编译和部署入口只接受 1.7，默认编译 Release。部署保留冻结的 1.6 运行文件。游戏运行时会拒绝部署，请先退出游戏。

1.7 直接引用游戏根目录的 `Assembly-CSharp.dll`、`UnityStubs.dll`、`Waylith.dll`，默认游戏路径为 `E:\steam\steamapps\common\RimWorld`；Harmony 引用为 `E:\steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll`。这些依赖不复制进 Mod 的程序集目录。编译可用 `-RimWorldPath` 和 `-HarmonyPath` 指定位置。

部署支持 `-TargetModsPath` 指定其他游戏 Mods 目录，并从其父目录读取 1.7 引用；`-HarmonyPath` 可单独指定 Harmony。

游戏中先启用框架，再按需要启用餐厅和旅馆扩展，并将框架排在扩展之前。旅馆可通过框架商品、服务和 CustomerAction 接口自动消费；没有餐厅时可使用食品商店。更新后需要重启游戏加载程序集。

服务配置页面的 1.7 接入方式见 [服务子页指南](RimSimManagementFramework/1.7/Docs/ServicePagesGuide.md)。后续服务使用同一入口，并由各自的设施条件控制显示。

## Git 管理

从本目录执行 Git 命令。目录整理通过路径移动保留原有提交历史、暂存内容和工作区修改，没有重建仓库或提交代码。源码、Defs、贴图、文档与 Mod 的程序集统一管理，`bin`、`obj` 和 IDE 缓存由根目录 `.gitignore` 排除。
