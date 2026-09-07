# RimSimRestaurantExtension

这是一个面向开源学习的 RimSimManagementFramework 扩展示例。它演示如何把一个“餐厅”玩法接入模拟经营框架的服务、岗位、订单、UI、表情、评价和调试入口。

项目位于主工程 Git 仓库的 `RimSimRestaurantExtension`，与 `RimSimManagementFramework` 目录同级，与框架共同管理源码、Defs 和文档。本目录直接包含 `About`、`LoadFolders.xml` 和 `1.6`，仍作为独立 Mod 加载，游戏内中文文本保留。

## 功能闭环

顾客预约店内桌椅入座，服务员交谈 120 Tick 后确认一项菜单，冻结来源、价格、份数与真实库存，并弹出一次商品图标。厨房菜单由厨师优先从本店冰箱取料，不足部分从绑定的后厨储存区补足；成品送入接待与出餐台。柜中现货由服务员直接取出，不需要烹饪配方。

服务员交付 90 Tick 后把实物放入顾客私人桌面托盘。顾客保持原座，依送达顺序执行原版吃喝，或将带走商品收进库存。等待与进食期间可继续请求接单，默认最多 3 次（含首次），间隔 1200 Tick；单店可设置为 1～5 次。

停止追加、全部子订单和桌面商品处理完毕后，统一登记实际吃喝和已接受带走商品的账单，释放餐位并进入同店收银。付款成功才登记收入；失败回收仍存在的未付款带走实物。接待与上菜分别计时，默认各 12000 Tick；服务超时取消未交付订单，已收到商品继续处理。

本扩展接入 10 种家具，均在框架经营建筑分类中建造：两种餐桌、餐椅、高脚椅、双人卡座、拼接吧台、冰箱和三种商品柜。双人卡座按格预约；壁挂柜使用原版依墙机制。旋转寿司台只保留资源，不提供建筑 Def。

冰箱容量 3000 件、耗电 200 W；壁挂橱柜和酒柜各 300 件，落地杂物柜 600 件。容量由所有品种共享。通电冰箱仅暂停腐坏，其余组件正常更新；断电按环境温度继续既有腐坏进度。先绑定原版后厨储存区，再在货柜管理中允许物品、设置目标与阈值，由框架补货岗位搬运真实库存。

单店页面使用框架搜索和紧凑表格，区分厨房制作与柜中现货。菜单和参数子窗口只回写父窗口草稿，统一保存后生效。柜内库存与销售规则由货柜独立草稿窗口保存。切页保留页面草稿，关闭窗口丢弃未保存内容。

会话、子订单、库存预留、员工携带及桌面实物均参与正常存读档。支持当前流程，不提供旧流程存档迁移。

## 关键接入点

- `CustomerActionDef`：声明持久化堂食动作。
- `ShopStaffRoleDef`：把厨师和服务员显示到框架岗位 UI。
- `ShopUiPageDef`：把餐厅总览和单店菜单挂进框架 UI。
- `RestaurantMenuItem`：保存菜品、价格、份数和每份食材。
- `GameComponent_RestaurantOrderManager`：持久化订单、维护索引并每 120 Tick 检查生命周期。
- `RestaurantOrderCoordinator`：负责接待、点菜、出餐与交付的阶段交接。
- `RestaurantMealTransferUtility`：负责出餐台、厨师、服务员和顾客之间的整单实物转移。
- `SimShopFinanceApi.QueueActionOrderCharges`：按动作和子订单标识幂等登记多条账单。
- `SimShopUiApi.ShowCustomerThingBubble`：公开的确定菜品图标气泡入口。
- `RestaurantDiningSpotUtility`：查找原版及扩展家具的独立餐位。
- `RestaurantStockUtility`：统一冰箱优先、绑定后厨其次的货源查询。
- `MapComponent_InventoryReservations`：跨店共享真实库存预留，提取时保留组件和引用。
- `RestaurantDiningSession` / `RestaurantTableTray`：管理多次点单与本人桌面商品。
- `SimShopDeliveredGoodsApi`：登记已送达带走商品及付款失败回收。
- `DebugActions_Restaurant`：生成包含菜单、食材、厨师、服务员、补货员和收银员的餐厅样板店。

## 编译

在已配置 MSBuild 的开发者命令提示符中进入：

```bat
cd /d E:\RimModDev\RimSimFramework\RimSimRestaurantExtension
```

运行：

```bat
build.bat
```

项目通过仓库内相对路径引用框架工程，MSBuild 会先编译框架。主工程解决方案也包含本扩展，可在仓库根目录运行 `compile_modSelf.bat` 统一编译。

生成 DLL 位于：

```text
1.6\Assemblies\RimSimRestaurantExtension.dll
```

## 安全部署

项目目录和部署到 RimWorld `Mods` 的文件夹均使用英文 `RimSimRestaurantExtension`。仓库根目录与两个 Mod 目录内的部署入口均同时编译并部署框架和餐厅，安装后仍是两个独立 Mod。

```bat
deploy_modSelf.bat
```

批处理与 PowerShell 入口统一调用仓库根目录脚本，准备并验证两个临时安装目录后切换到 Steam 游戏的 `Mods` 目录，保留各自创意工坊发布标识，不启动游戏。参数说明见 [仓库说明](../README.md)。

## 教程目录

- `Docs/01_UI页面接入.md`
- `Docs/02_岗位和WorkGiver.md`
- `Docs/03_餐厅订单闭环.md`
- `Docs/04_原版桌椅用餐点.md`
- `Docs/05_评价和表情.md`
- `Docs/06_Debug测试店.md`
- [贴图命名与资源清单](Docs/07_TextureCatalog.md)
- [家具、库存和商品配置](Docs/08_家具库存与追加点单.md)
- [编译与静态核查范围](Docs/09_编译与静态核查.md)
