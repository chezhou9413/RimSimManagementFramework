# 给现有储存建筑添加货柜销售

在目标建筑的 ThingDef 的 `<comps>` 中加入以下条目，保留原有 `thingClass`。模组应在经营框架和目标建筑模组之后加载。

```xml
<li Class="SimManagementLib.SimThingComp.CompProperties_StorageSales" />
```

## 配置上架容量和商品类型

例如，让一座货柜同时上架最多 30 件食品或工业用药，可以把组件写成下面这样。

```xml
<li Class="SimManagementLib.SimThingComp.CompProperties_StorageSales">
  <maxListedCount>30</maxListedCount>
  <allowedThingCategories><li>Foods</li></allowedThingCategories>
  <allowedThingDefs><li>MedicineIndustrial</li></allowedThingDefs>
</li>
```

| XML 参数 | 取值与用途 |
| --- | --- |
| `maxListedCount` | 每座货柜同时可售的总件数，所有品种共用。省略或填 `-1` 时全部可上架，填 `0` 时暂停展示全部商品。 |
| `allowedThingDefs` | 指定物品的 `ThingDef.defName`，例如 `MedicineIndustrial`。 |
| `allowedThingCategories` | 原版或模组的 `ThingCategoryDef.defName`，包含其子分类。例如 `Foods` 包含下属食品。 |
| `allowedGoodsCategoryIds` | 框架商品分类或已注册的自定义分类 ID。例如 `<allowedGoodsCategoryIds><li>Goods_Medicine</li></allowedGoodsCategoryIds>` 匹配框架药品分类。 |

三个名单取并集，全部省略或留空时允许所有品种。玩家仍可在每座货柜中逐品种停售。名单外的物品可以按宿主规则存放，店员只为允许经营的品种补货。

一柜存有 100 件符合条件的商品，上架上限设为 30 时，顾客一次最多可从该柜取走当时上架的 30 件。各堆按实物编号从小到大占用名额，最后一堆可以只上架一部分。售出、停售或预约变化会腾出名额，其他可售库存自动补上。套餐按整笔购买开始时的上架件数配货。

管理窗口会显示整柜已上架件数，以及每个品种的「可售/现存」。原版搬运和店员补货可以继续备足 100 件，具体能存多少由宿主容量和储存筛选决定。

## 原版大小储物架示例

框架 1.7 已为原版储物架（`Shelf`）和小型储物架（`ShelfSmall`）挂载组件，销售默认关闭。给其他模组的建筑接入时，可复制下面的补丁到兼容模组的 `1.7/Patches/` 目录，将 XPath 中的建筑名换成目标 `defName`。[框架自带的货架补丁](../Patches/StorageSales_VanillaShelves.xml)

```xml
<?xml version="1.0" encoding="utf-8"?>
<Patch>
  <Operation Class="PatchOperationConditional">
    <xpath>Defs/ThingDef[defName="Shelf" or defName="ShelfSmall"][not(comps)]</xpath>
    <match Class="PatchOperationAdd">
      <xpath>Defs/ThingDef[defName="Shelf" or defName="ShelfSmall"][not(comps)]</xpath>
      <value><comps /></value>
    </match>
  </Operation>
  <Operation Class="PatchOperationConditional">
    <xpath>Defs/ThingDef[defName="Shelf" or defName="ShelfSmall"]/comps[not(li[@Class="SimManagementLib.SimThingComp.CompProperties_StorageSales"])]</xpath>
    <match Class="PatchOperationAdd">
      <xpath>Defs/ThingDef[defName="Shelf" or defName="ShelfSmall"]/comps[not(li[@Class="SimManagementLib.SimThingComp.CompProperties_StorageSales"])]</xpath>
      <value><li Class="SimManagementLib.SimThingComp.CompProperties_StorageSales" /></value>
    </match>
  </Operation>
</Patch>
```

## 玩家使用

货架默认关闭“货柜销售”，划入商店后仍按普通储物架使用。要出售架上的物品，选中这座货架，手动开启“货柜销售”。开关按建筑保存，读档后沿用。

通过建筑的“货柜管理”按钮，或经营总管里该店的货柜入口，可以设置各品种的上架状态、单价、目标件数和补货阈值。点击“保存设置”后生效。

单价为 0 时，按顾客所选实物的市价报价，并应用本店的收银员加成。填入正数会为这座货柜上的同一品种统一设置基础售价，也可以用于给零市价物品定价。套餐沿用商店的套餐价格，可以混用这类货架和普通框架货柜。结账交付实际取走的堆叠，包括装备的品质、材料、耐久及物品组件状态。

目标为 0 时只卖已有库存。设置正目标并安排补货员后，库存及在途数量达到补货阈值或更低时，店员开始补至目标。阈值为 -1 时使用目标量。空货架可以通过“添加可储存品种”预设目标。宿主的储存筛选、优先级和原版搬运继续生效，实际接收的数量受空间和预约限制。

## 生成测试商店

在地图上打开开发者模式的调试操作菜单，进入 **SimShop → 生成原版货架测试商店（点选位置）**，点一块南侧可通行的 9×9 空地。店内有收银台、一名收银员和一名补货员。

- 西北的大货架已启售，有 6 件工业用药和 8 份包装生存食品。药品基础单价为 15 银，补货目标为 20 件，触发阈值为 8 件。
- 东北的小货架已启售，两件衬衫材质相同，一件普通、一件极佳，均按实物市价报价。
- 东南的备货架按普通仓储使用，存有 20 件工业用药。解除暂停后可以观察补货员从这里向大货架交货。需要顾客时，可使用同一菜单的 **强制刷新顾客（当前地图）**。

## 宿主库存

原版式货架使用本建筑的 `ISlotGroupParent.GetSlotGroup()`。内仓建筑可以由建筑自身或唯一的持货组件通过 `IThingHolder.GetDirectlyHeldThings()` 提供实际商品仓。组件遵循宿主公开的收货筛选、搬运开关、容量和交互位置。请将组件挂在用于存放待售商品的建筑上，带有多个不同物品仓的建筑应在建筑自身公开其商品仓。只用数字记录库存的建筑需要由其作者提供实物存取能力。
