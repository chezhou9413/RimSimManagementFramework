# 餐厅扩展多语言开发

餐厅扩展使用原版语言加载机制，运行时复用框架的 `SimTranslation.T`。语言文件位于 `1.6/Languages`，已提供 `ChineseSimplified` 和 `English`。

## 翻译目录

- `Keyed/RSR_UI.xml`：菜单、库存、传送带配置、经营总览和操作提示。
- `Keyed/RSR_State.xml`：用餐及订单状态。
- `Keyed/RSR_Issue.xml`：经营阻塞、订单失败、用餐结束和取料原因。
- `Keyed/RSR_Building.xml`：建筑检查面板与传送带配置命令。
- `Keyed/RSR_Conveyor.xml`：线路变化与放置提示。
- `Keyed/RSR_Food.xml`：食品分类及默认菜名。
- `Keyed/RSR_Preference.xml`：顾客偏好与选菜摘要。
- `Keyed/RSR_Review.xml`：提交给框架评价系统的餐厅体验摘要。
- `DefInjected/<Def类型>/RSR_<Def类型>.xml`：建筑、工作、岗位、页面、商品分类、顾客动作和绘制路径的 Def 字段。

添加语言时，将 `English` 目录复制为原版识别的语言目录名称，再翻译节点内容。保留节点名称以及 `{count}`、`{reason}` 等命名参数，允许调整参数在句子中的位置。换行使用 `\n`；文件保存为 UTF-8。

## C# 调用

```csharp
using SimManagementLib.Tool;
using Verse;

//返回完整的食品数量提示，职责是允许译文自行决定数量的位置。
private static string FoodCount(int count)
{
    return SimTranslation.T("RSR.UI.AvailableFoods", count.Named("count"));
}
```

语言文件对应内容：

```xml
<RSR.UI.AvailableFoods>可选食品 {count} 项</RSR.UI.AvailableFoods>
```

使用完整句子模板，避免把“前缀 + 数字 + 后缀”分别翻译。新文本键采用 `RSR.功能.语义名称`；其他扩展作者应使用自己的唯一前缀。

Def 自带的 `label`、`description`、`reportString`、`verb`、`gerund` 使用 DefInjected。键名是 `defName.字段名`，不把这些字段改成运行时翻译键。原 Def 保留中文源文本，中英文通过语言目录注入。

## 数据与界面约定

- 用枚举、布尔值或空阻塞原因判断业务状态，不比较翻译后的文本。
- 菜单和经营状态的短期缓存会随活动语言变化清空。
- 长按钮、表头和单行说明使用框架的截断与完整悬浮提示；餐厅开关使用 `RestaurantUiStyle.DrawCheckbox`。
- 玩家编辑的菜名属于存档数据。默认菜名在创建菜单时翻译，已经保存的菜名及历史订单摘要不会在切换语言时重写。
- 开发者生成餐厅工具、异常、配置错误、纯日志和渲染内部名称保留开发文本，不属于玩家经营界面的翻译范围。

## 静态检查

在仓库根目录执行：

```powershell
python RimSimRestaurantExtension/Tools/Localization/audit_localization.py
```

检查中英文键集合、重复键、空文本、XML、调用参数和 Def 字段覆盖。`--report <路径>` 可输出 UTF-8 JSON 明细。新增语言的译者需同时参考英文与中文参数，不删除占位符。

本次迁移覆盖 341 个运行时键和 68 个 Def 字段。扫描 129 个 C# 文件，保留的中文候选为：开发者生成工具 35 行、日志及异常配置诊断 19 行、仅写日志的补餐失败原因 11 行、渲染内部名称或日志 7 行。经营界面未发现未分类的中文硬编码文本。
