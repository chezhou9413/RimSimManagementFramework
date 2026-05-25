using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 绘制内置商店管理页，负责把 Def 页面适配到原经商管理窗口逻辑。
    /// </summary>
    public class BusinessPageWorker_ShopManagement : BusinessManagerPageWorker
    {
        /// <summary>
        /// 绘制商店管理页主体。
        /// </summary>
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            context?.Window?.ApiDrawShopManagementPage(rect);
        }
    }

    /// <summary>
    /// 绘制内置售货机页，负责把 Def 页面适配到原经商管理窗口逻辑。
    /// </summary>
    public class BusinessPageWorker_Vending : BusinessManagerPageWorker
    {
        /// <summary>
        /// 绘制售货机页主体。
        /// </summary>
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            context?.Window?.ApiDrawVendingMachinePage(rect);
        }
    }

    /// <summary>
    /// 绘制内置财务页，负责把 Def 页面适配到原经商管理窗口逻辑。
    /// </summary>
    public class BusinessPageWorker_Finance : BusinessManagerPageWorker
    {
        /// <summary>
        /// 绘制财务页主体。
        /// </summary>
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            context?.Window?.ApiDrawFinancePage(rect);
        }
    }

    /// <summary>
    /// 绘制内置评价页，负责在 AI 配置可用时显示顾客评价页面。
    /// </summary>
    public class BusinessPageWorker_Reviews : BusinessManagerPageWorker
    {
        /// <summary>
        /// 判断评价页是否具备显示条件。
        /// </summary>
        public override bool CanShow(ShopUiContext context)
        {
            return base.CanShow(context) && SimManagementLibMod.Settings?.HasValidReviewAiConfig() == true;
        }

        /// <summary>
        /// 绘制评价页主体。
        /// </summary>
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            context?.Window?.ApiDrawCustomerReviewsPage(rect);
        }
    }

    /// <summary>
    /// 绘制内置收藏品兑换页，负责把 Def 页面适配到原经商管理窗口逻辑。
    /// </summary>
    public class BusinessPageWorker_CollectibleExchange : BusinessManagerPageWorker
    {
        /// <summary>
        /// 绘制收藏品兑换页主体。
        /// </summary>
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            context?.Window?.ApiDrawCollectibleExchangePage(rect);
        }
    }

    /// <summary>
    /// 绘制内置蓝图页，负责把 Def 页面适配到原经商管理窗口逻辑。
    /// </summary>
    public class BusinessPageWorker_Blueprints : BusinessManagerPageWorker
    {
        /// <summary>
        /// 绘制蓝图页主体。
        /// </summary>
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            context?.Window?.ApiDrawBlueprintPage(rect);
        }
    }

    /// <summary>
    /// 绘制内置顾客页，负责把 Def 页面适配到原经商管理窗口逻辑。
    /// </summary>
    public class BusinessPageWorker_Customers : BusinessManagerPageWorker
    {
        /// <summary>
        /// 绘制顾客页主体。
        /// </summary>
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            context?.Window?.ApiDrawCustomerPage(rect);
        }
    }

    /// <summary>
    /// 绘制内置员工页，负责把 Def 页面适配到原经商管理窗口逻辑。
    /// </summary>
    public class BusinessPageWorker_Staff : BusinessManagerPageWorker
    {
        /// <summary>
        /// 绘制员工页主体。
        /// </summary>
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            context?.Window?.ApiDrawStaffPage(rect);
        }
    }

    /// <summary>
    /// 绘制店铺概览页，负责把 Def 页面适配到原店铺管理窗口逻辑。
    /// </summary>
    public class ShopPageWorker_Overview : ShopManagerPageWorker
    {
        /// <summary>
        /// 判断概览页是否显示保存按钮，概览页只读所以隐藏保存入口。
        /// </summary>
        public override bool ShowSaveButton(ShopUiContext context)
        {
            return false;
        }

        /// <summary>
        /// 绘制店铺概览主体。
        /// </summary>
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            context?.Window?.ApiDrawOverviewPanel(rect);
        }
    }

    /// <summary>
    /// 绘制店铺营业时间页，负责把 Def 页面适配到原店铺管理窗口逻辑。
    /// </summary>
    public class ShopPageWorker_BusinessHours : ShopManagerPageWorker
    {
        /// <summary>
        /// 返回营业时间页保存提示，负责说明当前页存在排班草稿。
        /// </summary>
        public override string GetSaveTip(ShopUiContext context)
        {
            return SimTranslation.T("RSMF.ShopManager.SaveTip.Schedule");
        }

        /// <summary>
        /// 绘制营业时间主体。
        /// </summary>
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            context?.Window?.ApiDrawBusinessHoursPanel(rect);
        }
    }

    /// <summary>
    /// 绘制店铺服务页，负责把 Def 页面适配到原店铺管理窗口逻辑。
    /// </summary>
    public class ShopPageWorker_Services : ShopManagerPageWorker
    {
        /// <summary>
        /// 返回服务页保存提示，负责说明服务槽位修改需要提交。
        /// </summary>
        public override string GetSaveTip(ShopUiContext context)
        {
            return SimTranslation.T("RSMF.ShopManager.SaveTip.Services");
        }

        /// <summary>
        /// 绘制服务管理主体。
        /// </summary>
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            context?.Window?.ApiDrawServicesPanel(rect);
        }
    }

    /// <summary>
    /// 绘制店铺套餐页，负责把 Def 页面适配到原店铺管理窗口逻辑。
    /// </summary>
    public class ShopPageWorker_ComboEdit : ShopManagerPageWorker
    {
        /// <summary>
        /// 判断套餐页是否显示保存按钮，套餐编辑直接写入草稿集合所以隐藏底部保存入口。
        /// </summary>
        public override bool ShowSaveButton(ShopUiContext context)
        {
            return false;
        }

        /// <summary>
        /// 构建套餐导航项，负责把套餐列表和新建套餐入口纳入 Def Worker 驱动。
        /// </summary>
        public override IEnumerable<ShopUiNavigationItem> BuildNavigationItems(ShopUiContext context)
        {
            if (!(context is ShopManagerUiContext shopContext) || def == null)
                yield break;

            int comboCount = shopContext.Combos?.Count ?? 0;
            yield return new ShopUiNavigationItem
            {
                pageDef = def,
                id = def.defName + ".New",
                label = SimTranslation.T("RSMF.ShopManager.NewCombo"),
                order = def.order,
                startsNewGroup = true,
                selected = c => false,
                activate = c => (c as ShopManagerUiContext)?.CreateCombo()
            };

            if (shopContext.Combos == null)
                yield break;

            for (int i = 0; i < shopContext.Combos.Count; i++)
            {
                ComboData combo = shopContext.Combos[i];
                if (combo == null) continue;
                ComboData localCombo = combo;
                int index = i;
                yield return new ShopUiNavigationItem
                {
                    pageDef = def,
                    id = def.defName + ".Combo." + index,
                    label = string.IsNullOrEmpty(localCombo.comboName) ? SimTranslation.T("RSMF.Common.UnnamedCombo") : localCombo.comboName,
                    order = def.order + 1 + index,
                    selected = c => c is ShopManagerUiContext typed && typed.CurrentPageDefName == def.defName && typed.CurrentCombo == localCombo,
                    activate = c => (c as ShopManagerUiContext)?.SelectCombo(localCombo)
                };
            }
        }

        /// <summary>
        /// 绘制套餐编辑主体。
        /// </summary>
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            context?.Window?.ApiDrawComboPanel(rect);
        }
    }
}
