# 框架多语言接入

框架界面使用 RimWorld 原生 `Keyed` 语言系统，公共入口为 `SimManagementLib.Tool.SimTranslation.T`。翻译作者只需提供 XML 语言包；扩展作者应使用自己模组的键前缀，避免与 `RSMF.*` 冲突。

## 添加一种语言

在模组的 `1.6/Languages/<语言目录名>/Keyed/` 下创建 UTF-8 XML 文件，参照 `English/Keyed` 中的键翻译。保留键名，只修改元素内的文字。独立汉化模组也可以按 RimWorld 的语言包加载方式提供这些键。

```xml
<?xml version="1.0" encoding="utf-8"?>
<LanguageData>
  <RSMF.PageManager.Title>Manage pages</RSMF.PageManager.Title>
  <RSMF.PageManager.Summary>{visible} / {total} pages visible. Use Up and Down to reorder tabs.</RSMF.PageManager.Summary>
</LanguageData>
```

同一语言中一个键只能定义一次。XML 文件名用于组织内容，不影响键查找。本次补充按职责组织为：

| 文件 | 内容 |
| --- | --- |
| `RSMF_Interface.xml` | 页面管理、套餐导入导出、搜索、品质货柜、服务、网络提示和调试日志设置 |
| `RSMF_ApiMessages.xml` | 公共 API 可向调用方返回的失败消息 |
| `RSMF_RestockInterface.xml` | 补货面板、队列摘要、任务行和等待原因 |
| `RSMF_TutorialSupplement.xml` | 补齐既有教程键；各语言文件条目数可以不同，但全部文件合并后的键集合一致 |

原有 `RSMF_Common.xml`、`RSMF_UI.xml` 等文件继续有效。翻译全部界面时应以整个 `Keyed` 目录为清单。

## 扩展代码使用方式

```csharp
using SimManagementLib.Tool;
using Verse;

//生成库存摘要，职责是把动态数量交给语言模板排列。
private static string BuildStockLabel(int count, int capacity)
{
    return SimTranslation.T("MyMod.Stock.Summary",
        count.Named("count"), capacity.Named("capacity"));
}
```

对应语言条目：

```xml
<MyMod.Stock.Summary>库存：{count} / {capacity}</MyMod.Stock.Summary>
```

- 使用完整句子模板和命名参数，允许其他语言改变语序。不要分别拼接“数量”“个”等语法片段。
- 保留 `{count}` 等参数名和富文本标签；换行使用 `\n`，XML 中的 `&`、`<`、`>` 按 XML 规则转义。
- 有固定格式要求时，在 C# 中把数字格式化后作为参数传入，例如 `price.ToString("F1").Named("price")`。
- 在绘制、查询标签或执行操作时翻译，避免在静态字段初始化时保存已翻译文字。缓存显示文字时应同时检查当前语言。
- `TOrFallback` 保留给语言尚未初始化或扩展允许无键直写的入口。正常界面优先使用 `T`，并为键提供中英文条目。
- 可选参数必须是编译期常量，不能把 `T(...)` 写在参数默认值中。默认提示可使用 `null`，进入函数后再取翻译。
- `SimApiResult.failReason` 等失败文字供显示使用，不作为程序判断依据；不要通过比较译文决定行为。

## Def 和注册页面

普通 Def 的 `label`、`description` 使用 RimWorld `DefInjected`，不要把 `defName`、类名、存档字段名、JSON 字段名翻译掉。

`ShopUiPageDef` 可使用 `labelKey`、`descriptionKey`；`BusinessTutorialDef` 可使用 `titleKey`、`textBeforeImageKey`、`textAfterImageKey`。这些键同样放在 `Keyed` 中。保留原始字段作为无键配置时的文本，不需要改动页面 Worker。

玩家输入的店名、套餐名、公告正文以及已经生成的评价属于内容，不会因为切换语言而被重新翻译。模型提示、诊断日志和协议匹配文字也不能直接进行全文替换。

## 布局和维护

公共搜索栏按译文测量标签宽度；公共按钮和页签会省略超长文字，并通过悬停提示提供完整文本。长正文仍应使用 `Text.CalcHeight` 计算高度。补货概要卡片已按当前语言的实际多行高度布局。

在模组根目录执行：

```powershell
python -X utf8 Tools/Localization/audit_localization.py
```

检查 XML 是否有效、同语言是否有重复键、中英文键集合和命名参数是否一致，以及源码和 Def 中的明确键引用是否存在。动态拼接键和英文标识需要人工确认。

需要完整候选清单时，可指定 `--report <输出路径.json>`。中文字符串扫描是启发式统计，包含日志、模型提示和内部状态，不代表这些候选都是尚未处理的 UI。
