using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager
    {
        /// <summary>
        /// 返回当前管理的商店区域，负责给 UI API 上下文提供稳定入口。
        /// </summary>
        public Zone_Shop ApiShopZone => shopZone;

        /// <summary>
        /// 返回当前店铺货柜列表，负责给外部页面读取运行时货柜状态。
        /// </summary>
        public IReadOnlyList<Building_SimContainer> ApiStorages => storages;

        /// <summary>
        /// 返回当前店铺套餐列表，负责给外部页面读取套餐草稿状态。
        /// </summary>
        public IReadOnlyList<ComboData> ApiCombos => zoneCombos;

        /// <summary>
        /// 返回当前店铺服务建筑列表，负责给外部页面读取服务配置目标。
        /// </summary>
        public IReadOnlyList<Thing> ApiServiceProviders => serviceProviders;

        /// <summary>
        /// 返回服务配置草稿，负责给外部页面读取或调整服务槽位。
        /// </summary>
        public Dictionary<int, List<ServiceSlotData>> ApiDraftServiceData => draftServiceData;

        /// <summary>
        /// 返回营业时间草稿，负责给外部页面读取或调整排班配置。
        /// </summary>
        public ShopScheduleData ApiDraftSchedule => draftSchedule;

        /// <summary>
        /// 返回当前选中的货柜，负责给外部页面复用侧栏选择。
        /// </summary>
        public Building_SimContainer ApiSelectedStorage => GetSelectedStorage();

        /// <summary>
        /// 返回当前正在编辑的套餐，负责给套餐导航项判断选中状态。
        /// </summary>
        public ComboData ApiCurrentCombo => curCombo;

        /// <summary>
        /// 绘制内置概览页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawOverviewPanel(Rect rect)
        {
            DrawOverviewPanel(rect);
        }

        /// <summary>
        /// 绘制内置营业时间页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawBusinessHoursPanel(Rect rect)
        {
            DrawBusinessHoursPanel(rect);
        }

        /// <summary>
        /// 绘制内置服务页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawServicesPanel(Rect rect)
        {
            DrawServicesPanel(rect);
        }

        /// <summary>
        /// 绘制内置套餐编辑页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawComboPanel(Rect rect)
        {
            DrawComboPanel(rect);
        }

        /// <summary>
        /// 选择指定套餐并切换到套餐编辑页，负责让外部导航 Worker 复用内置状态切换。
        /// </summary>
        public void ApiSelectCombo(ComboData combo)
        {
            if (combo == null) return;
            if (curCombo != null && curCombo != combo)
                EnsureComboHasName(curCombo);
            curPageDefName = PageComboEdit;
            curCombo = combo;
            listScroll = Vector2.zero;
            comboPriceBuf = combo.totalPrice.ToString("F0");
            NotifyPageOpened(GetCurrentUiPage());
        }

        /// <summary>
        /// 创建新套餐并切换到套餐编辑页，负责让外部导航 Worker 复用内置创建流程。
        /// </summary>
        public void ApiCreateCombo()
        {
            EnsureComboHasName(curCombo);
            ComboData newCombo = new ComboData();
            zoneCombos.Add(newCombo);
            curPageDefName = PageComboEdit;
            curCombo = newCombo;
            listScroll = Vector2.zero;
            comboPriceBuf = "0";
            NotifyPageOpened(GetCurrentUiPage());
        }
    }
}
