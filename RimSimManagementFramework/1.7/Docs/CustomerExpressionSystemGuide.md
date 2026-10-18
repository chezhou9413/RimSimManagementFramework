# 顾客表情系统说明

## 1. 系统目标

`RimSimManagementFramework` 里的顾客表情系统，本质上是一个“按顾客类型和上下文标签匹配表情贴图，再在小人头顶弹出气泡”的系统。

它适合做两类扩展：

1. 给某个种族的小人顾客配置专属表情包。
2. 给某个主题人设或子种类配置专属表情风格，例如女仆咖啡店、兽耳店员、异星游客等。

当前实现只支持“图片表情气泡”，不直接支持文本 emoji 或纯文字表情。

## 2. 代码结构

### 2.1 核心 Def

表情 Def 定义在：

- [CustomerExpressionSetDef.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimDef/CustomerExpressionSetDef.cs)

核心类型有三个：

1. `CustomerExpressionTagExtension`
   用于给 `ThingDef` 或 `PawnKindDef` 打标签。

2. `CustomerExpressionSetDef`
   一整套表情配置，负责声明“这套表情适用于哪些种族/哪些标签”。

3. `CustomerExpressionEntry`
   单条表情规则，负责声明“某个事件触发时，用哪张图，以多大概率和权重显示”。

### 2.2 触发与匹配入口

表情选择和播放入口在：

- [CustomerExpressionUtility.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/Tool/CustomerExpressionUtility.cs)

显示气泡的底层工具在：

- [ShopBubbleUtility.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/Tool/ShopBubbleUtility.cs)

匹配流程如下：

1. 游戏逻辑在某个时机调用 `CustomerExpressionUtility.TryShowExpression(pawn, eventId, request)`。
2. 系统收集该顾客的标签集合。
3. 从全部 `CustomerExpressionSetDef` 中筛出匹配该顾客的表情集。
4. 如果命中多个表情集，只保留 `priority` 最高的一组。
5. 从这组里筛选 `eventId` 和上下文标签匹配的 `CustomerExpressionEntry`。
6. 先过 `chance`，再按 `weight` 做加权随机。
7. 通过 `iconTexPath` 加载贴图并弹出头顶气泡。
8. 同一个 `pawn + eventId` 受 `cooldownTicks` 限制。

## 3. 标签系统

### 3.1 系统自动收集的标签

`CustomerExpressionUtility.CollectTags()` 会自动加入这些标签：

- `pawn.def.defName`
- `pawn.kindDef.defName`
- `race:种族DefName`
- `pawnkind:PawnKindDefName`
- `humanlike`
- `animal`
- `mechanoid`
- `flesh`

此外，如果 `ThingDef` 或 `PawnKindDef` 上挂了 `CustomerExpressionTagExtension`，扩展里的 `expressionTags` 也会被加入。

### 3.2 额外上下文标签

部分时机会额外传入上下文标签：

- 购买套餐时会附加 `combo`
- 购买偏好商品时会附加 `preferred`

这些标签不是顾客固有标签，而是“这一次表情触发的上下文标签”。

## 4. 如何给其他种族配置一套专属表情

有两种主流接入方式。

### 4.1 方式 A：按种族 Def 直接匹配

适合“某个种族整体都使用一套表情”的情况。

示例：

```xml
<SimManagementLib.SimDef.CustomerExpressionSetDef>
  <defName>SimShopExpression_MyRace</defName>
  <label>my race customer expressions</label>
  <priority>50</priority>
  <targetRaceDefs>
    <li>MyRaceThingDef</li>
  </targetRaceDefs>
  <requiredTags>
    <li>humanlike</li>
  </requiredTags>
  <expressions>
    <li>
      <eventId>arrival</eventId>
      <iconTexPath>MyRace/CustomerExpressions/arrival</iconTexPath>
      <chance>0.7</chance>
      <weight>1.0</weight>
      <cooldownTicks>300</cooldownTicks>
      <popupScale>1.2</popupScale>
    </li>
    <li>
      <eventId>purchase_preferred_item</eventId>
      <iconTexPath>MyRace/CustomerExpressions/favorite</iconTexPath>
      <chance>0.9</chance>
      <weight>1.0</weight>
      <cooldownTicks>120</cooldownTicks>
      <popupScale>1.25</popupScale>
      <requiredContextTags>
        <li>preferred</li>
      </requiredContextTags>
    </li>
  </expressions>
</SimManagementLib.SimDef.CustomerExpressionSetDef>
```

### 4.2 方式 B：按标签匹配

适合“多个种族共用一套主题表情”或“同种族不同子职业有不同表情”的情况。

先在目标种族或 PawnKind 上挂标签：

```xml
<ThingDef ParentName="AlienRaceBase">
  <defName>MyRaceThingDef</defName>
  <modExtensions>
    <li Class="SimManagementLib.SimDef.CustomerExpressionTagExtension">
      <expressionTags>
        <li>myrace</li>
        <li>cat_cafe</li>
      </expressionTags>
    </li>
  </modExtensions>
</ThingDef>
```

