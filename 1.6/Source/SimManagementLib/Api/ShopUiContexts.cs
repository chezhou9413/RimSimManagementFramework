using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimDialog;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供商店 UI 页面共享的绘制上下文，负责承载窗口范围、搜索、滚动、刷新与异常信息。
    /// </summary>
    public class ShopUiContext
    {
        public ShopUiPageDef PageDef { get; internal set; }
        public string CurrentPageDefName { get; internal set; } = "";
        public Rect WindowRect { get; internal set; }
        public string SearchText { get; set; } = "";
        public Vector2 ScrollPosition;
        public bool RefreshRequested { get; private set; }
        public Exception LastException { get; private set; }
        internal Action<string> PageSelector { get; set; }

        /// <summary>
        /// 标记当前页面需要重建缓存，负责让外部 Worker 在下一帧请求刷新。
        /// </summary>
        public void RequestRefresh()
        {
            RefreshRequested = true;
        }

        /// <summary>
        /// 清理刷新标记，负责让窗口在处理完刷新请求后恢复普通绘制状态。
        /// </summary>
        public void ClearRefreshRequest()
        {
            RefreshRequested = false;
        }

        /// <summary>
        /// 记录外部页面异常，负责让 API 查询和窗口错误状态共享同一份信息。
        /// </summary>
        public void RecordException(Exception ex)
        {
            LastException = ex;
        }

        /// <summary>
        /// 切换到指定页面，负责让导航 Worker 不直接依赖具体窗口类型。
        /// </summary>
        public void SelectPage(string defName)
        {
            PageSelector?.Invoke(defName);
        }
    }

    /// <summary>
    /// 提供经商管理主界面的页面上下文，负责暴露地图、商店列表、财务、评价和打开店铺等入口。
    /// </summary>
    public class BusinessManagerUiContext : ShopUiContext
    {
        public MainTabWindow_BusinessManager Window { get; internal set; }
        public Map CurrentMap => Find.CurrentMap;
        public GameComponent_ShopFinanceManager FinanceManager => Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
        public GameComponent_CustomerReviewManager ReviewManager => Current.Game?.GetComponent<GameComponent_CustomerReviewManager>();
        public GameComponent_ShopComboManager ComboManager => Current.Game?.GetComponent<GameComponent_ShopComboManager>();

        /// <summary>
        /// 返回所有可管理商店，负责给外部总览页复用主窗口收集逻辑。
        /// </summary>
        public IReadOnlyList<Zone_Shop> GetAllShops()
        {
            return Window?.ApiGetAllShops() ?? new List<Zone_Shop>();
        }

        /// <summary>
        /// 打开指定商店管理窗口，负责给外部页面提供稳定的跳转入口。
        /// </summary>
        public void OpenShop(Zone_Shop shop)
        {
            if (shop != null)
                Find.WindowStack.Add(new Dialog_ShopManager(shop));
        }

        /// <summary>
        /// 将相机定位到指定商店，负责兼容空区域和跨地图商店。
        /// </summary>
        public void JumpToShop(Zone_Shop shop)
        {
            if (shop?.Map == null) return;
            IntVec3 cell = shop.Cells.Count > 0 ? shop.Cells[0] : shop.Map.Center;
            CameraJumper.TryJump(cell, shop.Map);
        }
    }

    /// <summary>
    /// 提供单店铺管理窗口的页面上下文，负责暴露当前商店、草稿数据、货柜、服务建筑和统一保存入口。
    /// </summary>
    public class ShopManagerUiContext : ShopUiContext
    {
        public Dialog_ShopManager Window { get; internal set; }
        public Zone_Shop Shop => Window?.ApiShopZone;
        public IReadOnlyList<Building_SimContainer> Storages => Window?.ApiStorages ?? new List<Building_SimContainer>();
        public IReadOnlyList<ComboData> Combos => Window?.ApiCombos ?? new List<ComboData>();
        public IReadOnlyList<Thing> ServiceProviders => Window?.ApiServiceProviders ?? new List<Thing>();
        public Dictionary<int, List<ServiceSlotData>> DraftServiceData => Window?.ApiDraftServiceData;
        public ShopScheduleData DraftSchedule => Window?.ApiDraftSchedule;
        public Building_SimContainer SelectedStorage => Window?.ApiSelectedStorage;
        public ComboData CurrentCombo => Window?.ApiCurrentCombo;

        /// <summary>
        /// 保存当前店铺窗口草稿并保持窗口打开，负责让外部表单页复用原窗口保存行为。
        /// </summary>
        public void SaveDrafts()
        {
            Window?.ApiSaveDrafts(closeAfterSave: false);
        }

        /// <summary>
        /// 保存当前店铺窗口草稿并关闭窗口，负责兼容需要提交后退出的外部流程。
        /// </summary>
        public void SaveDraftsAndClose()
        {
            Window?.ApiSaveDraftsAndClose();
        }

        /// <summary>
        /// 关闭当前店铺窗口，负责让外部页面复用取消行为。
        /// </summary>
        public void CancelAndClose()
        {
            Window?.Close();
        }

        /// <summary>
        /// 选择一个套餐并打开套餐编辑页，负责让套餐导航项进入统一页面生命周期。
        /// </summary>
        public void SelectCombo(ComboData combo)
        {
            Window?.ApiSelectCombo(combo);
        }

        /// <summary>
        /// 创建一个新套餐并打开编辑页，负责让新建入口也走导航 Worker。
        /// </summary>
        public void CreateCombo()
        {
            Window?.ApiCreateCombo();
        }
    }
}
