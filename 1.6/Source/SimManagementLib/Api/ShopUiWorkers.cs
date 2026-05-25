using SimManagementLib.SimDef;
using SimManagementLib.SimDialog;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供所有商店 UI 页面的基础生命周期，负责让外部页面安全挂载、绘制、保存、取消和刷新。
    /// </summary>
    public class ShopUiPageWorker
    {
        public ShopUiPageDef def;

        /// <summary>
        /// 判断页面是否应显示，默认使用 Def 可见性。
        /// </summary>
        public virtual bool CanShow(ShopUiContext context)
        {
            return def == null || def.defaultVisible;
        }

        /// <summary>
        /// 在页面被选中时接收通知，默认不执行额外逻辑。
        /// </summary>
        public virtual void OnOpen(ShopUiContext context)
        {
        }

        /// <summary>
        /// 绘制页签标签，默认显示 Def 的翻译标签。
        /// </summary>
        public virtual void DrawLabel(Rect rect, ShopUiContext context, bool selected)
        {
            string label = def?.DisplayLabel ?? "UI";
            SimUiStyle.DrawTabButton(rect, label, selected, new Color(0.72f, 0.72f, 0.72f, 1f));
        }

        /// <summary>
        /// 构建侧栏导航项，默认把页面自身作为一个可点击入口。
        /// </summary>
        public virtual IEnumerable<ShopUiNavigationItem> BuildNavigationItems(ShopUiContext context)
        {
            if (def == null || !def.showInNavigation)
                yield break;

            yield return new ShopUiNavigationItem
            {
                pageDef = def,
                id = def.defName,
                label = def.DisplayLabel,
                order = def.order,
                selected = c => c?.CurrentPageDefName == def.defName,
                activate = c => c?.SelectPage(def.defName)
            };
        }

        /// <summary>
        /// 绘制页面主体，默认显示空状态以避免外部 Def 误配置时报错。
        /// </summary>
        public virtual void DrawPage(Rect rect, ShopUiContext context)
        {
            Widgets.NoneLabel(rect.center.y, rect.width, def?.DisplayLabel ?? "UI");
        }

        /// <summary>
        /// 判断窗口底部是否需要显示保存按钮，默认让外部页面参与统一保存流程。
        /// </summary>
        public virtual bool ShowSaveButton(ShopUiContext context)
        {
            return true;
        }

        /// <summary>
        /// 返回底部保存按钮文本，负责让页面按自己的语义显示操作名称。
        /// </summary>
        public virtual string GetSaveButtonLabel(ShopUiContext context)
        {
            return SimTranslation.TOrFallback("RSMF.ShopManager.Save", "保存");
        }

        /// <summary>
        /// 返回底部提示文本，负责解释当前页面保存行为。
        /// </summary>
        public virtual string GetSaveTip(ShopUiContext context)
        {
            return SimTranslation.TOrFallback("RSMF.ShopManager.SaveTip", "");
        }

        /// <summary>
        /// 保存页面草稿，默认不执行额外逻辑。
        /// </summary>
        public virtual void OnSave(ShopUiContext context)
        {
        }

        /// <summary>
        /// 丢弃页面草稿，默认不执行额外逻辑。
        /// </summary>
        public virtual void OnCancel(ShopUiContext context)
        {
        }

        /// <summary>
        /// 刷新页面缓存，默认不执行额外逻辑。
        /// </summary>
        public virtual void OnRefresh(ShopUiContext context)
        {
        }
    }

    /// <summary>
    /// 提供经商管理页面的专用基类，负责把通用上下文转换为主界面上下文。
    /// </summary>
    public abstract class BusinessManagerPageWorker : ShopUiPageWorker
    {
        /// <summary>
        /// 绘制经商管理页面，负责为继承类提供强类型上下文。
        /// </summary>
        public abstract void DrawBusinessPage(Rect rect, BusinessManagerUiContext context);

        /// <summary>
        /// 绘制页面主体，负责安全忽略错误上下文类型。
        /// </summary>
        public override void DrawPage(Rect rect, ShopUiContext context)
        {
            if (context is BusinessManagerUiContext businessContext)
                DrawBusinessPage(rect, businessContext);
        }
    }

    /// <summary>
    /// 提供单店铺管理页面的专用基类，负责把通用上下文转换为店铺窗口上下文。
    /// </summary>
    public abstract class ShopManagerPageWorker : ShopUiPageWorker
    {
        /// <summary>
        /// 绘制店铺管理页面，负责为继承类提供强类型上下文。
        /// </summary>
        public abstract void DrawShopPage(Rect rect, ShopManagerUiContext context);

        /// <summary>
        /// 绘制页面主体，负责安全忽略错误上下文类型。
        /// </summary>
        public override void DrawPage(Rect rect, ShopUiContext context)
        {
            if (context is ShopManagerUiContext shopContext)
                DrawShopPage(rect, shopContext);
        }
    }

    /// <summary>
    /// 提供列表型页面模板，负责封装滚动区、空状态和逐行绘制流程。
    /// </summary>
    public abstract class ShopUiListPageWorker<T> : ShopUiPageWorker
    {
        /// <summary>
        /// 构建当前页面需要显示的行数据。
        /// </summary>
        protected abstract IEnumerable<T> BuildRows(ShopUiContext context);

        /// <summary>
        /// 返回单行高度，默认使用适合中文小字号的安全高度。
        /// </summary>
        protected virtual float GetRowHeight(T row, ShopUiContext context)
        {
            return 48f;
        }

        /// <summary>
        /// 绘制单行内容，由继承类实现具体列和按钮。
        /// </summary>
        protected abstract void DrawRow(Rect rect, T row, int index, ShopUiContext context);

        /// <summary>
        /// 绘制列表页面，负责处理滚动区域高度和空列表提示。
        /// </summary>
        public override void DrawPage(Rect rect, ShopUiContext context)
        {
            List<T> rows = new List<T>(BuildRows(context) ?? new List<T>());
            if (rows.Count == 0)
            {
                ShopUiLayoutUtility.DrawEmptyState(rect, SimTranslation.TOrFallback("RSMF.ShopUi.Empty.NoRows", "No items to display."));
                return;
            }

            float viewWidth = Mathf.Max(1f, rect.width - 18f);
            float viewHeight = 0f;
            for (int i = 0; i < rows.Count; i++)
                viewHeight += Mathf.Max(Text.LineHeightOf(GameFont.Small) + 12f, GetRowHeight(rows[i], context));

            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height + 1f, viewHeight));
            Widgets.BeginScrollView(rect, ref context.ScrollPosition, viewRect);
            float y = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                float rowHeight = Mathf.Max(Text.LineHeightOf(GameFont.Small) + 12f, GetRowHeight(rows[i], context));
                DrawRow(new Rect(0f, y, viewRect.width, rowHeight), rows[i], i, context);
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }
    }

    /// <summary>
    /// 提供表单草稿页面模板，负责统一创建、应用和丢弃配置草稿。
    /// </summary>
    public abstract class ShopUiFormPageWorker<TDraft> : ShopUiPageWorker
    {
        private TDraft draft;

        /// <summary>
        /// 创建页面草稿，负责从上下文读取当前配置。
        /// </summary>
        protected abstract TDraft CreateDraft(ShopUiContext context);

        /// <summary>
        /// 绘制页面草稿，负责给继承类实现具体表单。
        /// </summary>
        protected abstract void DrawDraft(Rect rect, TDraft draft, ShopUiContext context);

        /// <summary>
        /// 应用页面草稿，负责把表单结果写回业务对象。
        /// </summary>
        protected abstract void ApplyDraft(TDraft draft, ShopUiContext context);

        /// <summary>
        /// 丢弃页面草稿，负责清理临时引用。
        /// </summary>
        protected virtual void DiscardDraft(TDraft draft, ShopUiContext context)
        {
        }

        /// <summary>
        /// 页面打开时创建草稿，负责让表单从稳定状态开始编辑。
        /// </summary>
        public override void OnOpen(ShopUiContext context)
        {
            draft = CreateDraft(context);
        }

        /// <summary>
        /// 绘制表单页面，负责按需补建草稿。
        /// </summary>
        public override void DrawPage(Rect rect, ShopUiContext context)
        {
            if (draft == null)
                draft = CreateDraft(context);
            DrawDraft(rect, draft, context);
        }

        /// <summary>
        /// 保存草稿，负责把继承类的应用逻辑接入窗口保存流程。
        /// </summary>
        public override void OnSave(ShopUiContext context)
        {
            if (draft != null)
                ApplyDraft(draft, context);
        }

        /// <summary>
        /// 取消草稿，负责把继承类的清理逻辑接入窗口取消流程。
        /// </summary>
        public override void OnCancel(ShopUiContext context)
        {
            if (draft != null)
                DiscardDraft(draft, context);
            draft = default(TDraft);
        }
    }

    /// <summary>
    /// 提供 UI 命令按钮的基础逻辑，负责让外部开发者封装可复用动作。
    /// </summary>
    public abstract class ShopUiActionWorker
    {
        /// <summary>
        /// 判断命令是否可执行。
        /// </summary>
        public virtual bool CanExecute(ShopUiContext context)
        {
            return true;
        }

        /// <summary>
        /// 执行命令。
        /// </summary>
        public abstract void Execute(ShopUiContext context);
    }

    /// <summary>
    /// 提供可复用 UI 数据源的基础逻辑，负责让列表页与数据收集解耦。
    /// </summary>
    public abstract class ShopUiDataProvider<T>
    {
        /// <summary>
        /// 返回上下文对应的数据集合。
        /// </summary>
        public abstract IEnumerable<T> GetItems(ShopUiContext context);
    }

    /// <summary>
    /// 描述侧栏中的一个可点击导航入口，负责让页面 Worker 声明静态或运行时入口。
    /// </summary>
    public class ShopUiNavigationItem
    {
        public ShopUiPageDef pageDef;
        public string id = "";
        public string label = "";
        public int order;
        public bool startsNewGroup;
        public Func<ShopUiContext, bool> selected;
        public Action<ShopUiContext> activate;

        /// <summary>
        /// 判断当前上下文下该导航项是否被选中。
        /// </summary>
        public bool IsSelected(ShopUiContext context)
        {
            return selected?.Invoke(context) == true;
        }

        /// <summary>
        /// 执行导航项点击动作。
        /// </summary>
        public void Activate(ShopUiContext context)
        {
            activate?.Invoke(context);
        }
    }
}