再让表情集按标签命中：

```xml
<SimManagementLib.SimDef.CustomerExpressionSetDef>
  <defName>SimShopExpression_CatCafeTheme</defName>
  <label>cat cafe customer expressions</label>
  <priority>80</priority>
  <requiredTags>
    <li>cat_cafe</li>
  </requiredTags>
  <excludedTags>
    <li>mechanoid</li>
  </excludedTags>
  <expressions>
    <li>
      <eventId>browse_start</eventId>
      <iconTexPath>CatCafe/Expressions/curious</iconTexPath>
      <chance>0.6</chance>
      <cooldownTicks>180</cooldownTicks>
      <popupScale>1.25</popupScale>
    </li>
  </expressions>
</SimManagementLib.SimDef.CustomerExpressionSetDef>
```

## 5. 贴图资源规则

### 5.1 路径规则

`iconTexPath` 是相对 `Content/Textures/` 的路径，不带 `.png` 后缀。

例如：

```xml
<iconTexPath>MyRace/CustomerExpressions/arrival</iconTexPath>
```

对应文件应放在：

`Content/Textures/MyRace/CustomerExpressions/arrival.png`

### 5.2 加载逻辑

`CustomerExpressionEntry.ResolveTexture()` 会优先尝试从当前模组内容目录直接读 PNG，再回退到 `ContentFinder`。

这意味着：

1. 正常情况下直接放在模组 `Content/Textures/` 下即可。
2. 资源路径最好稳定，不要频繁改名。
3. 如果贴图加载失败，该表情不会显示，但不会让系统崩溃。

## 6. 配置字段说明

`CustomerExpressionSetDef` 关键字段：

- `priority`
  多套配置同时命中时，只使用最高优先级那一层。

- `targetRaceDefs`
  只匹配这些 `ThingDef` 种族。

- `targetPawnKinds`
  只匹配这些 `PawnKindDef`。

- `requiredTags`
  顾客标签必须全部满足。

- `excludedTags`
  顾客标签命中任意一个就排除。

- `expressions`
  本表情集下的全部表情规则。

`CustomerExpressionEntry` 关键字段：

- `eventId`
  触发时机 ID，必须和代码里的事件常量一致。

- `chance`
  本次触发时是否播放的概率，`0~1`。

- `weight`
  同一时机命中多个候选表情时的随机权重。

- `cooldownTicks`
  同一个顾客在同一个 `eventId` 上的冷却。

- `iconTexPath`
  表情贴图路径。

- `iconColor`
  图标染色。

- `popupScale`
  弹窗缩放。

- `requiredContextTags`
  本次事件附带的上下文标签必须满足。

- `excludedContextTags`
  本次事件附带的上下文标签命中任一则不显示。

## 7. 现有可配置表情时机表

下表基于当前代码实现整理，都是已经接入、可以直接配置的 `eventId`。

| eventId | 触发时机 | 代码位置 | 额外上下文标签 | 适合配置的表情方向 |
|---|---|---|---|---|
| `arrival` | 顾客刚生成并到店时 | [CustomerArrivalManager.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimMapComp/CustomerArrivalManager.cs) | 无 | 兴奋、期待、逛街、入店 |
| `browse_start` | 到达货架开始浏览时 | [JobDriver_BrowseAndPick.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_BrowseAndPick.cs) | 无 | 好奇、观察、挑选 |
| `browse_wait` | 顾客等待或暂时找不到合适目标时 | [JobGiver_Customer_BrowseAndPick.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobGiver_Customer_BrowseAndPick.cs) | 无 | 发呆、困惑、犹豫 |
| `browse_no_match` | 当前预算或库存下没有合适商品时 | [JobDriver_BrowseAndPick.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_BrowseAndPick.cs) | 无 | 失望、问号、无奈 |
| `purchase_item` | 成功购买普通商品时 | [JobDriver_BrowseAndPick.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_BrowseAndPick.cs) | 无 | 满意、开心、普通购买反馈 |
| `purchase_preferred_item` | 成功购买偏好商品时 | [JobDriver_BrowseAndPick.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_BrowseAndPick.cs) | `preferred` | 惊喜、爱心眼、强烈喜爱 |
| `purchase_combo` | 成功购买套餐时 | [JobDriver_BrowseAndPick.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_BrowseAndPick.cs) | `combo` | 超值、满足、豪华套餐 |
| `checkout_queue_start` | 到收银台开始排队时 | [JobDriver_PayAtRegister.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_PayAtRegister.cs) | 无 | 准备付款、耐心等待 |
| `checkout_service_start` | 轮到自己开始结账服务时 | [JobDriver_PayAtRegister.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_PayAtRegister.cs) | 无 | 递钱、结账、确认账单 |
| `checkout_paid` | 完成付款后 | [JobDriver_PayAtRegister.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_PayAtRegister.cs) | 无 | 满足、轻松、交易完成 |
| `checkout_timeout` | 排队超时，放弃结账时 | [JobDriver_PayAtRegister.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_PayAtRegister.cs) | 无 | 生气、不耐烦、抱怨 |
| `dine_start` | 堂食行为开始时 | [JobDriver_PostPurchaseDineInShop.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_PostPurchaseDineInShop.cs) | 无 | 开吃、期待、美食前摇 |
| `dine_finish` | 堂食结束时 | [JobDriver_PostPurchaseDineInShop.cs](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Source/SimManagementLib/SimAI/JobDriver_PostPurchaseDineInShop.cs) | 无 | 满足、回味、吃饱 |

