using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Dining;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //绘制餐厅总览，职责是先统计完整留存会话，再分页展示经营状态与订单明细。
    public class BusinessPageWorker_RestaurantExtension : BusinessManagerPageWorker
    {
        private const int PageSize = 30;

        //绘制总览页面，职责是使用窗口状态、框架搜索和统一列表样式。
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            var state = context.GetOrCreatePageState("RimSimRestaurant.Overview", () => new RestaurantOverviewState());
            using (new RestaurantGuiScope())
            {
                var shops = context.GetAllShops().Where(RestaurantBusinessUiUtility.IsRestaurantShop).ToList();
                var ids = new HashSet<int>(shops.Select(shop => shop.ID));
                var all = (RestaurantOrderUtility.OrderManager?.Sessions ?? new List<RestaurantDiningSession>())
                    .Where(session => ids.Contains(session.shopId)).ToList();
                float row = RestaurantUiStyle.ControlHeight();
                string summary = $"餐厅 {shops.Count} · 活跃 {all.Count(order => !order.IsTerminal)} · 留存会话 {all.Count}\n"
                    + $"订单金额 {all.Sum(session => session.Orders.Sum(o => o.price)):F0} · 待付款 {all.Where(order => order.state == RestaurantSessionState.AwaitingCheckout).Sum(order => order.Amount):F0} · 已付款 {all.Sum(session => session.Orders.Sum(o => o.paidAmount)):F0}";
                float header = Text.CalcHeight(summary, rect.width);
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, header), summary);
                float y = rect.y + header + 8f;
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(rect.x, y, 130f, row), "用餐会话与结果"))
                { state.showOrders = true; state.scroll = Vector2.zero; }
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(rect.x + 138f, y, 130f, row), "餐厅经营状态"))
                { state.showOrders = false; state.scroll = Vector2.zero; }
                var body = new Rect(rect.x, y + row + 8f, rect.width, Mathf.Max(0f, rect.yMax - y - row - 8f));
                if (state.showOrders) DrawOrders(body, all, context, state);
                else DrawShops(body, shops, context, state);
            }
        }

        //绘制会话分页和可展开子订单，职责是让分页仅影响列表而不影响汇总。
        private static void DrawOrders(Rect rect, List<RestaurantDiningSession> all, BusinessManagerUiContext context, RestaurantOverviewState state)
        {
            var sessions = all.Where(s => Matches(context.SearchText, CustomerName(s.Anchor) + " " + s.reason
                + " " + string.Join(" ", s.Orders.Select(o => o.menuItemLabel))))
                .OrderBy(s => s.IsTerminal).ThenByDescending(s => s.sessionId).ToList();
            int pages = Mathf.Max(1, Mathf.CeilToInt(sessions.Count / (float)PageSize));
            state.page = Mathf.Clamp(state.page, 0, pages - 1);
            float control = RestaurantUiStyle.ControlHeight();
            float footer = rect.yMax - control;
            Widgets.Label(new Rect(rect.x, footer, rect.width - 216f, control), $"匹配 {sessions.Count} 次用餐 · 第 {state.page + 1}/{pages} 页");
            if (RestaurantUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 204f, footer, 96f, control), "上一页", state.page > 0))
            { state.page--; state.scroll = Vector2.zero; }
            if (RestaurantUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 100f, footer, 100f, control), "下一页", state.page < pages - 1))
            { state.page++; state.scroll = Vector2.zero; }
            var shown = sessions.Skip(state.page * PageSize).Take(PageSize).ToList();
            float line = RestaurantUiStyle.LineHeight(GameFont.Small), childHeight = line * 4f + 18f, summaryHeight = line * 2f + 14f;
            float height = shown.Sum(s => summaryHeight + (state.expanded.Contains(s.sessionId) ? s.Orders.Count() * childHeight : 0));
            var outer = new Rect(rect.x, rect.y, rect.width, Mathf.Max(0, rect.height - control - 8f));
            var view = new Rect(0, 0, rect.width - 16f, Mathf.Max(outer.height, height));
            Widgets.BeginScrollView(outer, ref state.scroll, view);
            try
            {
                float y = 0;
                foreach (var session in shown)
                {
                    var block = new Rect(0, y, view.width, summaryHeight);
                    ShopUiVisualUtility.DrawTableRowBackground(block, 0, true);
                    bool expanded = state.expanded.Contains(session.sessionId);
                    if (Widgets.ButtonText(new Rect(4, y + 4, 32, control), expanded ? "−" : "+"))
                    { if (expanded) state.expanded.Remove(session.sessionId); else state.expanded.Add(session.sessionId); }
                    string status = session.state == RestaurantSessionState.AwaitingCheckout ? "待付款"
                        : session.state == RestaurantSessionState.Completed ? "已付款"
                        : session.IsTerminal ? "用餐失败" : session.stopOrdering ? "停止追加，等待处理完商品" : "用餐与追加中";
                    DrawText(new Rect(44, y + 4, view.width - 50, line),
                        $"会话 #{session.sessionId} · {CustomerName(session.Anchor)} · 接单 {session.rounds} 次 · {status}");
                    DrawText(new Rect(44, y + line + 7, view.width - 50, line),
                        $"实际账单 {session.Amount:F0} · 已付 {session.Orders.Sum(o => o.paidAmount):F0} · 实际成本 {session.Cost:F0} · {session.reason}");
                    y += summaryHeight;
                    if (!expanded) continue;
                    foreach (var order in session.Orders)
                    { DrawOrder(new Rect(18, y, view.width - 18, childHeight), order, order.orderId, line); y += childHeight; }
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        //绘制订单当前阶段、员工与金额，职责是区分应付、已付和阻塞原因。
        private static void DrawOrder(Rect rect, RestaurantOrder order, int index, float line)
        {
            ShopUiVisualUtility.DrawTableRowBackground(rect, index, false);
            string stage = RestaurantBusinessUiUtility.StateLabel(order.state);
            string meal = order.menuConfirmed ? order.menuItemLabel + " ×" + order.mealCount : "尚未点菜";
            string title = $"#{order.orderId} {CustomerName(order)} · {meal}";
            Text.WordWrap = false;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, Mathf.Max(40f, rect.width - 142f), line), title.Truncate(rect.width - 142f));
            RestaurantUiStyle.DrawBadge(new Rect(rect.xMax - 130f, rect.y + 3f, 124f, line + 2f), stage, RestaurantBusinessUiUtility.StateColor(order.state));
            int id = order.state == RestaurantOrderState.Cooking || order.state == RestaurantOrderState.ChefBringingToPass
                ? order.cookThingId : order.waiterThingId;
            Map map = Find.Maps.FirstOrDefault(item => item.uniqueID == order.mapId);
            string employee = (RestaurantOrderUtility.FindThingById(map, id) as Pawn)?.LabelShortCap ?? "未认领";
            int began = order.menuConfirmed ? order.orderedTick : order.seatedTick >= 0 ? order.seatedTick : order.createdTick;
            int ended = order.deliveredTick > 0 ? order.deliveredTick : order.IsTerminal ? order.completedTick : Find.TickManager.TicksGame;
            string detail = $"负责员工：{employee} · 等待 {Mathf.Max(0, ended - began) / 60} 秒 · 订单 {order.price:F0} · 已付 {order.paidAmount:F0}";
            DrawText(new Rect(rect.x + 6f, rect.y + line + 6f, rect.width - 12f, line), detail);
            string issue = order.failReason.NullOrEmpty() ? order.blockReason : order.failReason;
            if (issue.NullOrEmpty())
            {
                if (order.state == RestaurantOrderState.WaitingOrder) issue = "等待能到达顾客旁的服务员";
                else if (order.state == RestaurantOrderState.WaitingCook) issue = "等待厨师、灶台与足量可预约食材";
                else if (order.state == RestaurantOrderState.ChefBringingToPass && order.cookThingId < 0) issue = "等待厨师恢复已有成品的出餐运输";
                else if (order.state == RestaurantOrderState.ReadyToDeliver) issue = "等待服务员及可达的取餐、交付位置";
                else if (order.state == RestaurantOrderState.AwaitingCheckout) issue = "用餐完成，等待框架收银付款";
                else issue = order.selectionReason;
            }
            DrawText(new Rect(rect.x + 6f, rect.y + line * 2f + 8f, rect.width - 12f, line),
                $"第 {order.round} 轮 · 来源 {(order.stockProduct ? order.sourceCabinet?.LabelCap.ToString() ?? "货柜已移除" : "厨房制作")} · 已接受 {order.acceptedCount}/{order.mealCount}");
            DrawText(new Rect(rect.x + 6f, rect.y + line * 3f + 10f, rect.width - 12f, line), issue ?? "");
            Text.WordWrap = true;
        }

        //绘制餐厅状态列表，职责是共用经营快照并提供管理与定位入口。
        private static void DrawShops(Rect rect, List<Zone_Shop> shops, BusinessManagerUiContext context, RestaurantOverviewState state)
        {
            var shown = shops.Where(shop => Matches(context.SearchText, shop.label)).ToList();
            float line = RestaurantUiStyle.LineHeight(GameFont.Small);
            float row = Mathf.Max(line * 2f + 16f, RestaurantUiStyle.ControlHeight() + 12f);
            var view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, row * shown.Count));
            Widgets.BeginScrollView(rect, ref state.scroll, view);
            try
            {
                for (int i = 0; i < shown.Count; i++)
                {
                    var shop = shown[i];
                    var cell = new Rect(0f, i * row, view.width, row);
                    ShopUiVisualUtility.DrawTableRowBackground(cell, i, false);
                    DrawText(new Rect(6f, cell.y + 4f, cell.width - 190f, line), shop.label);
                    string issue = RestaurantBusinessAvailability.Snapshot(shop);
                    DrawText(new Rect(6f, cell.y + line + 8f, cell.width - 190f, line), issue.NullOrEmpty() ? "当前经营检查通过" : issue);
                    if (RestaurantUiStyle.DrawPrimaryButton(new Rect(cell.xMax - 180f, cell.y + 6f, 82f, row - 12f), "管理")) context.OpenShop(shop);
                    if (RestaurantUiStyle.DrawSecondaryButton(new Rect(cell.xMax - 90f, cell.y + 6f, 82f, row - 12f), "定位")) context.JumpToShop(shop);
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        //显示可截断文字及完整悬浮提示，职责是保持窄列与中文字体安全。
        private static void DrawText(Rect rect, string text)
        {
            Widgets.Label(rect, text.Truncate(rect.width));
            TooltipHandler.TipRegion(rect, text);
        }

        //查询顾客名称，职责是让离图历史订单仍有可识别身份。
        private static string CustomerName(RestaurantOrder order)
        {
            Map map = Find.Maps.FirstOrDefault(item => item.uniqueID == order.mapId);
            return RestaurantOrderUtility.FindCustomer(map, order)?.LabelShortCap ?? $"顾客 #{order.customerThingId}";
        }

        //匹配框架搜索词，职责是为餐厅和订单使用同一搜索输入。
        private static bool Matches(string search, string value)
        {
            return search.NullOrEmpty() || (value ?? "").IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        //关闭总览保存按钮，职责是表明此页不修改业务设置。
        public override bool ShowSaveButton(ShopUiContext context)
        {
            return false;
        }
    }
}
