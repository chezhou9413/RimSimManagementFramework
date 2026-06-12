using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager
    {
        /// <summary>
        /// 返回所有可管理商店，负责给 UI API 上下文提供稳定的商店列表。
        /// </summary>
        public IReadOnlyList<Zone_Shop> ApiGetAllShops()
        {
            return CollectAllShops().Select(entry => entry.Zone).Where(zone => zone != null).ToList();
        }

        /// <summary>
        /// 绘制内置商店管理页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawShopManagementPage(Rect rect)
        {
            DrawShopManagementPage(rect);
        }

        /// <summary>
        /// 绘制内置售货机页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawVendingMachinePage(Rect rect)
        {
            DrawVendingMachinePage(rect);
        }

        /// <summary>
        /// 绘制内置财务页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawFinancePage(Rect rect)
        {
            DrawFinancePage(rect);
        }

        /// <summary>
        /// 绘制内置评价页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawCustomerReviewsPage(Rect rect)
        {
            DrawCustomerReviewsPage(rect);
        }

        /// <summary>
        /// 绘制内置收藏品兑换页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawCollectibleExchangePage(Rect rect)
        {
            DrawCollectibleExchangePage(rect);
        }

        /// <summary>
        /// 绘制内置蓝图页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawBlueprintPage(Rect rect)
        {
            DrawBlueprintPage(rect);
        }

        /// <summary>
        /// 绘制内置公告页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawAnnouncementsPage(Rect rect)
        {
            DrawAnnouncementsPage(rect);
        }

        /// <summary>
        /// 绘制内置顾客页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawCustomerPage(Rect rect)
        {
            DrawCustomerPage(rect);
        }

        /// <summary>
        /// 绘制内置员工页，负责让内置 Worker 复用原窗口实现。
        /// </summary>
        public void ApiDrawStaffPage(Rect rect)
        {
            DrawStaffPage(rect);
        }
    }
}
