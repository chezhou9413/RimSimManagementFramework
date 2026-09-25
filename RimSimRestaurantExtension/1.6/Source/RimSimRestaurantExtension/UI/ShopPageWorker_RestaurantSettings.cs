using SimManagementLib.Tool;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //绘制框架内单店菜单表格，职责是复用窗口搜索和统一保存并把编辑状态保存在上下文。
    public class ShopPageWorker_RestaurantSettings : ShopManagerPageWorker
    {
        private const string StateKey = "RimSimRestaurant.Settings";

        //判断餐厅页面是否属于当前店铺，职责是按接待设施挂载页面。
        public override bool CanShow(ShopUiContext context)
        {
            return RestaurantOrderCreationUtility.FindOrderCounter((context as ShopManagerUiContext)?.Shop) != null;
        }

        //获取当前窗口草稿，职责是切页时保留编辑内容。
        private static RestaurantPageState State(ShopManagerUiContext context)
        {
            return context.GetOrCreatePageState(StateKey, () => new RestaurantPageState
            {
                draft = RestaurantOrderUtility.Settings.GetOrCreate(context.Shop.ID).Clone()
            });
        }

        //打开页面时建立一次草稿，职责是不覆盖未保存编辑。
        public override void OnOpen(ShopUiContext context)
        {
            if (context is ShopManagerUiContext shopContext && shopContext.Shop != null) State(shopContext);
        }

        //绘制菜单工作台，职责是把经营提示、设施入口和可编辑菜品分成清晰层级。
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            var state = State(context);
            using (new RestaurantGuiScope())
            {
                float h = RestaurantUiStyle.ControlHeight();
                float y = rect.y + ShopUiVisualUtility.DrawPageHeading(rect, SimTranslation.T("RSR.UI.MenuHeading"),
                    SimTranslation.T("RSR.UI.MenuCountHint", (state.draft.menuItems.Count).Named("count")));
                string issue = RestaurantBusinessAvailability.Snapshot(context.Shop);
                string status = issue.NullOrEmpty() ? SimTranslation.T("RSR.UI.BusinessReadyHint") : SimTranslation.T("RSR.UI.BusinessIssue", (issue).Named("reason"));
                float notice = ShopUiVisualUtility.NoticeHeight(status, rect.width);
                ShopUiVisualUtility.DrawNotice(new Rect(rect.x, y, rect.width, notice), status, !issue.NullOrEmpty());
                y += notice + 10f;
                RestaurantUiStyle.DrawCheckbox(new Rect(rect.x, y, 118f, h), SimTranslation.T("RSR.UI.EnableRestaurant"), ref state.draft.enabled);
                bool narrow = rect.width < 500f;
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(narrow ? rect.xMax - 110f : rect.x + 126f, y, 110f, h), SimTranslation.T("RSR.UI.Parameters")))
                    Find.WindowStack.Add(new Dialog_RestaurantParameters(state.draft));
                if (narrow) y += h + 8f;
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(narrow ? rect.x : rect.x + 244f, y, 100f, h), SimTranslation.T("RSR.UI.ConveyorManager")))
                    OpenConveyors(context);
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(rect.xMax - 110f, y, 110f, h), SimTranslation.T("RSR.UI.AddDish")))
                    OpenEditor(context, state, new RestaurantMenuItem { id = GameComp.RestaurantShopSettings.MakeMenuId() }, true);
                y += h + 10f;
                RestaurantMenuTable.Draw(new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y)), context, state,
                    (item, adding) => OpenEditor(context, state, item, adding));
            }
        }

        //打开本店线路配置，职责是让传送带从框架商店管理页面直接进入同风格工作台。
        private static void OpenConveyors(ShopManagerUiContext context)
        {
            var manager = context.Shop.Map.GetComponent<Conveyor.Transport.MapComponent_SushiConveyor>();
            manager.Rebuild();
            var lines = manager.lines.Where(line => line.Shop == context.Shop).ToList();
            if (lines.Count == 0)
            {
                Messages.Message(SimTranslation.T("RSR.UI.NoConveyor"), RimWorld.MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (lines.Count == 1) Find.WindowStack.Add(new Conveyor.UI.Dialog_ConveyorStock(lines[0].segments[0]));
            else Find.WindowStack.Add(new FloatMenu(lines.Select(line => new FloatMenuOption(
                SimTranslation.T("RSR.UI.LineSummary", (line.id).Named("id"), (line.Occupied).Named("occupied"), (line.segments.Count).Named("capacity")),
                () => Find.WindowStack.Add(new Conveyor.UI.Dialog_ConveyorStock(line.segments[0])))).ToList()));
        }

        //打开独立菜单编辑窗口，职责是子窗口确认仅修改当前父窗口草稿。
        private static void OpenEditor(ShopManagerUiContext context, RestaurantPageState state, RestaurantMenuItem item, bool adding)
        {
            Find.WindowStack.Add(new Dialog_RestaurantMenuEditor(context.Shop, item, edited =>
            {
                if (context.GetPageState<RestaurantPageState>(StateKey) != state) return;
                if (adding) state.draft.menuItems.Add(edited);
                else
                {
                    int index = state.draft.menuItems.FindIndex(menu => menu.id == item.id);
                    if (index >= 0) state.draft.menuItems[index] = edited;
                }
                state.menuStatus.Clear();
            }));
        }

        //提交当前窗口草稿，职责是只有统一保存才改变游戏中的菜单和参数。
        public override void OnSave(ShopUiContext context)
        {
            var state = context.GetPageState<RestaurantPageState>(StateKey);
            var shop = (context as ShopManagerUiContext)?.Shop;
            if (state == null || shop == null) return;
            RestaurantOrderUtility.Settings.GetOrCreate(shop.ID).CopyFrom(state.draft);
            RestaurantBusinessAvailability.Reset();
            RestaurantMenuUtility.ResetSelections();
            shop.InvalidateShopRuntimeCache();
        }

        //说明保存语义，职责是帮助用户区分子窗口确认和实际提交。
        public override string GetSaveTip(ShopUiContext context)
        {
            return SimTranslation.T("RSR.UI.MenuSaveHint");
        }
    }
}
