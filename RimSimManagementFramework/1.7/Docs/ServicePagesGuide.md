# 商店管理的服务子页（1.7）

服务配置统一进入单店窗口的“服务管理”，由子页签切换。窗口按 `CanShow` 显示当前店铺具备的服务；没有任何可见服务时隐藏总入口。经商管理主界面的统计页面另行归入“运营总览”，与单店配置分开。

## XML 接入

继续使用 `ShopUiPageDef`，增加 `isServicePage`，不另建顶层服务入口：

```xml
<SimManagementLib.SimDef.ShopUiPageDef>
  <defName>MyMod_ShopServiceSettings</defName>
  <label>My service</label>
  <labelKey>MyMod.UI.ServiceLabel</labelKey>
  <scope>ShopManager</scope>
  <isServicePage>true</isServicePage>
  <order>480</order>
  <workerClass>MyMod.UI.ServiceSettingsPage</workerClass>
</SimManagementLib.SimDef.ShopUiPageDef>
```

`order` 决定子页顺序；`defaultVisible`、`showInNavigation`、`requiredModPackageIds` 继续生效。`isServicePage` 仅作用于单店窗口。没有该标记的普通页面保持原导航方式。

代码注册使用 `SimShopUiApi.RegisterRuntimeServicePage(defName, label, order, workerClass)`；返回的 Def 可继续设置翻译键及依赖。

## 显示条件

Worker 继承 `ShopManagerPageWorker`，覆盖 `CanShow(ShopUiContext)`，先检查 `base.CanShow(context)`，再按 `ShopManagerUiContext.Shop` 内的实际设施判断。查询不得创建订单、登记床位或扣款。

配置入口检查设施是否存在，不以“正在营业、通电、有空位或有顾客”作为必要条件，避免停业后无法重新开启。首次设置所需的入口也不能要求配置已经完成，例如出租床登记页必须允许尚未登记的床。

| 子页 | 当前显示条件 |
| --- | --- |
| 通用服务 | 店内有通用服务提供设施 |
| 餐厅 | 店内有餐厅接待柜台 |
| 客房、住客 | 店内有旅馆服务台和旅馆床，或仍有未结束的住宿合同 |
| 温泉 | 店内有温泉 |
| 取钱机 | 店内有取钱机 |

窗口约每秒刷新设施与页面可见性，暂停游戏时也会刷新。需要立即刷新时调用 `context.RequestRefresh()` 或 `SimShopUiApi.RequestRefresh()`；显式刷新会通知可见页面的 `OnRefresh`。

## 草稿与生命周期

服务首页采用紧凑摘要、操作按钮和可排序列表；不要将全部参数表单与每条记录的明细依次铺满首页。通过“编辑”“详情”等小按钮打开二级窗口，编辑窗口复制父草稿字段，确认才回写父草稿，取消或关闭不回写。真正的经营设置只在父窗口 `OnSave` 中提交。纯只读明细和定位操作无需保存。

页签设置 `labelKey` 并提供中英文 Keyed 文本，避免页面名称依赖提前缓存的 Def 标签。列表和二级窗口继续使用框架 `ShopUiVisualUtility` 控件，按实际字体测量行高，为滚动条、关闭按钮和确认栏分别预留空间；长单行文本省略时保留完整悬停提示。

- Worker 由 Def 共享，窗口草稿放进 `context.GetOrCreatePageState`，不要保存在 Worker 实例字段中。`OnSave` 使用 `GetPageState`，未打开过的页面不创建草稿。
- 服务子页在每个窗口首次进入时调用 `OnOpen`。切换子页不会重新调用它，搜索与通用滚动位置分别保留。
- 顶层入口记住上次选择的服务。子页签最多展示两行，更多项可在页签栏中滚动；长标签可悬停查看。
- `context.PageDef` 和 `CurrentPageDefName` 仍标识实际子页；`context.SelectPage(原页面 defName)` 可直接定位该服务。
- “保存全部设置”调用窗口各页的 `OnSave`，包含已访问后暂时隐藏的服务页。显示住客等只读子页时也保留统一保存按钮，便于提交其他子页草稿。
- 取消或关闭调用一次 `OnCancel`，随后释放窗口状态；成功保存并关闭不再调用 `OnCancel`。保存失败保留窗口，已成功页面不会自动回滚。
- 暂时隐藏不销毁草稿。`OnSave` 应自行校验设施是否仍有效；`OnRefresh` 不应覆盖尚未提交的草稿。
- 子页直接使用框架传入的内容矩形，搜索栏、导航栏和底部保存区域已由框架预留，不重复绘制。

## 经营主窗口的运营总览

原“商店管理”“餐厅运营”“旅馆运营”合并为一个“运营总览”入口。内部的“全部店铺”复用原店铺列表和管理、定位入口；餐厅、旅馆保留各自统计与明细。总览按所有已加载地图上的实际 `Zone_Shop` 收集店铺，不要求地图标记为玩家家园，调试图和临时地图上的店铺也会列出。

扩展运营页继续继承 `BusinessManagerPageWorker`，将 `ShopUiPageDef.scope` 设为 `BusinessManager` 并增加 `<isOperationsPage>true</isOperationsPage>`。不再另占顶层页签，`order` 决定内部分类顺序，`labelKey` 提供简短的中英文分类名称。代码注册也可在 `RegisterRuntimePage` 返回的 Def 上设置该标记。

`CanShow` 检查实际设施或尚未结束的业务，不依赖搜索词、营业状态、员工到岗或剩余空位。无对应业务的分类隐藏；设施变化约一秒内刷新，当前分类消失时返回“全部店铺”。各分类分别保留搜索词和窗口状态，绘制与打开回调仍以实际子页作为 `context.PageDef`；表单编辑继续通过 `context.OpenShop` 进入单店窗口。
