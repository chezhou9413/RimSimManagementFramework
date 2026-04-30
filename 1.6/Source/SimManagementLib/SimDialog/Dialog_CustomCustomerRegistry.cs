using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 提供用于编辑玩家自定义运行时顾客类型的游戏内窗口。
    /// </summary>
    public class Dialog_CustomCustomerRegistry : Window
    {
        private const float HeaderHeight = 76f;
        private const float SidebarWidth = 390f;
        private const float SidebarFooterHeight = 88f;
        private const float SectionGap = 14f;
        private const float KindRowHeight = 94f;
        private const float ScrollbarWidth = 16f;
        private const float CloseXReservedWidth = Widgets.CloseButtonSize + Widgets.CloseButtonMargin * 2f + 18f;
        private const int BrowserPageSize = 11;

        private static readonly Color WindowBg = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f, 0.95f);
        private static readonly Color Accent = new Color(0.18f, 0.69f, 0.87f, 1f);
        private static readonly Color SoftAccent = new Color(0.18f, 0.69f, 0.87f, 0.12f);
        private static readonly Color MutedText = new Color(0.73f, 0.77f, 0.82f, 1f);
        private static readonly Color BuiltInBadge = new Color(0.21f, 0.49f, 0.78f, 0.22f);
        private static readonly Color CustomBadge = new Color(0.17f, 0.72f, 0.48f, 0.20f);
        private static readonly Color LockedBadge = new Color(0.90f, 0.66f, 0.24f, 0.20f);

        private CustomCustomerDatabaseData draftData;
        private List<RuntimeCustomerKind> previewKinds = new List<RuntimeCustomerKind>();
        private List<PawnKindDef> allPawnKinds = new List<PawnKindDef>();
        private List<ThingDef> allPreferenceThings = new List<ThingDef>();
        private List<WeatherDef> allWeatherDefs = new List<WeatherDef>();
        private List<RuntimeGoodsCategory> allGoodsCategories = new List<RuntimeGoodsCategory>();

        private string selectedKindId = string.Empty;
        private string newKindLabelBuffer = string.Empty;
        private string selectedLabelBuffer = string.Empty;
        private string pawnKindSearch = string.Empty;
        private string preferenceThingSearch = string.Empty;
        private string profileLabelBuffer = string.Empty;

        private Vector2 kindScroll;
        private Vector2 detailsScroll;
        private Vector2 pawnKindBrowserScroll;
        private Vector2 preferenceBrowserScroll;
        private Vector2 targetGoodsScroll;
        private Vector2 weatherScroll;
        private Vector2 preferenceListScroll;
        private Vector2 preferenceCategoryScroll;
        private int pawnKindPageIndex;
        private int preferencePageIndex;
        private bool dirty;

        public override Vector2 InitialSize => new Vector2(1380f, 860f);

        /// <summary>
        /// 初始化顾客注册窗口，并加载用于选择控件的候选 Def 数据。
        /// </summary>
        public Dialog_CustomCustomerRegistry()
        {
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            draggable = true;
            resizeable = true;
            closeOnClickedOutside = false;

            LoadDraft();
        }

        /// <summary>
        /// 绘制完整注册窗口，并在结束时恢复共享 IMGUI 状态。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                Widgets.DrawBoxSolid(inRect, WindowBg);

                float headerHeight = Mathf.Max(HeaderHeight, Text.LineHeightOf(GameFont.Medium) + Text.LineHeightOf(GameFont.Tiny) * 2f + 24f);
                Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, headerHeight);
                Rect bodyRect = new Rect(inRect.x, headerRect.yMax + 8f, inRect.width, Mathf.Max(0f, inRect.height - headerHeight - 8f));
                float sidebarWidth = Mathf.Min(SidebarWidth, Mathf.Max(280f, bodyRect.width * 0.40f));
                Rect sidebarRect = new Rect(bodyRect.x, bodyRect.y, sidebarWidth, bodyRect.height);
                Rect contentRect = new Rect(sidebarRect.xMax + SectionGap, bodyRect.y, Mathf.Max(0f, bodyRect.width - sidebarWidth - SectionGap), bodyRect.height);

                DrawHeader(headerRect);
                DrawSidebar(sidebarRect);
                DrawContent(contentRect);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        /// <summary>
        /// 绘制标题文本和数据库操作按钮。
        /// </summary>
        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            float actionWidth = 590f;
            float textWidth = Mathf.Max(240f, rect.width - actionWidth - CloseXReservedWidth - 44f);
            Rect titleRect = new Rect(rect.x + 20f, rect.y + 8f, textWidth, Text.LineHeightOf(GameFont.Medium) + 4f);

            Text.Font = GameFont.Medium;
            GUI.color = Color.white;
            Widgets.Label(titleRect, "自定义顾客注册");

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Rect descRect = new Rect(rect.x + 20f, titleRect.yMax + 4f, textWidth, Text.LineHeightOf(GameFont.Tiny) * 2f + 6f);
            Widgets.Label(descRect, "CustomerKindDef 只读。玩家本地 JSON 会在 Def 之后加载，作为运行时顾客 Kind 参与自然刷客。");

            float buttonY = rect.y + 18f;
            float right = rect.xMax - 16f - CloseXReservedWidth;

            const float actionButtonWidth = 96f;
            const float actionButtonGap = 10f;

            right -= actionButtonWidth;
            if (SimUiStyle.DrawPrimaryButton(new Rect(right, buttonY, actionButtonWidth, 34f), dirty ? "保存*" : "保存"))
                SaveDraft();

            right -= actionButtonWidth + actionButtonGap;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, actionButtonWidth, 34f), "重载"))
                ConfirmReload();

            right -= actionButtonWidth + actionButtonGap;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, actionButtonWidth, 34f), "刷新类型"))
                RefreshGoodsCategories(true);

            right -= actionButtonWidth + actionButtonGap;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, actionButtonWidth, 34f), "导出 B64"))
                Find.WindowStack.Add(new Dialog_TextTransfer("导出顾客 Base64", "下面这串内容包含玩家自定义顾客 Kind。", CustomCustomerDatabase.ExportBase64(draftData)));

            right -= actionButtonWidth + actionButtonGap;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, actionButtonWidth, 34f), "导入 B64"))
                Find.WindowStack.Add(new Dialog_TextImport(HandleImportReplace));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制顾客类型列表和新建区域。
        /// </summary>
        private void DrawSidebar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Rect titleRect = new Rect(rect.x + 16f, rect.y + 12f, rect.width - 32f, Text.LineHeightOf(GameFont.Small) + 6f);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(titleRect, "顾客 Kind");

            Rect listRect = new Rect(rect.x + 12f, titleRect.yMax + 8f, rect.width - 24f, Mathf.Max(0f, rect.height - SidebarFooterHeight - titleRect.height - 26f));
            Rect footerRect = new Rect(rect.x + 12f, rect.yMax - SidebarFooterHeight, rect.width - 24f, SidebarFooterHeight - 12f);

            float viewHeight = Mathf.Max(listRect.height, previewKinds.Count * (KindRowHeight + 6f));
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), viewHeight);

            Widgets.BeginScrollView(listRect, ref kindScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < previewKinds.Count; i++)
            {
                RuntimeCustomerKind kind = previewKinds[i];
                DrawKindRow(new Rect(0f, y, viewRect.width, KindRowHeight), kind);
                y += KindRowHeight + 6f;
            }
            Widgets.EndScrollView();

            Widgets.DrawBoxSolid(footerRect, new Color(1f, 1f, 1f, 0.03f));
            SimUiStyle.DrawBorder(footerRect, new Color(1f, 1f, 1f, 0.06f));
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(footerRect.x + 10f, footerRect.y + 8f, footerRect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), "新建运行时顾客");
            newKindLabelBuffer = Widgets.TextField(new Rect(footerRect.x + 10f, footerRect.y + 30f, footerRect.width - 108f, 28f), newKindLabelBuffer);
            if (SimUiStyle.DrawPrimaryButton(new Rect(footerRect.xMax - 90f, footerRect.y + 29f, 80f, 30f), "创建", !string.IsNullOrWhiteSpace(newKindLabelBuffer), GameFont.Tiny))
                CreateKind();

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        /// <summary>
        /// 绘制一行顾客类型，显示来源和关键数值。
        /// </summary>
        private void DrawKindRow(Rect rect, RuntimeCustomerKind kind)
        {
            bool selected = selectedKindId == kind.kindId;
            Widgets.DrawBoxSolid(rect, selected ? SoftAccent : new Color(1f, 1f, 1f, Mouse.IsOver(rect) ? 0.05f : 0.02f));
            SimUiStyle.DrawBorder(rect, selected ? Accent : new Color(1f, 1f, 1f, 0.04f));

            if (selected)
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 4f, rect.height), Accent);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            float titleWidth = Mathf.Max(50f, rect.width - 110f);
            titleWidth = Mathf.Max(50f, rect.width - 24f);
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 8f, titleWidth, Text.LineHeightOf(GameFont.Small) + 2f), kind.label.Truncate(titleWidth));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 36f, titleWidth, Text.LineHeightOf(GameFont.Tiny) + 2f), kind.kindId.Truncate(titleWidth));
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 58f, titleWidth - 92f, Text.LineHeightOf(GameFont.Tiny) + 2f), $"PawnKind {kind.pawnKindDefs.Count}  预算 {kind.budgetRange.min}-{kind.budgetRange.max}");

            DrawBadge(new Rect(rect.xMax - 82f, rect.y + 58f, 70f, 18f), kind.sourceDef != null ? "Def" : "Custom", kind.sourceDef != null ? BuiltInBadge : CustomBadge);

            if (Widgets.ButtonInvisible(rect))
            {
                selectedKindId = kind.kindId;
                selectedLabelBuffer = kind.label;
                detailsScroll = Vector2.zero;
                pawnKindBrowserScroll = Vector2.zero;
                preferenceBrowserScroll = Vector2.zero;
                targetGoodsScroll = Vector2.zero;
                weatherScroll = Vector2.zero;
                preferenceListScroll = Vector2.zero;
                preferenceCategoryScroll = Vector2.zero;
                pawnKindPageIndex = 0;
                preferencePageIndex = 0;
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制当前选中顾客类型的编辑区或只读 Def 详情。
        /// </summary>
        private void DrawContent(Rect rect)
        {
            RuntimeCustomerKind selected = GetSelectedKind();
            if (selected == null)
            {
                Widgets.DrawBoxSolid(rect, PanelBg);
                SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = MutedText;
                Widgets.Label(rect, "没有可用的顾客 Kind");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Rect scrollRect = rect.ContractedBy(12f);
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, scrollRect.width - ScrollbarWidth), EstimateContentHeight(selected));
            Widgets.BeginScrollView(scrollRect, ref detailsScroll, viewRect);

            float y = 0f;
            y = DrawKindInfo(new Rect(0f, y, viewRect.width, 126f), selected);
            y += SectionGap;
            y = DrawNumericSettings(new Rect(0f, y, viewRect.width, 138f), selected);
            y += SectionGap;
            y = DrawPawnKinds(new Rect(0f, y, viewRect.width, 310f), selected);
            y += SectionGap;
            y = DrawTargetsAndWeather(new Rect(0f, y, viewRect.width, 250f), selected);
            y += SectionGap;
            y = DrawPreferences(new Rect(0f, y, viewRect.width, 340f), selected);
            y += SectionGap;
            DrawProfiles(new Rect(0f, y, viewRect.width, EstimateProfilesHeight(selected)), selected);

            Widgets.EndScrollView();
        }

        /// <summary>
        /// 估算容纳全部编辑分区所需的滚动高度。
        /// </summary>
        private float EstimateContentHeight(RuntimeCustomerKind selected)
        {
            return 126f + 138f + 310f + 250f + 340f + EstimateProfilesHeight(selected) + SectionGap * 5f + 24f;
        }

        /// <summary>
        /// 估算顾客档案编辑区所需高度。
        /// </summary>
        private float EstimateProfilesHeight(RuntimeCustomerKind selected)
        {
            int profileCount = GetDraftRecord(selected.kindId)?.spawnProfiles?.Count ?? selected.spawnProfiles.Count;
            return Mathf.Max(260f, 104f + profileCount * 138f);
        }

        /// <summary>
        /// 绘制当前顾客类型的名称、ID、来源和删除控件。
        /// </summary>
        private float DrawKindInfo(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, "基础信息");
            bool editable = kind.sourceDef == null;
            float right = rect.xMax - 14f;

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 42f, 110f, Text.LineHeightOf(GameFont.Tiny) + 2f), "显示名称");
            if (editable)
            {
                selectedLabelBuffer = Widgets.TextField(new Rect(rect.x + 126f, rect.y + 36f, 220f, 28f), selectedLabelBuffer ?? kind.label);
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.x + 356f, rect.y + 35f, 70f, 30f), "改名", true, GameFont.Tiny))
                    RenameSelectedKind();
                if (SimUiStyle.DrawDangerButton(new Rect(right - 100f, rect.y + 35f, 100f, 30f), "删除 Kind", true, GameFont.Tiny))
                    ConfirmDeleteKind();
            }
            else
            {
                GUI.color = Color.white;
                Widgets.Label(new Rect(rect.x + 126f, rect.y + 40f, rect.width - 260f, Text.LineHeightOf(GameFont.Tiny) + 2f), kind.label);
                DrawBadge(new Rect(right - 96f, rect.y + 38f, 96f, 20f), "Def 只读", LockedBadge);
            }

            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 74f, rect.width - 28f, Text.LineHeightOf(GameFont.Tiny) + 2f), "ID: " + kind.kindId);
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 94f, rect.width - 28f, Text.LineHeightOf(GameFont.Tiny) + 2f), editable ? "来源：玩家本地 JSON。保存后会合并进运行时顾客目录。" : "来源：CustomerKindDef。这里可以查看数据，但不能编辑 Def。");

            GUI.color = Color.white;
            return rect.yMax;
        }

        /// <summary>
        /// 绘制自定义顾客的可编辑数值设置，以及 Def 顾客的只读数值。
        /// </summary>
        private float DrawNumericSettings(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, "刷新与预算");
            CustomCustomerKindRecord record = GetDraftRecord(kind.kindId);
            bool editable = record != null;

            if (editable)
            {
                DrawFloatField(new Rect(rect.x + 14f, rect.y + 42f, 190f, 30f), "MTB 天", ref record.baseMtbDays, 0.01f, 20f);
                DrawIntField(new Rect(rect.x + 218f, rect.y + 42f, 190f, 30f), "预算下限", ref record.budgetMin, 1, 1000000);
                DrawIntField(new Rect(rect.x + 422f, rect.y + 42f, 190f, 30f), "预算上限", ref record.budgetMax, 1, 1000000);
                DrawIntField(new Rect(rect.x + 14f, rect.y + 82f, 190f, 30f), "耐心下限", ref record.queuePatienceMin, 60, 120000);
                DrawIntField(new Rect(rect.x + 218f, rect.y + 82f, 190f, 30f), "耐心上限", ref record.queuePatienceMax, 60, 120000);
                DrawFloatField(new Rect(rect.x + 422f, rect.y + 82f, 190f, 30f), "最低口碑", ref record.minShopReputation, 0f, 100f);
                DrawFloatField(new Rect(rect.x + 626f, rect.y + 42f, 170f, 30f), "开始小时", ref record.activeHourMin, 0f, 24f);
                DrawFloatField(new Rect(rect.x + 626f, rect.y + 82f, 170f, 30f), "结束小时", ref record.activeHourMax, 0f, 24f);
            }
            else
            {
                DrawReadOnlyLine(new Rect(rect.x + 14f, rect.y + 42f, rect.width - 28f, Text.LineHeightOf(GameFont.Tiny) + 2f), $"MTB {kind.baseMtbDays:F2} 天    预算 {kind.budgetRange.min}-{kind.budgetRange.max}    排队耐心 {kind.queuePatienceRange.min}-{kind.queuePatienceRange.max}");
                DrawReadOnlyLine(new Rect(rect.x + 14f, rect.y + 72f, rect.width - 28f, Text.LineHeightOf(GameFont.Tiny) + 2f), $"活跃时段 {kind.activeHourRange.TrueMin:F1}-{kind.activeHourRange.TrueMax:F1}    最低口碑 {kind.minShopReputation:F0}");
            }

            return rect.yMax;
        }

        /// <summary>
        /// 绘制已选 PawnKind，并提供可搜索的 PawnKind 浏览器。
        /// </summary>
        private float DrawPawnKinds(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, "PawnKind");
            CustomCustomerKindRecord record = GetDraftRecord(kind.kindId);
            bool editable = record != null;

            Rect listRect = new Rect(rect.x + 14f, rect.y + 38f, rect.width * 0.36f, rect.height - 52f);
            Rect browserRect = new Rect(listRect.xMax + 14f, listRect.y, rect.width - listRect.width - 42f, listRect.height);

            DrawStringChipList(listRect, kind.pawnKindDefs.Select(p => p.LabelCap.RawText + " / " + p.defName).ToList(), editable ? "已选 PawnKind，可用右侧追加。" : "Def 中配置的 PawnKind。");
            if (editable)
                DrawPawnKindBrowser(browserRect, record);
            else
                DrawReadOnlyPanel(browserRect, "Def 顾客不能在运行时修改 PawnKind。");

            return rect.yMax;
        }

        /// <summary>
        /// 绘制目标商品类型和天气过滤条件。
        /// </summary>
        private float DrawTargetsAndWeather(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, "兴趣类型与天气");
            CustomCustomerKindRecord record = GetDraftRecord(kind.kindId);
            bool editable = record != null;

            Rect goodsRect = new Rect(rect.x + 14f, rect.y + 38f, (rect.width - 42f) * 0.5f, rect.height - 52f);
            Rect weatherRect = new Rect(goodsRect.xMax + 14f, goodsRect.y, goodsRect.width, goodsRect.height);

            if (editable)
            {
                DrawGoodsCategoryToggles(goodsRect, record.targetGoodsCategoryIds, "目标商品类型");
                DrawWeatherToggles(weatherRect, record.allowedWeatherDefNames, "允许天气");
            }
            else
            {
                DrawStringChipList(goodsRect, kind.targetGoodsCategoryIds, "目标商品类型。为空表示不限制。");
                DrawStringChipList(weatherRect, kind.allowedWeathers.Select(w => w.LabelCap.RawText + " / " + w.defName).ToList(), "允许天气。为空表示不限制。");
            }

            return rect.yMax;
        }

        /// <summary>
        /// 绘制自定义偏好记录，并提供用于追加偏好物品的 ThingDef 浏览器。
        /// </summary>
        private float DrawPreferences(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, "偏好");
            CustomCustomerKindRecord record = GetDraftRecord(kind.kindId);
            bool editable = record != null;
            float columnWidth = (rect.width - 56f) / 3f;
            Rect leftRect = new Rect(rect.x + 14f, rect.y + 38f, columnWidth, rect.height - 52f);
            Rect middleRect = new Rect(leftRect.xMax + 14f, leftRect.y, columnWidth, leftRect.height);
            Rect rightRect = new Rect(middleRect.xMax + 14f, leftRect.y, rect.width - columnWidth * 2f - 56f, leftRect.height);

            if (editable)
            {
                DrawPreferenceList(leftRect, record);
                DrawPreferenceCategoryToggles(middleRect, record);
                DrawPreferenceThingBrowser(rightRect, record);
            }
            else
            {
                DrawStringChipList(leftRect, kind.itemPreferences.Select(FormatPreference).ToList(), "Def 中配置的偏好。");
                DrawReadOnlyPanel(middleRect, "运行时不会修改 Def 偏好。");
                DrawReadOnlyPanel(rightRect, "自定义顾客可以追加偏好商品。");
            }

            return rect.yMax;
        }

        /// <summary>
        /// 绘制自定义顾客类型的轻量档案控制区。
        /// </summary>
        private void DrawProfiles(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, "顾客档案");
            CustomCustomerKindRecord record = GetDraftRecord(kind.kindId);
            bool editable = record != null;
            Rect listRect = new Rect(rect.x + 14f, rect.y + 38f, rect.width - 28f, rect.height - 52f);

            if (!editable)
            {
                DrawStringChipList(listRect, kind.spawnProfiles.Select(p => $"{p.label}  权重 {p.weight:F1}  预算 {p.budgetRange.min}-{p.budgetRange.max}").ToList(), "Def 中配置的档案。为空则使用基础配置。");
                return;
            }

            Widgets.DrawBoxSolid(listRect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(listRect, new Color(1f, 1f, 1f, 0.06f));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(listRect.x + 10f, listRect.y + 8f, listRect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), "档案用于给同一 Kind 增加不同预算、耐心、时间、天气、目标类型和偏好。");

            float y = listRect.y + 34f;
            for (int i = 0; i < record.spawnProfiles.Count; i++)
            {
                CustomCustomerProfileRecord profile = record.spawnProfiles[i];
                Rect rowRect = new Rect(listRect.x + 10f, y, listRect.width - 20f, 128f);
                DrawProfileRow(rowRect, record, profile, i);
                y += 136f;
            }

            Rect inputRect = new Rect(listRect.x + 10f, listRect.yMax - 36f, listRect.width - 100f, 28f);
            profileLabelBuffer = Widgets.TextField(inputRect, profileLabelBuffer);
            if (SimUiStyle.DrawPrimaryButton(new Rect(listRect.xMax - 82f, listRect.yMax - 37f, 72f, 30f), "添加", !string.IsNullOrWhiteSpace(profileLabelBuffer), GameFont.Tiny))
                AddProfile(record);

            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制单个顾客档案的基础字段和列表同步操作。
        /// </summary>
        private void DrawProfileRow(Rect rowRect, CustomCustomerKindRecord parentRecord, CustomCustomerProfileRecord profile, int profileIndex)
        {
            Widgets.DrawBoxSolid(rowRect, new Color(1f, 1f, 1f, 0.03f));
            SimUiStyle.DrawBorder(rowRect, new Color(1f, 1f, 1f, 0.05f));

            string oldLabel = profile.label ?? "";
            profile.label = Widgets.TextField(new Rect(rowRect.x + 8f, rowRect.y + 8f, 150f, 28f), oldLabel);
            if (!string.Equals(oldLabel, profile.label, StringComparison.Ordinal))
                dirty = true;
            DrawFloatField(new Rect(rowRect.x + 168f, rowRect.y + 8f, 136f, 28f), "权重", ref profile.weight, 0.01f, 100f);
            DrawIntField(new Rect(rowRect.x + 314f, rowRect.y + 8f, 150f, 28f), "预算下", ref profile.budgetMin, 1, 1000000);
            DrawIntField(new Rect(rowRect.x + 474f, rowRect.y + 8f, 150f, 28f), "预算上", ref profile.budgetMax, 1, 1000000);

            DrawIntField(new Rect(rowRect.x + 8f, rowRect.y + 44f, 150f, 28f), "耐心下", ref profile.queuePatienceMin, 60, 120000);
            DrawIntField(new Rect(rowRect.x + 168f, rowRect.y + 44f, 150f, 28f), "耐心上", ref profile.queuePatienceMax, 60, 120000);
            DrawFloatField(new Rect(rowRect.x + 328f, rowRect.y + 44f, 136f, 28f), "开始", ref profile.activeHourMin, 0f, 24f);
            DrawFloatField(new Rect(rowRect.x + 474f, rowRect.y + 44f, 136f, 28f), "结束", ref profile.activeHourMax, 0f, 24f);

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 84f, rowRect.width - 330f, Text.LineHeightOf(GameFont.Tiny) + 2f),
                $"天气 {profile.allowedWeatherDefNames.Count}  类型 {profile.preferredGoodsCategoryIds.Count}  偏好物品 {profile.preferredThingDefNames.Count}");

            float actionY = rowRect.y + 80f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(rowRect.xMax - 318f, actionY, 76f, 28f), "同步天气", true, GameFont.Tiny))
            {
                profile.allowedWeatherDefNames = parentRecord.allowedWeatherDefNames.ToList();
                dirty = true;
            }
            if (SimUiStyle.DrawSecondaryButton(new Rect(rowRect.xMax - 236f, actionY, 76f, 28f), "同步类型", true, GameFont.Tiny))
            {
                profile.preferredGoodsCategoryIds = parentRecord.targetGoodsCategoryIds.ToList();
                dirty = true;
            }
            if (SimUiStyle.DrawSecondaryButton(new Rect(rowRect.xMax - 154f, actionY, 76f, 28f), "同步偏好", true, GameFont.Tiny))
            {
                profile.preferredThingDefNames = parentRecord.itemPreferences
                    .Where(pref => !string.IsNullOrEmpty(pref.preferredThingDefName))
                    .Select(pref => pref.preferredThingDefName)
                    .Distinct()
                    .ToList();
                dirty = true;
            }
            if (SimUiStyle.DrawDangerButton(new Rect(rowRect.xMax - 72f, actionY, 64f, 28f), "删除", true, GameFont.Tiny))
            {
                parentRecord.spawnProfiles.RemoveAt(profileIndex);
                MarkDirtyAndRebuild();
                return;
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制编辑分区通用的视觉边框和标题。
        /// </summary>
        private static void DrawSection(Rect rect, string title)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.07f));
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 10f, rect.width - 28f, Text.LineHeightOf(GameFont.Small) + 4f), title);
        }

        /// <summary>
        /// 使用安全文本高度绘制只读行。
        /// </summary>
        private static void DrawReadOnlyLine(Rect rect, string text)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制小型只读提示面板。
        /// </summary>
        private static void DrawReadOnlyPanel(Rect rect, string text)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.06f));
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(rect.ContractedBy(10f), text);
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制带标题的文本行列表面板。
        /// </summary>
        private static void DrawStringChipList(Rect rect, List<string> rows, string heading)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.06f));
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), heading);

            float y = rect.y + 32f;
            if (rows.NullOrEmpty())
            {
                Widgets.Label(new Rect(rect.x + 10f, y, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), "无");
            }
            else
            {
                for (int i = 0; i < rows.Count && y + 22f <= rect.yMax; i++)
                {
                    GUI.color = Color.white;
                    Widgets.Label(new Rect(rect.x + 10f, y, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), rows[i].Truncate(rect.width - 20f));
                    y += 22f;
                }
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制带标签的整数输入框，并在解析成功时更新数值。
        /// </summary>
        private void DrawIntField(Rect rect, string label, ref int value, int min, int max)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x, rect.y, 74f, rect.height), label);
            string buffer = Widgets.TextField(new Rect(rect.x + 76f, rect.y, rect.width - 76f, rect.height), value.ToString());
            if (int.TryParse(buffer, out int parsed))
            {
                int clamped = Mathf.Clamp(parsed, min, max);
                if (clamped != value)
                {
                    value = clamped;
                    dirty = true;
                }
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制带标签的浮点数输入框，并在解析成功时更新数值。
        /// </summary>
        private void DrawFloatField(Rect rect, string label, ref float value, float min, float max)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x, rect.y, 74f, rect.height), label);
            string buffer = Widgets.TextField(new Rect(rect.x + 76f, rect.y, rect.width - 76f, rect.height), value.ToString("0.##"));
            if (float.TryParse(buffer, out float parsed))
            {
                float clamped = Mathf.Clamp(parsed, min, max);
                if (Math.Abs(clamped - value) > 0.001f)
                {
                    value = clamped;
                    dirty = true;
                }
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 为自定义顾客类型绘制可搜索的 PawnKind 浏览器。
        /// </summary>
        private void DrawPawnKindBrowser(Rect rect, CustomCustomerKindRecord record)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.06f));
            pawnKindSearch = Widgets.TextField(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 28f), pawnKindSearch);
            List<PawnKindDef> filtered = allPawnKinds.Where(def => MatchesSearch(def.defName, def.LabelCap.RawText, pawnKindSearch)).ToList();
            DrawPager(new Rect(rect.x + 10f, rect.y + 42f, rect.width - 20f, 28f), filtered.Count, ref pawnKindPageIndex);
            List<PawnKindDef> page = filtered.Skip(pawnKindPageIndex * BrowserPageSize).Take(BrowserPageSize).ToList();
            Rect listRect = new Rect(rect.x + 10f, rect.y + 74f, rect.width - 20f, rect.height - 84f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - ScrollbarWidth, Mathf.Max(listRect.height, page.Count * 46f));
            Widgets.BeginScrollView(listRect, ref pawnKindBrowserScroll, viewRect);
            for (int i = 0; i < page.Count; i++)
            {
                PawnKindDef def = page[i];
                Rect row = new Rect(0f, i * 46f, viewRect.width, 42f);
                DrawKindAppendRow(row, def.LabelCap.RawText, def.defName, record.pawnKindDefNames.Contains(def.defName), () =>
                {
                    record.pawnKindDefNames.Add(def.defName);
                    MarkDirtyAndRebuild();
                }, () =>
                {
                    record.pawnKindDefNames.RemoveAll(name => name == def.defName);
                    MarkDirtyAndRebuild();
                });
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制 PawnKind 浏览器中以 label 为主的信息行。
        /// </summary>
        private static void DrawKindAppendRow(Rect row, string label, string defName, bool exists, Action addAction, Action removeAction)
        {
            Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, Mouse.IsOver(row) ? 0.05f : 0.02f));
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + 8f, row.y + 4f, row.width - 94f, Text.LineHeightOf(GameFont.Small) + 2f), label.Truncate(row.width - 94f));
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(row.x + 8f, row.y + 26f, row.width - 94f, Text.LineHeightOf(GameFont.Tiny) + 2f), defName.Truncate(row.width - 94f));

            if (exists)
            {
                if (removeAction != null && SimUiStyle.DrawDangerButton(new Rect(row.xMax - 74f, row.y + 8f, 66f, 26f), "移除", true, GameFont.Tiny))
                    removeAction();
            }
            else if (SimUiStyle.DrawPrimaryButton(new Rect(row.xMax - 74f, row.y + 8f, 66f, 26f), "追加", true, GameFont.Tiny))
            {
                addAction?.Invoke();
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制运行时目标商品类型 ID 的勾选项。
        /// </summary>
        private void DrawGoodsCategoryToggles(Rect rect, List<string> selectedIds, string title)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.06f));
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), title + "，为空表示不限制");
            Rect listRect = new Rect(rect.x + 8f, rect.y + 32f, rect.width - 16f, Mathf.Max(24f, rect.height - 40f));
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), Mathf.Max(listRect.height, allGoodsCategories.Count * 28f));
            Widgets.BeginScrollView(listRect, ref targetGoodsScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < allGoodsCategories.Count; i++)
            {
                RuntimeGoodsCategory category = allGoodsCategories[i];
                bool value = selectedIds.Contains(category.categoryId);
                bool old = value;
                Widgets.CheckboxLabeled(new Rect(0f, y, viewRect.width, 24f), category.label.Truncate(viewRect.width - 34f), ref value);
                if (value != old)
                {
                    if (value) selectedIds.Add(category.categoryId);
                    else selectedIds.RemoveAll(id => string.Equals(id, category.categoryId, StringComparison.OrdinalIgnoreCase));
                    MarkDirtyAndRebuild();
                    Widgets.EndScrollView();
                    return;
                }
                y += 28f;
            }
            Widgets.EndScrollView();
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制允许天气过滤条件的勾选项。
        /// </summary>
        private void DrawWeatherToggles(Rect rect, List<string> selectedIds, string title)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.06f));
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), title + "，为空表示不限制");
            Rect listRect = new Rect(rect.x + 8f, rect.y + 32f, rect.width - 16f, Mathf.Max(24f, rect.height - 40f));
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), Mathf.Max(listRect.height, allWeatherDefs.Count * 28f));
            Widgets.BeginScrollView(listRect, ref weatherScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < allWeatherDefs.Count; i++)
            {
                WeatherDef weather = allWeatherDefs[i];
                bool value = selectedIds.Contains(weather.defName);
                bool old = value;
                Widgets.CheckboxLabeled(new Rect(0f, y, viewRect.width, 24f), weather.LabelCap.RawText.Truncate(viewRect.width - 34f), ref value);
                if (value != old)
                {
                    if (value) selectedIds.Add(weather.defName);
                    else selectedIds.RemoveAll(id => string.Equals(id, weather.defName, StringComparison.OrdinalIgnoreCase));
                    MarkDirtyAndRebuild();
                    Widgets.EndScrollView();
                    return;
                }
                y += 28f;
            }
            Widgets.EndScrollView();
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制自定义偏好记录及其删除操作。
        /// </summary>
        private void DrawPreferenceList(Rect rect, CustomCustomerKindRecord record)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.06f));
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), "偏好项会提高购买权重，可直接调整倍率。");
            Rect listRect = new Rect(rect.x + 8f, rect.y + 32f, rect.width - 16f, Mathf.Max(32f, rect.height - 40f));
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), Mathf.Max(listRect.height, record.itemPreferences.Count * 48f));
            Widgets.BeginScrollView(listRect, ref preferenceListScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < record.itemPreferences.Count; i++)
            {
                CustomCustomerPreferenceRecord pref = record.itemPreferences[i];
                Rect row = new Rect(0f, y, viewRect.width, 42f);
                Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 8f, row.y + 5f, row.width - 212f, Text.LineHeightOf(GameFont.Tiny) + 2f), FormatPreference(pref).Truncate(row.width - 212f));
                DrawFloatField(new Rect(row.xMax - 198f, row.y + 7f, 126f, 28f), "倍率", ref pref.weight, 1f, 20f);
                if (SimUiStyle.DrawDangerButton(new Rect(row.xMax - 66f, row.y + 7f, 58f, 28f), "删除", true, GameFont.Tiny))
                {
                    record.itemPreferences.RemoveAt(i);
                    MarkDirtyAndRebuild();
                    Widgets.EndScrollView();
                    return;
                }
                y += 46f;
            }
            Widgets.EndScrollView();
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制可追加为偏好的运行时商品类型。
        /// </summary>
        private void DrawPreferenceCategoryToggles(Rect rect, CustomCustomerKindRecord record)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.06f));
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), "偏好商品类型，会参与购买偏好判断。");
            Rect listRect = new Rect(rect.x + 8f, rect.y + 32f, rect.width - 16f, Mathf.Max(32f, rect.height - 40f));
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), Mathf.Max(listRect.height, allGoodsCategories.Count * 32f));
            Widgets.BeginScrollView(listRect, ref preferenceCategoryScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < allGoodsCategories.Count; i++)
            {
                RuntimeGoodsCategory category = allGoodsCategories[i];
                CustomCustomerPreferenceRecord existing = record.itemPreferences.FirstOrDefault(pref =>
                    string.Equals(pref.preferredGoodsCategoryId, category.categoryId, StringComparison.OrdinalIgnoreCase));
                bool selected = existing != null;
                bool old = selected;
                Widgets.CheckboxLabeled(new Rect(0f, y, viewRect.width - 126f, 24f), category.label.Truncate(viewRect.width - 160f), ref selected);
                if (selected != old)
                {
                    if (selected)
                    {
                        record.itemPreferences.Add(new CustomCustomerPreferenceRecord
                        {
                            preferredGoodsCategoryId = category.categoryId,
                            weight = 2f
                        });
                    }
                    else
                    {
                        record.itemPreferences.RemoveAll(pref => string.Equals(pref.preferredGoodsCategoryId, category.categoryId, StringComparison.OrdinalIgnoreCase));
                    }
                    MarkDirtyAndRebuild();
                    Widgets.EndScrollView();
                    return;
                }

                if (existing != null)
                    DrawFloatField(new Rect(viewRect.xMax - 116f, y - 2f, 108f, 28f), "倍率", ref existing.weight, 1f, 20f);
                y += 32f;
            }
            Widgets.EndScrollView();
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制用于添加偏好物品的可搜索 ThingDef 浏览器。
        /// </summary>
        private void DrawPreferenceThingBrowser(Rect rect, CustomCustomerKindRecord record)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.06f));
            preferenceThingSearch = Widgets.TextField(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 28f), preferenceThingSearch);
            List<ThingDef> filtered = allPreferenceThings.Where(def => MatchesSearch(def.defName, def.LabelCap.RawText, preferenceThingSearch)).ToList();
            DrawPager(new Rect(rect.x + 10f, rect.y + 42f, rect.width - 20f, 28f), filtered.Count, ref preferencePageIndex);
            List<ThingDef> page = filtered.Skip(preferencePageIndex * BrowserPageSize).Take(BrowserPageSize).ToList();
            Rect listRect = new Rect(rect.x + 10f, rect.y + 74f, rect.width - 20f, rect.height - 84f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - ScrollbarWidth, Mathf.Max(listRect.height, page.Count * 34f));
            Widgets.BeginScrollView(listRect, ref preferenceBrowserScroll, viewRect);
            for (int i = 0; i < page.Count; i++)
            {
                ThingDef def = page[i];
                Rect row = new Rect(0f, i * 34f, viewRect.width, 30f);
                bool exists = record.itemPreferences.Any(pref => pref.preferredThingDefName == def.defName);
                DrawAppendRow(row, def.LabelCap.RawText, def.defName, exists, () =>
                {
                    record.itemPreferences.Add(new CustomCustomerPreferenceRecord { preferredThingDefName = def.defName, weight = 2f });
                    MarkDirtyAndRebuild();
                }, null);
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制浏览器共用的一行追加或移除控件。
        /// </summary>
        private static void DrawAppendRow(Rect row, string label, string defName, bool exists, Action addAction, Action removeAction)
        {
            Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, Mouse.IsOver(row) ? 0.05f : 0.02f));
            Text.Font = GameFont.Tiny;
            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + 8f, row.y + 3f, row.width - 92f, Text.LineHeightOf(GameFont.Tiny) + 2f), label.Truncate(row.width - 92f));
            GUI.color = MutedText;
            Widgets.Label(new Rect(row.x + 8f, row.y + 17f, row.width - 92f, Text.LineHeightOf(GameFont.Tiny) + 2f), defName.Truncate(row.width - 92f));
            if (exists)
            {
                if (removeAction != null && SimUiStyle.DrawDangerButton(new Rect(row.xMax - 74f, row.y + 3f, 66f, 24f), "移除", true, GameFont.Tiny))
                    removeAction();
                else if (removeAction == null)
                    DrawBadge(new Rect(row.xMax - 74f, row.y + 6f, 66f, 18f), "已存在", BuiltInBadge);
            }
            else if (SimUiStyle.DrawPrimaryButton(new Rect(row.xMax - 74f, row.y + 3f, 66f, 24f), "追加", true, GameFont.Tiny))
            {
                addAction?.Invoke();
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制可搜索浏览器的分页控件。
        /// </summary>
        private static void DrawPager(Rect rect, int totalCount, ref int pageIndex)
        {
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)BrowserPageSize));
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x, rect.y + 6f, 96f, Text.LineHeightOf(GameFont.Tiny) + 2f), $"共 {totalCount} 项");
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 188f, rect.y, 72f, 26f), "上一页", pageIndex > 0, GameFont.Tiny))
                pageIndex--;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.xMax - 110f, rect.y + 3f, 48f, 20f), $"{pageIndex + 1}/{pageCount}");
            Text.Anchor = TextAnchor.UpperLeft;
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 58f, rect.y, 58f, 26f), "下一页", pageIndex < pageCount - 1, GameFont.Tiny))
                pageIndex++;
            GUI.color = Color.white;
        }

        /// <summary>
        /// 加载磁盘数据并重建全部预览列表。
        /// </summary>
        private void LoadDraft()
        {
            RefreshGoodsCategories(false);
            draftData = CustomCustomerDatabase.Load();
            allPawnKinds = CustomCustomerDatabase.GetAllCandidatePawnKinds();
            allPreferenceThings = CustomCustomerDatabase.GetAllCandidatePreferenceThings();
            allWeatherDefs = CustomCustomerDatabase.GetAllWeatherDefs();
            RebuildPreviewFromDraft();
            EnsureValidSelection();
            dirty = false;
        }

        /// <summary>
        /// 从运行时商品目录重新读取可选商品类型。
        /// </summary>
        private void RefreshGoodsCategories(bool showMessage)
        {
            GoodsCatalog.NotifyCatalogChanged();
            GoodsCatalog.EnsureInitialized();
            allGoodsCategories = GoodsCatalog.Categories?
                .OrderBy(category => category.IsBuiltInCategory ? 0 : 1)
                .ThenBy(category => category.label)
                .ToList() ?? new List<RuntimeGoodsCategory>();

            if (showMessage)
                Messages.Message($"已刷新运行时商品类型：{allGoodsCategories.Count} 个。", MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>
        /// 通过合并 Def 顾客类型和玩家记录重建预览数据。
        /// </summary>
        private void RebuildPreviewFromDraft()
        {
            List<RuntimeCustomerKind> list = new List<RuntimeCustomerKind>();
            list.AddRange(DefDatabase<SimDef.CustomerKindDef>.AllDefsListForReading.Where(def => def != null).Select(RuntimeCustomerKind.FromDef).Where(kind => kind != null));
            list.AddRange((draftData?.kinds ?? new List<CustomCustomerKindRecord>()).Select(RuntimeCustomerKind.FromCustomRecord).Where(kind => kind != null));
            previewKinds = list
                .OrderBy(kind => kind.sourceDef != null ? 0 : 1)
                .ThenBy(kind => kind.label)
                .ToList();
        }

        /// <summary>
        /// 在重建后保持当前选择指向有效条目。
        /// </summary>
        private void EnsureValidSelection()
        {
            if (previewKinds.Count == 0)
            {
                selectedKindId = string.Empty;
                selectedLabelBuffer = string.Empty;
                return;
            }

            RuntimeCustomerKind selected = GetSelectedKind() ?? previewKinds[0];
            selectedKindId = selected.kindId;
            selectedLabelBuffer = selected.label;
        }

        /// <summary>
        /// 返回当前选中的预览顾客类型。
        /// </summary>
        private RuntimeCustomerKind GetSelectedKind()
        {
            return previewKinds.FirstOrDefault(kind => kind.kindId == selectedKindId);
        }

        /// <summary>
        /// 返回指定自定义顾客类型 ID 对应的可编辑草稿记录。
        /// </summary>
        private CustomCustomerKindRecord GetDraftRecord(string kindId)
        {
            return draftData?.kinds?.FirstOrDefault(record => string.Equals(record.kindId, kindId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 使用第一个可用 PawnKind 作为安全种子创建新的运行时顾客类型。
        /// </summary>
        private void CreateKind()
        {
            string label = CustomCustomerDatabase.NormalizeLabel(newKindLabelBuffer);
            if (string.IsNullOrEmpty(label)) return;
            if (allPawnKinds.Count == 0)
            {
                Messages.Message("没有可用的 PawnKindDef。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            string kindId = CustomCustomerDatabase.GenerateUniqueKindId(label, previewKinds.Select(kind => kind.kindId));
            draftData.kinds.Add(new CustomCustomerKindRecord
            {
                kindId = kindId,
                label = label,
                pawnKindDefNames = new List<string> { allPawnKinds[0].defName }
            });

            newKindLabelBuffer = string.Empty;
            selectedKindId = kindId;
            selectedLabelBuffer = label;
            MarkDirtyAndRebuild();
        }

        /// <summary>
        /// 重命名当前选中的自定义顾客类型。
        /// </summary>
        private void RenameSelectedKind()
        {
            CustomCustomerKindRecord record = GetDraftRecord(selectedKindId);
            if (record == null) return;
            string label = CustomCustomerDatabase.NormalizeLabel(selectedLabelBuffer);
            if (string.IsNullOrEmpty(label))
            {
                Messages.Message("顾客名称不能为空。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            record.label = label;
            MarkDirtyAndRebuild();
        }

        /// <summary>
        /// 删除当前自定义顾客类型前弹出确认窗口。
        /// </summary>
        private void ConfirmDeleteKind()
        {
            if (GetDraftRecord(selectedKindId) == null) return;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("删除这个自定义顾客 Kind？", DeleteSelectedKind));
        }

        /// <summary>
        /// 从草稿数据中删除当前自定义顾客类型。
        /// </summary>
        private void DeleteSelectedKind()
        {
            draftData.kinds.RemoveAll(record => string.Equals(record.kindId, selectedKindId, StringComparison.OrdinalIgnoreCase));
            MarkDirtyAndRebuild();
            EnsureValidSelection();
        }

        /// <summary>
        /// 根据父级顾客类型数值添加一个简单生成档案。
        /// </summary>
        private void AddProfile(CustomCustomerKindRecord record)
        {
            string label = CustomCustomerDatabase.NormalizeLabel(profileLabelBuffer);
            if (string.IsNullOrEmpty(label)) return;
            record.spawnProfiles.Add(new CustomCustomerProfileRecord
            {
                label = label,
                budgetMin = record.budgetMin,
                budgetMax = record.budgetMax,
                queuePatienceMin = record.queuePatienceMin,
                queuePatienceMax = record.queuePatienceMax,
                activeHourMin = record.activeHourMin,
                activeHourMax = record.activeHourMax
            });
            profileLabelBuffer = string.Empty;
            MarkDirtyAndRebuild();
        }

        /// <summary>
        /// 保存草稿数据并重建运行时顾客目录。
        /// </summary>
        private void SaveDraft()
        {
            CustomCustomerDatabase.Save(draftData);
            CustomCustomerDatabase.NotifyRuntimeChanged();
            LoadDraft();
            dirty = false;
            Messages.Message("自定义顾客注册已保存，运行时顾客目录已重建。", MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>
        /// 在确认放弃未保存改动后重新加载磁盘数据。
        /// </summary>
        private void ConfirmReload()
        {
            if (!dirty)
            {
                LoadDraft();
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("重新加载会丢弃当前未保存的自定义顾客改动，是否继续？", LoadDraft));
        }

        /// <summary>
        /// 使用导入的顾客数据替换草稿并保存。
        /// </summary>
        private void HandleImportReplace(CustomCustomerDatabaseData imported)
        {
            draftData = imported ?? new CustomCustomerDatabaseData();
            RebuildPreviewFromDraft();
            EnsureValidSelection();
            SaveDraft();
        }

        /// <summary>
        /// 标记数据已修改并重建预览模型。
        /// </summary>
        private void MarkDirtyAndRebuild()
        {
            dirty = true;
            RebuildPreviewFromDraft();
            EnsureValidSelection();
        }

        /// <summary>
        /// 判断两个可搜索字符串是否命中查询文本。
        /// </summary>
        private static bool MatchesSearch(string defName, string label, string search)
        {
            return string.IsNullOrEmpty(search)
                || (!string.IsNullOrEmpty(defName) && defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrEmpty(label) && label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// 将运行时偏好格式化为显示文本。
        /// </summary>
        private static string FormatPreference(RuntimeItemPreference pref)
        {
            if (pref == null) return "";
            if (pref.preferredThing != null) return $"{pref.preferredThing.LabelCap.RawText} x{pref.weight:F1}";
            return $"{pref.preferredGoodsCategoryId} x{pref.weight:F1}";
        }

        /// <summary>
        /// 将自定义偏好记录格式化为显示文本。
        /// </summary>
        private static string FormatPreference(CustomCustomerPreferenceRecord pref)
        {
            if (pref == null) return "";
            if (!string.IsNullOrEmpty(pref.preferredThingDefName)) return $"{pref.preferredThingDefName} x{pref.weight:F1}";
            return $"{pref.preferredGoodsCategoryId} x{pref.weight:F1}";
        }

        /// <summary>
        /// 绘制紧凑的来源或状态徽标，并恢复文本状态。
        /// </summary>
        private static void DrawBadge(Rect rect, string label, Color fill)
        {
            Widgets.DrawBoxSolid(rect, fill);
            SimUiStyle.DrawBorder(rect, new Color(fill.r, fill.g, fill.b, 0.55f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        /// <summary>
        /// 显示可导出的 Base64 文本。
        /// </summary>
        private sealed class Dialog_TextTransfer : Window
        {
            private readonly string title;
            private readonly string info;
            private string text;
            private Vector2 scroll;

            public override Vector2 InitialSize => new Vector2(900f, 520f);

            /// <summary>
            /// 初始化导出窗口。
            /// </summary>
            public Dialog_TextTransfer(string title, string info, string text)
            {
                this.title = title;
                this.info = info;
                this.text = text ?? string.Empty;
                forcePause = true;
                absorbInputAroundWindow = true;
                doCloseX = true;
                closeOnClickedOutside = false;
            }

            /// <summary>
            /// 绘制导出文本和复制操作。
            /// </summary>
            public override void DoWindowContents(Rect inRect)
            {
                Text.Font = GameFont.Medium;
                GUI.color = Color.white;
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - CloseXReservedWidth, Text.LineHeightOf(GameFont.Medium) + 4f), title);
                Text.Font = GameFont.Tiny;
                GUI.color = MutedText;
                Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, Text.LineHeightOf(GameFont.Tiny) * 2f + 4f), info);
                Rect textRect = new Rect(inRect.x, inRect.y + 74f, inRect.width, Mathf.Max(80f, inRect.height - 122f));
                Widgets.DrawBoxSolid(textRect, new Color(0f, 0f, 0f, 0.22f));
                SimUiStyle.DrawBorder(textRect, new Color(1f, 1f, 1f, 0.10f));
                Rect viewRect = new Rect(0f, 0f, textRect.width - ScrollbarWidth, Mathf.Max(textRect.height, Text.CalcHeight(text, textRect.width - 24f) + 12f));
                Widgets.BeginScrollView(textRect.ContractedBy(4f), ref scroll, viewRect);
                text = Widgets.TextArea(viewRect, text);
                Widgets.EndScrollView();
                if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 130f, inRect.yMax - 38f, 130f, 32f), "复制到剪贴板"))
                {
                    GUIUtility.systemCopyBuffer = text;
                    Messages.Message("Base64 导出内容已复制到剪贴板。", MessageTypeDefOf.PositiveEvent, false);
                }
                GUI.color = Color.white;
            }
        }

        /// <summary>
        /// 接收 Base64 顾客注册文本并执行导入。
        /// </summary>
        private sealed class Dialog_TextImport : Window
        {
            private readonly Action<CustomCustomerDatabaseData> importAction;
            private string text = string.Empty;
            private Vector2 scroll;

            public override Vector2 InitialSize => new Vector2(900f, 560f);

            /// <summary>
            /// 初始化导入窗口。
            /// </summary>
            public Dialog_TextImport(Action<CustomCustomerDatabaseData> importAction)
            {
                this.importAction = importAction;
                forcePause = true;
                absorbInputAroundWindow = true;
                doCloseX = true;
                closeOnClickedOutside = false;
            }

            /// <summary>
            /// 绘制导入控件和确认操作。
            /// </summary>
            public override void DoWindowContents(Rect inRect)
            {
                Text.Font = GameFont.Medium;
                GUI.color = Color.white;
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - CloseXReservedWidth, Text.LineHeightOf(GameFont.Medium) + 4f), "导入并覆盖");
                Text.Font = GameFont.Tiny;
                GUI.color = MutedText;
                Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, Text.LineHeightOf(GameFont.Tiny) * 2f + 4f), "导入后会覆盖玩家本地所有自定义顾客数据，但不会改动任何 CustomerKindDef。");
                Rect textRect = new Rect(inRect.x, inRect.y + 74f, inRect.width, Mathf.Max(90f, inRect.height - 122f));
                Widgets.DrawBoxSolid(textRect, new Color(0f, 0f, 0f, 0.22f));
                SimUiStyle.DrawBorder(textRect, new Color(1f, 1f, 1f, 0.10f));
                Rect viewRect = new Rect(0f, 0f, textRect.width - ScrollbarWidth, Mathf.Max(textRect.height, Text.CalcHeight(text, textRect.width - 24f) + 12f));
                Widgets.BeginScrollView(textRect.ContractedBy(4f), ref scroll, viewRect);
                text = Widgets.TextArea(viewRect, text);
                Widgets.EndScrollView();
                if (SimUiStyle.DrawSecondaryButton(new Rect(inRect.x, inRect.yMax - 38f, 130f, 32f), "从剪贴板粘贴"))
                    text = GUIUtility.systemCopyBuffer ?? string.Empty;
                if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 130f, inRect.yMax - 38f, 130f, 32f), "确认导入"))
                {
                    if (!CustomCustomerDatabase.TryImportBase64(text, out CustomCustomerDatabaseData data, out string error))
                    {
                        Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                    importAction?.Invoke(data);
                    Close();
                    Messages.Message("自定义顾客数据已导入并覆盖本地内容。", MessageTypeDefOf.PositiveEvent, false);
                }
                GUI.color = Color.white;
            }
        }
    }
}
