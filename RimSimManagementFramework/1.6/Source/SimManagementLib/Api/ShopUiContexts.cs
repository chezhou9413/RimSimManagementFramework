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
    //提供商店 UI 页面共享的绘制上下文，负责承载窗口范围、搜索、滚动、刷新与异常信息。
    public class ShopUiContext
    {
        private readonly Dictionary<string, object> pageStates = new Dictionary<string, object>();

        //读取窗口内页面私有状态，职责是隔离共享 Worker 并在切页时保留草稿。
        public T GetOrCreatePageState<T>(string key, Func<T> factory) where T : class
        {
            if (!pageStates.TryGetValue(key, out object state))
                pageStates[key] = state = factory();
            return (T)state;
        }

        //查询已经打开页面的状态，职责是避免保存未访问页面时创建草稿。
        public T GetPageState<T>(string key) where T : class
        {
            return pageStates.TryGetValue(key, out object state) ? state as T : null;
        }

        //释放窗口页面状态，职责是在任何关闭路径丢弃未提交数据。
        internal void ClearPageStates()
        {
            pageStates.Clear();
        }

        public ShopUiPageDef PageDef { get; internal set; }
        public string CurrentPageDefName { get; internal set; } = "";
        public Rect WindowRect { get; internal set; }
        public string SearchText { get; set; } = "";
        public Vector2 ScrollPosition;
        public bool RefreshRequested { get; private set; }
        public Exception LastException { get; private set; }
        internal Action<string> PageSelector { get; set; }

        //标记当前页面需要重建缓存，负责让外部 Worker 在下一帧请求刷新。
        public void RequestRefresh()
        {
            RefreshRequested = true;
        }

        //清理刷新标记，负责让窗口在处理完刷新请求后恢复普通绘制状态。
        public void ClearRefreshRequest()
        {
            RefreshRequested = false;
        }

        //记录外部页面异常，负责让 API 查询和窗口错误状态共享同一份信息。
        public void RecordException(Exception ex)
        {
            LastException = ex;
        }

        //切换到指定页面，负责让导航 Worker 不直接依赖具体窗口类型。
        public void SelectPage(string defName)
        {
            PageSelector?.Invoke(defName);
        }
    }

    //提供经商管理主界面的页面上下文，负责暴露地图、商店列表、财务、评价和打开店铺等入口。
    public class BusinessManagerUiContext : ShopUiContext
    {
        public MainTabWindow_BusinessManager Window { get; internal set; }
        public Map CurrentMap => Find.CurrentMap;
        public GameComponent_ShopFinanceManager FinanceManager => Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
        public GameComponent_CustomerReviewManager ReviewManager => Current.Game?.GetComponent<GameComponent_CustomerReviewManager>();
        public GameComponent_ShopComboManager ComboManager => Current.Game?.GetComponent<GameComponent_ShopComboManager>();

        //返回所有可管理商店，负责给外部总览页复用主窗口收集逻辑。
        public IReadOnlyList<Zone_Shop> GetAllShops()
        {
            return Window?.ApiGetAllShops() ?? new List<Zone_Shop>();
        }

        //打开指定商店管理窗口，负责给外部页面提供稳定的跳转入口。
        public void OpenShop(Zone_Shop shop)
        {
            if (shop != null)
                Find.WindowStack.Add(new Dialog_ShopManager(shop));
        }

        //将相机定位到指定商店，负责兼容空区域和跨地图商店。
        public void JumpToShop(Zone_Shop shop)
        {
            if (shop?.Map == null) return;
            IntVec3 cell = shop.Cells.Count > 0 ? shop.Cells[0] : shop.Map.Center;
            CameraJumper.TryJump(cell, shop.Map);
        }
    }

    //提供单店铺管理窗口的页面上下文，负责暴露当前商店、草稿数据、货柜、服务建筑和统一保存入口。
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

        //保存当前店铺窗口草稿并保持窗口打开，负责让外部表单页复用原窗口保存行为。
        public void SaveDrafts()
        {
            Window?.ApiSaveDrafts(closeAfterSave: false);
        }

        //保存当前店铺窗口草稿并关闭窗口，负责兼容需要提交后退出的外部流程。
        public void SaveDraftsAndClose()
        {
            Window?.ApiSaveDraftsAndClose();
        }

        //关闭当前店铺窗口，负责让外部页面复用取消行为。
        public void CancelAndClose()
        {
            Window?.Close();
        }

        //选择一个套餐并打开套餐编辑页，负责让套餐导航项进入统一页面生命周期。
        public void SelectCombo(ComboData combo)
        {
            Window?.ApiSelectCombo(combo);
        }

        //创建一个新套餐并打开编辑页，负责让新建入口也走导航 Worker。
        public void CreateCombo()
        {
            Window?.ApiCreateCombo();
        }
    }
}
