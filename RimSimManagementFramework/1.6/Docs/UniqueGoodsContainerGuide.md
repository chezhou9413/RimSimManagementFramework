# 单件品质商品货柜继承指南

`Building_UniqueGoodsContainer` 用真实 `Thing` 实例保存带材质或品质的商品。每个槽位只能上架一件物品，顾客购买和结账会保留原物品的材质、品质、耐久、颜色与组件状态。

## XML 配置

派生 ThingDef 需要覆盖 `thingClass` 并添加单件货柜组件：

```xml
<thingClass>SimManagementLib.SimThingClass.Building_UniqueGoodsContainer</thingClass>
<comps>
  <li Class="SimManagementLib.SimThingComp.ThingCompProperties_UniqueGoodsContainer">
    <slotCount>30</slotCount>
    <requireStuffOrQuality>true</requireStuffOrQuality>
    <allowedThingCategories>
      <li>Apparel</li>
    </allowedThingCategories>
  </li>
</comps>
```

`allowedThingCategories` 为空时不限制物品分类。无论分类配置如何，生物编码物品和死人衣物都不能上架。

## C# 扩展

派生类可以覆盖 `IsEligibleUniqueThing` 添加业务限制，覆盖 `DrawUniqueGoods` 绘制槽位内商品，并在 `NotifyUniqueSlotsChanged` 中刷新额外缓存。`UniqueSlots` 提供只读槽位数据，`GetStoredThing` 返回槽位当前持有的真实物品。