## 8. 建议的种族接入策略

### 8.1 如果你是“单一种族模组”

推荐直接用：

- `targetRaceDefs`
- `requiredTags = humanlike`
- 一套完整的 13 个事件表情

这样最稳，也最容易维护。

### 8.2 如果你是“多 PawnKind / 多主题子职业模组”

推荐：

1. 给 `ThingDef` 打基础标签，例如 `myrace`。
2. 给不同 `PawnKindDef` 再打细分标签，例如 `maid`, `noble`, `tourist`。
3. 通用基础表情集用中等 `priority`。
4. 子职业专属表情集用更高 `priority` 覆盖。

这样能形成“基础表情 + 高优先级局部覆盖”的结构。

## 9. 优先级与覆盖建议

为了避免不同模组之间互相抢表情，建议按下面思路分层：

- `10~30`
  通用默认表情。

- `40~70`
  某个种族的专属表情。

- `80~120`
  某个子主题、店铺主题、角色身份的专属表情。

因为当前系统会先筛出最高 `priority` 的表情集，再从该层里选具体表情，所以高优先级表情集最好写得完整一点，至少覆盖你关心的主要事件。

## 10. 最小可用模板

如果你只想先快速跑通一套种族表情，可以从这个最小模板开始：

```xml
<Defs>
  <SimManagementLib.SimDef.CustomerExpressionSetDef>
    <defName>SimShopExpression_MyRaceBasic</defName>
    <label>my race basic expressions</label>
    <priority>50</priority>
    <targetRaceDefs>
      <li>MyRaceThingDef</li>
    </targetRaceDefs>
    <requiredTags>
      <li>humanlike</li>
    </requiredTags>
    <expressions>
      <li>
        <eventId>arrival</eventId>
        <iconTexPath>MyRace/CustomerExpressions/arrival</iconTexPath>
        <chance>0.7</chance>
        <cooldownTicks>300</cooldownTicks>
        <popupScale>1.2</popupScale>
      </li>
      <li>
        <eventId>purchase_item</eventId>
        <iconTexPath>MyRace/CustomerExpressions/purchase</iconTexPath>
        <chance>0.8</chance>
        <cooldownTicks>120</cooldownTicks>
        <popupScale>1.2</popupScale>
      </li>
      <li>
        <eventId>checkout_paid</eventId>
        <iconTexPath>MyRace/CustomerExpressions/paid</iconTexPath>
        <chance>0.85</chance>
        <cooldownTicks>180</cooldownTicks>
        <popupScale>1.2</popupScale>
      </li>
    </expressions>
  </SimManagementLib.SimDef.CustomerExpressionSetDef>
</Defs>
```

## 11. 现有参考文件

可以直接参考这两个已有配置：

- [CustomerExpressions_VanillaEmoji.xml](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Defs/Reusable/CustomerExpressionSetDef/CustomerExpressions_VanillaEmoji.xml)
- [CustomerExpressions_TagTemplate.xml](/E:/RimModDev/RimSimFramework/RimSimManagementFramework/1.6/Defs/Reusable/CustomerExpressionSetDef/CustomerExpressions_TagTemplate.xml)

## 12. 当前系统限制

截至 2026-04-06，这套系统有几个边界要注意：

- 只支持图片表情，不支持直接配文本 emoji。
- 同一时刻只显示一个气泡，新的气泡会销毁旧的气泡。
- `cooldownTicks` 是按 `pawn + eventId` 计算，不区分不同图片候选。
- 多套表情集同时命中时，只取最高优先级层，不会跨优先级混合。
- 目前额外上下文标签只有 `combo` 和 `preferred` 两个是代码里明确传入的。

## 13. 推荐给其他种族作者的交付规范

如果你要给第三方种族作者发接入说明，建议要求他们至少提交这些内容：

1. 种族或 PawnKind 的 DefName。
2. 是否需要额外主题标签。
3. 一套 `Content/Textures/...` 下的 PNG 表情资源。
4. 一份 `CustomerExpressionSetDef` XML。
5. 至少覆盖以下关键事件：
   `arrival`、`browse_start`、`purchase_item`、`checkout_paid`、`checkout_timeout`。

这样即使没有做满全部事件，也能先有一套完整的顾客反馈链路。
