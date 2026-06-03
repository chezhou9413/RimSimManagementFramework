# 顾客溢价敏感度配置指南

顾客会根据商品售价和市价的倍率决定购买意愿。未配置 `priceSensitivity` 的现有顾客、旧存档顾客和旧自定义顾客 JSON 都会自动使用默认参数。

## 字段说明

```xml
<priceSensitivity>
  <discountWeightMultiplier>1.35</discountWeightMultiplier>
  <softMarkupRatio>1.5</softMarkupRatio>
  <rejectMarkupRatio>2.5</rejectMarkupRatio>
  <overpricedMinWeight>0.05</overpricedMinWeight>
  <complainMarkupRatio>2.0</complainMarkupRatio>
</priceSensitivity>
```

- `discountWeightMultiplier`：低于市价时的最高购买权重加成。
- `softMarkupRatio`：小幅溢价上限，超过后购买权重明显降低。
- `rejectMarkupRatio`：超过该市价倍率时顾客直接拒买。
- `overpricedMinWeight`：软溢价到拒买区间内的最低购买权重。
- `complainMarkupRatio`：超过该倍率时，拒买或无消费评价会记录价格偏高原因。

## Def 示例

```xml
<SimManagementLib.SimDef.CustomerKindDef>
  <defName>Customer_BargainHunter</defName>
  <label>精打细算顾客</label>
  <priceSensitivity>
    <discountWeightMultiplier>1.6</discountWeightMultiplier>
    <softMarkupRatio>1.2</softMarkupRatio>
    <rejectMarkupRatio>1.8</rejectMarkupRatio>
    <overpricedMinWeight>0.03</overpricedMinWeight>
    <complainMarkupRatio>1.4</complainMarkupRatio>
  </priceSensitivity>
</SimManagementLib.SimDef.CustomerKindDef>
```

档案可以覆盖顾客类型配置：

```xml
<spawnProfiles>
  <li>
    <label>富裕档案</label>
    <weight>1</weight>
    <priceSensitivity>
      <softMarkupRatio>2.0</softMarkupRatio>
      <rejectMarkupRatio>4.0</rejectMarkupRatio>
      <complainMarkupRatio>3.0</complainMarkupRatio>
    </priceSensitivity>
  </li>
</spawnProfiles>
```

## 自定义顾客 JSON 示例

```json
{
  "kindId": "custom_bargain_hunter",
  "label": "精打细算顾客",
  "priceSensitivity": {
    "discountWeightMultiplier": 1.6,
    "softMarkupRatio": 1.2,
    "rejectMarkupRatio": 1.8,
    "overpricedMinWeight": 0.03,
    "complainMarkupRatio": 1.4
  }
}
```

## 推荐模板

- 精打细算顾客：`softMarkupRatio=1.2`，`rejectMarkupRatio=1.8`，`discountWeightMultiplier=1.6`。
- 普通顾客：不配置，使用默认值。
- 富裕/收藏型顾客：`softMarkupRatio=2.0`，`rejectMarkupRatio=4.0`，`discountWeightMultiplier=1.15`。
- 冲动消费顾客：`softMarkupRatio=1.8`，`rejectMarkupRatio=3.2`，`overpricedMinWeight=0.12`。

## 测试方法

1. 同一种商品分别设置为 `0.8x`、`1.2x`、`3.0x` 市价。
2. 默认顾客应更偏向购买折扣商品，仍可能购买 `1.2x` 商品，拒买 `3.0x` 商品。
3. 精打细算顾客应更早拒买，富裕顾客应能接受更高溢价。
4. 开启开发模式检查顾客 Inspect 或 Debug 报告，可看到价格拒买原因和价格倍率。
