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

## 排序与定价

可上架和已上架列表各自支持名称、品质、实际市价、耐久比例排序，并可切换升序、降序。刷新库存只更新数据和限制有效页码，不重置当前页与滚动位置。主动搜索或改变排序时从第一页开始。

已上架列表的“市价 ×”输入框默认倍率为 1，点击“批量定价”即按每件实物的当前市价乘该倍率设置售价。范围是本柜全部现货及待补货商品，不受分页影响；顾客已预订商品和缺少来源的槽位会跳过。修改立即生效。

实际市价使用原版 `Thing.MarketValue`，已经计算材质、品质、耐久以及其他价格组件。不能再额外乘一次品质倍率。顾客筛选货柜和购买时均比较本件商品的真实市价与独立售价，预算仍独立生效。
