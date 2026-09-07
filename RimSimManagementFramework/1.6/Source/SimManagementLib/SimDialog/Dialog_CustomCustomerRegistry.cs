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
            Widgets.Label(titleRect, SimTranslation.T("RSMF.CustomCustomer.Title"));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Rect descRect = new Rect(rect.x + 20f, titleRect.yMax + 4f, textWidth, Text.LineHeightOf(GameFont.Tiny) * 2f + 6f);
            Widgets.Label(descRect, SimTranslation.T("RSMF.CustomCustomer.Description"));

            float buttonY = rect.y + 18f;
            float right = rect.xMax - 16f - CloseXReservedWidth;

            const float actionButtonWidth = 96f;
            const float actionButtonGap = 10f;

            right -= actionButtonWidth;
            if (SimUiStyle.DrawPrimaryButton(new Rect(right, buttonY, actionButtonWidth, 34f), dirty ? SimTranslation.T("RSMF.CustomCustomer.SaveDirty") : SimTranslation.T("RSMF.CustomCustomer.Save")))
                SaveDraft();

            right -= actionButtonWidth + actionButtonGap;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, actionButtonWidth, 34f), SimTranslation.T("RSMF.CustomCustomer.Reload")))
                ConfirmReload();

            right -= actionButtonWidth + actionButtonGap;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, actionButtonWidth, 34f), SimTranslation.T("RSMF.CustomCustomer.RefreshTypes")))
                RefreshGoodsCategories(true);

            right -= actionButtonWidth + actionButtonGap;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, actionButtonWidth, 34f), SimTranslation.T("RSMF.CustomCustomer.ExportBase64")))
                Find.WindowStack.Add(new Dialog_TextTransfer(SimTranslation.T("RSMF.CustomCustomer.ExportTitle"), SimTranslation.T("RSMF.CustomCustomer.ExportInfo"), CustomCustomerDatabase.ExportBase64(draftData)));

            right -= actionButtonWidth + actionButtonGap;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, actionButtonWidth, 34f), SimTranslation.T("RSMF.CustomCustomer.ImportBase64")))
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
            Widgets.Label(titleRect, SimTranslation.T("RSMF.CustomCustomer.KindListTitle"));

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
            Widgets.Label(new Rect(footerRect.x + 10f, footerRect.y + 8f, footerRect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.NewRuntime"));
            newKindLabelBuffer = Widgets.TextField(new Rect(footerRect.x + 10f, footerRect.y + 30f, footerRect.width - 108f, 28f), newKindLabelBuffer);
            if (SimUiStyle.DrawPrimaryButton(new Rect(footerRect.xMax - 90f, footerRect.y + 29f, 80f, 30f), SimTranslation.T("RSMF.CustomCustomer.Create"), !string.IsNullOrWhiteSpace(newKindLabelBuffer), GameFont.Tiny))
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
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 58f, titleWidth - 92f, Text.LineHeightOf(GameFont.Tiny) + 2f), $"PawnKind {kind.pawnKindDefs.Count}  {SimTranslation.T("RSMF.CustomCustomer.BudgetMin")} {kind.budgetRange.min}-{kind.budgetRange.max}");

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
                Widgets.Label(rect, SimTranslation.T("RSMF.CustomCustomer.NoKinds"));
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
            y = DrawPriceSensitivitySettings(new Rect(0f, y, viewRect.width, 112f), selected);
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
            return 126f + 138f + 112f + 310f + 250f + 340f + EstimateProfilesHeight(selected) + SectionGap * 6f + 24f;
        }

        /// <summary>
        /// 估算顾客档案编辑区所需高度。
        /// </summary>
        private float EstimateProfilesHeight(RuntimeCustomerKind selected)
        {
            int profileCount = GetDraftRecord(selected.kindId)?.spawnProfiles?.Count ?? selected.spawnProfiles.Count;
            return Mathf.Max(296f, 104f + profileCount * 174f);
        }

        /// <summary>
        /// 绘制当前顾客类型的名称、ID、来源和删除控件。
        /// </summary>
        private float DrawKindInfo(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, SimTranslation.T("RSMF.CustomCustomer.Section.BasicInfo"));
            bool editable = kind.sourceDef == null;
            float right = rect.xMax - 14f;

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 42f, 110f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.DisplayName"));
            if (editable)
            {
                selectedLabelBuffer = Widgets.TextField(new Rect(rect.x + 126f, rect.y + 36f, 220f, 28f), selectedLabelBuffer ?? kind.label);
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.x + 356f, rect.y + 35f, 70f, 30f), SimTranslation.T("RSMF.CustomCustomer.Rename"), true, GameFont.Tiny))
                    RenameSelectedKind();
                if (SimUiStyle.DrawDangerButton(new Rect(right - 100f, rect.y + 35f, 100f, 30f), SimTranslation.T("RSMF.CustomCustomer.DeleteKind"), true, GameFont.Tiny))
                    ConfirmDeleteKind();
            }
            else
            {
                GUI.color = Color.white;
                Widgets.Label(new Rect(rect.x + 126f, rect.y + 40f, rect.width - 260f, Text.LineHeightOf(GameFont.Tiny) + 2f), kind.label);
                DrawBadge(new Rect(right - 96f, rect.y + 38f, 96f, 20f), SimTranslation.T("RSMF.CustomCustomer.DefReadonly"), LockedBadge);
            }

            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 74f, rect.width - 28f, Text.LineHeightOf(GameFont.Tiny) + 2f), "ID: " + kind.kindId);
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 94f, rect.width - 28f, Text.LineHeightOf(GameFont.Tiny) + 2f), editable ? SimTranslation.T("RSMF.CustomCustomer.SourceCustom") : SimTranslation.T("RSMF.CustomCustomer.SourceDef"));

            GUI.color = Color.white;
            return rect.yMax;
        }

        /// <summary>
        /// 绘制顾客价格敏感度设置，负责控制折扣加权、溢价降权和拒买阈值。
        /// </summary>
        private float DrawPriceSensitivitySettings(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, SimTranslation.T("RSMF.CustomCustomer.Section.PriceSensitivity"));
            CustomCustomerKindRecord record = GetDraftRecord(kind.kindId);
            bool editable = record != null;

            CustomerPriceSensitivityProps props = editable
                ? EnsurePriceSensitivity(record)
                : CustomerPriceSensitivityProps.Resolve(kind.priceSensitivity);

            if (editable)
            {
                DrawPriceSensitivityFields(rect, props);
            }
            else
            {
                DrawReadOnlyLine(new Rect(rect.x + 14f, rect.y + 42f, rect.width - 28f, Text.LineHeightOf(GameFont.Tiny) + 2f),
                    SimTranslation.T(
                        "RSMF.CustomCustomer.ReadOnlyPriceSensitivity",
                        props.discountWeightMultiplier.ToString("F2").Named("discount"),
                        props.softMarkupRatio.ToString("F2").Named("soft"),
                        props.rejectMarkupRatio.ToString("F2").Named("reject"),
                        props.complainMarkupRatio.ToString("F2").Named("complain")));
            }

            return rect.yMax;
        }

        /// <summary>
        /// 绘制自定义顾客的可编辑数值设置，以及 Def 顾客的只读数值。
        /// </summary>
        private float DrawNumericSettings(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, SimTranslation.T("RSMF.CustomCustomer.Section.RefreshBudget"));
            CustomCustomerKindRecord record = GetDraftRecord(kind.kindId);
            bool editable = record != null;

            if (editable)
            {
                DrawFloatField(new Rect(rect.x + 14f, rect.y + 42f, 190f, 30f), SimTranslation.T("RSMF.CustomCustomer.MtbDays"), ref record.baseMtbDays, 0.01f, 20f);
                DrawIntField(new Rect(rect.x + 218f, rect.y + 42f, 190f, 30f), SimTranslation.T("RSMF.CustomCustomer.BudgetMin"), ref record.budgetMin, 1, 1000000);
                DrawIntField(new Rect(rect.x + 422f, rect.y + 42f, 190f, 30f), SimTranslation.T("RSMF.CustomCustomer.BudgetMax"), ref record.budgetMax, 1, 1000000);
                DrawIntField(new Rect(rect.x + 14f, rect.y + 82f, 190f, 30f), SimTranslation.T("RSMF.CustomCustomer.PatienceMin"), ref record.queuePatienceMin, 60, 120000);
                DrawIntField(new Rect(rect.x + 218f, rect.y + 82f, 190f, 30f), SimTranslation.T("RSMF.CustomCustomer.PatienceMax"), ref record.queuePatienceMax, 60, 120000);
                DrawFloatField(new Rect(rect.x + 422f, rect.y + 82f, 190f, 30f), SimTranslation.T("RSMF.CustomCustomer.MinReputation"), ref record.minShopReputation, 0f, 100f);
                DrawFloatField(new Rect(rect.x + 626f, rect.y + 42f, 170f, 30f), SimTranslation.T("RSMF.CustomCustomer.StartHour"), ref record.activeHourMin, 0f, 24f);
                DrawFloatField(new Rect(rect.x + 626f, rect.y + 82f, 170f, 30f), SimTranslation.T("RSMF.CustomCustomer.EndHour"), ref record.activeHourMax, 0f, 24f);
            }
            else
            {
                DrawReadOnlyLine(new Rect(rect.x + 14f, rect.y + 42f, rect.width - 28f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.ReadOnlyBudget",
                    kind.baseMtbDays.ToString("F2").Named("mtb"),
                    kind.budgetRange.min.Named("min"),
                    kind.budgetRange.max.Named("max"),
                    kind.queuePatienceRange.min.Named("patienceMin"),
                    kind.queuePatienceRange.max.Named("patienceMax")));
                DrawReadOnlyLine(new Rect(rect.x + 14f, rect.y + 72f, rect.width - 28f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.ReadOnlyTime",
                    kind.activeHourRange.TrueMin.ToString("F1").Named("start"),
                    kind.activeHourRange.TrueMax.ToString("F1").Named("end"),
                    kind.minShopReputation.ToString("F0").Named("minReputation")));
            }

            return rect.yMax;
        }

        /// <summary>
        /// 绘制已选 PawnKind，并提供可搜索的 PawnKind 浏览器。
        /// </summary>
        private float DrawPawnKinds(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, SimTranslation.T("RSMF.CustomCustomer.Section.PawnKind"));
            CustomCustomerKindRecord record = GetDraftRecord(kind.kindId);
            bool editable = record != null;

            Rect listRect = new Rect(rect.x + 14f, rect.y + 38f, rect.width * 0.36f, rect.height - 52f);
            Rect browserRect = new Rect(listRect.xMax + 14f, listRect.y, rect.width - listRect.width - 42f, listRect.height);

            DrawStringChipList(listRect, kind.pawnKindDefs.Select(p => p.LabelCap.RawText + " / " + p.defName).ToList(), editable ? SimTranslation.T("RSMF.CustomCustomer.SelectedPawnKinds") : SimTranslation.T("RSMF.CustomCustomer.BuiltInPawnKinds"));
            if (editable)
                DrawPawnKindBrowser(browserRect, record);
            else
                DrawReadOnlyPanel(browserRect, SimTranslation.T("RSMF.CustomCustomer.CannotModifyPawnKind"));

            return rect.yMax;
        }

        /// <summary>
        /// 绘制目标商品类型和天气过滤条件。
        /// </summary>
        private float DrawTargetsAndWeather(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, SimTranslation.T("RSMF.CustomCustomer.Section.TargetWeather"));
            CustomCustomerKindRecord record = GetDraftRecord(kind.kindId);
            bool editable = record != null;

            Rect goodsRect = new Rect(rect.x + 14f, rect.y + 38f, (rect.width - 42f) * 0.5f, rect.height - 52f);
            Rect weatherRect = new Rect(goodsRect.xMax + 14f, goodsRect.y, goodsRect.width, goodsRect.height);

            if (editable)
            {
                DrawGoodsCategoryToggles(goodsRect, record.targetGoodsCategoryIds, SimTranslation.T("RSMF.CustomCustomer.TargetGoodsType"));
                DrawWeatherToggles(weatherRect, record.allowedWeatherDefNames, SimTranslation.T("RSMF.CustomCustomer.AllowedWeather"));
            }
            else
            {
                DrawStringChipList(goodsRect, kind.targetGoodsCategoryIds, SimTranslation.T("RSMF.CustomCustomer.TargetGoodsType") + SimTranslation.T("RSMF.CustomCustomer.EmptyMeansNoLimit"));
                DrawStringChipList(weatherRect, kind.allowedWeathers.Select(w => w.LabelCap.RawText + " / " + w.defName).ToList(), SimTranslation.T("RSMF.CustomCustomer.AllowedWeather") + SimTranslation.T("RSMF.CustomCustomer.EmptyMeansNoLimit"));
            }

            return rect.yMax;
        }

        /// <summary>
        /// 绘制自定义偏好记录，并提供用于追加偏好物品的 ThingDef 浏览器。
        /// </summary>
        private float DrawPreferences(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, SimTranslation.T("RSMF.CustomCustomer.Section.Preferences"));
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
                DrawStringChipList(leftRect, kind.itemPreferences.Select(FormatPreference).ToList(), SimTranslation.T("RSMF.CustomCustomer.BuiltInPreferences"));
                DrawReadOnlyPanel(middleRect, SimTranslation.T("RSMF.CustomCustomer.NoModifyDefPreferences"));
                DrawReadOnlyPanel(rightRect, SimTranslation.T("RSMF.CustomCustomer.AppendPreferences"));
            }

            return rect.yMax;
        }

        /// <summary>
        /// 绘制自定义顾客类型的轻量档案控制区。
        /// </summary>
        private void DrawProfiles(Rect rect, RuntimeCustomerKind kind)
        {
            DrawSection(rect, SimTranslation.T("RSMF.CustomCustomer.Section.Profiles"));
            CustomCustomerKindRecord record = GetDraftRecord(kind.kindId);
            bool editable = record != null;
            Rect listRect = new Rect(rect.x + 14f, rect.y + 38f, rect.width - 28f, rect.height - 52f);

            if (!editable)
            {
                DrawStringChipList(listRect, kind.spawnProfiles.Select(p => $"{p.label}  {SimTranslation.T("RSMF.CustomCustomer.Weight")} {p.weight:F1}  {SimTranslation.T("RSMF.CustomCustomer.BudgetMin")} {p.budgetRange.min}-{p.budgetRange.max}").ToList(), SimTranslation.T("RSMF.CustomCustomer.BuiltInProfiles"));
                return;
            }

            Widgets.DrawBoxSolid(listRect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(listRect, new Color(1f, 1f, 1f, 0.06f));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(listRect.x + 10f, listRect.y + 8f, listRect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.ProfilesTip"));

            float y = listRect.y + 34f;
            for (int i = 0; i < record.spawnProfiles.Count; i++)
            {
                CustomCustomerProfileRecord profile = record.spawnProfiles[i];
                Rect rowRect = new Rect(listRect.x + 10f, y, listRect.width - 20f, 164f);
                DrawProfileRow(rowRect, record, profile, i);
                y += 174f;
            }

            Rect inputRect = new Rect(listRect.x + 10f, listRect.yMax - 36f, listRect.width - 100f, 28f);
            profileLabelBuffer = Widgets.TextField(inputRect, profileLabelBuffer);
            if (SimUiStyle.DrawPrimaryButton(new Rect(listRect.xMax - 82f, listRect.yMax - 37f, 72f, 30f), SimTranslation.T("RSMF.CustomCustomer.Add"), !string.IsNullOrWhiteSpace(profileLabelBuffer), GameFont.Tiny))
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
            DrawFloatField(new Rect(rowRect.x + 168f, rowRect.y + 8f, 136f, 28f), SimTranslation.T("RSMF.CustomCustomer.Weight"), ref profile.weight, 0.01f, 100f);
            DrawIntField(new Rect(rowRect.x + 314f, rowRect.y + 8f, 150f, 28f), SimTranslation.T("RSMF.CustomCustomer.BudgetLower"), ref profile.budgetMin, 1, 1000000);
            DrawIntField(new Rect(rowRect.x + 474f, rowRect.y + 8f, 150f, 28f), SimTranslation.T("RSMF.CustomCustomer.BudgetUpper"), ref profile.budgetMax, 1, 1000000);

            DrawIntField(new Rect(rowRect.x + 8f, rowRect.y + 44f, 150f, 28f), SimTranslation.T("RSMF.CustomCustomer.PatienceLower"), ref profile.queuePatienceMin, 60, 120000);
            DrawIntField(new Rect(rowRect.x + 168f, rowRect.y + 44f, 150f, 28f), SimTranslation.T("RSMF.CustomCustomer.PatienceUpper"), ref profile.queuePatienceMax, 60, 120000);
            DrawFloatField(new Rect(rowRect.x + 328f, rowRect.y + 44f, 136f, 28f), SimTranslation.T("RSMF.CustomCustomer.Start"), ref profile.activeHourMin, 0f, 24f);
            DrawFloatField(new Rect(rowRect.x + 474f, rowRect.y + 44f, 136f, 28f), SimTranslation.T("RSMF.CustomCustomer.End"), ref profile.activeHourMax, 0f, 24f);
            DrawPriceSensitivityFields(new Rect(rowRect.x + 8f, rowRect.y, rowRect.width - 16f, 28f), EnsurePriceSensitivity(profile), rowRect.y + 80f);

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 132f, rowRect.width - 330f, Text.LineHeightOf(GameFont.Tiny) + 2f),
                SimTranslation.T("RSMF.CustomCustomer.ProfileWeatherCount",
                    profile.allowedWeatherDefNames.Count.Named("weather"),
                    profile.preferredGoodsCategoryIds.Count.Named("types"),
                    profile.preferredThingDefNames.Count.Named("items")));

            float actionY = rowRect.y + 134f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(rowRect.xMax - 318f, actionY, 76f, 28f), SimTranslation.T("RSMF.CustomCustomer.SyncWeather"), true, GameFont.Tiny))
            {
                profile.allowedWeatherDefNames = parentRecord.allowedWeatherDefNames.ToList();
                dirty = true;
            }
            if (SimUiStyle.DrawSecondaryButton(new Rect(rowRect.xMax - 236f, actionY, 76f, 28f), SimTranslation.T("RSMF.CustomCustomer.SyncType"), true, GameFont.Tiny))
            {
                profile.preferredGoodsCategoryIds = parentRecord.targetGoodsCategoryIds.ToList();
                dirty = true;
            }
            if (SimUiStyle.DrawSecondaryButton(new Rect(rowRect.xMax - 154f, actionY, 76f, 28f), SimTranslation.T("RSMF.CustomCustomer.SyncPreference"), true, GameFont.Tiny))
            {
                profile.preferredThingDefNames = parentRecord.itemPreferences
                    .Where(pref => !string.IsNullOrEmpty(pref.preferredThingDefName))
                    .Select(pref => pref.preferredThingDefName)
                    .Distinct()
                    .ToList();
                dirty = true;
            }
            if (SimUiStyle.DrawDangerButton(new Rect(rowRect.xMax - 72f, actionY, 64f, 28f), SimTranslation.T("RSMF.CustomCustomer.Delete"), true, GameFont.Tiny))
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
                Widgets.Label(new Rect(rect.x + 10f, y, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.None"));
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
        /// 绘制价格敏感度字段组，负责用紧凑布局编辑折扣、溢价和拒买参数。
        /// </summary>
        private void DrawPriceSensitivityFields(Rect rect, CustomerPriceSensitivityProps props)
        {
            DrawPriceSensitivityFields(rect, props, rect.y + 42f);
        }

        /// <summary>
        /// 绘制价格敏感度字段组，负责支持分区和档案行共用同一套控件。
        /// </summary>
        private void DrawPriceSensitivityFields(Rect rect, CustomerPriceSensitivityProps props, float fieldY)
        {
            if (props == null)
                return;

            float fieldW = Mathf.Max(112f, (rect.width - 56f) / 5f);
            DrawCompactFloatField(new Rect(rect.x + 14f, fieldY, fieldW, 44f), SimTranslation.T("RSMF.CustomCustomer.PriceDiscount"), ref props.discountWeightMultiplier, 0.1f, 10f);
            DrawCompactFloatField(new Rect(rect.x + 28f + fieldW, fieldY, fieldW, 44f), SimTranslation.T("RSMF.CustomCustomer.PriceSoft"), ref props.softMarkupRatio, 1f, 20f);
            DrawCompactFloatField(new Rect(rect.x + 42f + fieldW * 2f, fieldY, fieldW, 44f), SimTranslation.T("RSMF.CustomCustomer.PriceReject"), ref props.rejectMarkupRatio, 1.01f, 50f);
            DrawCompactFloatField(new Rect(rect.x + 56f + fieldW * 3f, fieldY, fieldW, 44f), SimTranslation.T("RSMF.CustomCustomer.PriceMinWeight"), ref props.overpricedMinWeight, 0.001f, 1f);
            DrawCompactFloatField(new Rect(rect.x + 70f + fieldW * 4f, fieldY, fieldW, 44f), SimTranslation.T("RSMF.CustomCustomer.PriceComplain"), ref props.complainMarkupRatio, 1f, 50f);
            props.EnsureDefaults();
        }

        /// <summary>
        /// 绘制紧凑浮点输入框，负责在横向字段较多时避免中文标签挤压输入区域。
        /// </summary>
        private void DrawCompactFloatField(Rect rect, string label, ref float value, float min, float max)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            float labelHeight = Text.LineHeightOf(GameFont.Tiny) + 2f;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, labelHeight), label.Truncate(rect.width));
            GUI.color = Color.white;
            string buffer = Widgets.TextField(new Rect(rect.x, rect.y + labelHeight + 2f, rect.width, 24f), value.ToString("0.##"));
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
        /// 确保自定义顾客类型存在价格敏感度配置，负责兼容旧 JSON。
        /// </summary>
        private static CustomerPriceSensitivityProps EnsurePriceSensitivity(CustomCustomerKindRecord record)
        {
            if (record.priceSensitivity == null)
                record.priceSensitivity = CustomerPriceSensitivityProps.Default();
            record.priceSensitivity.EnsureDefaults();
            return record.priceSensitivity;
        }

        /// <summary>
        /// 确保自定义顾客档案存在价格敏感度配置，负责兼容旧 JSON。
        /// </summary>
        private static CustomerPriceSensitivityProps EnsurePriceSensitivity(CustomCustomerProfileRecord record)
        {
            if (record.priceSensitivity == null)
                record.priceSensitivity = CustomerPriceSensitivityProps.Default();
            record.priceSensitivity.EnsureDefaults();
            return record.priceSensitivity;
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
                if (removeAction != null && SimUiStyle.DrawDangerButton(new Rect(row.xMax - 74f, row.y + 8f, 66f, 26f), SimTranslation.T("RSMF.CustomCustomer.Remove"), true, GameFont.Tiny))
                    removeAction();
            }
            else if (SimUiStyle.DrawPrimaryButton(new Rect(row.xMax - 74f, row.y + 8f, 66f, 26f), SimTranslation.T("RSMF.CustomCustomer.Append"), true, GameFont.Tiny))
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
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.NoLimitHint", title.Named("title")));
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
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.NoLimitHint", title.Named("title")));
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
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.PreferenceWeightTip"));
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
                DrawFloatField(new Rect(row.xMax - 198f, row.y + 7f, 126f, 28f), SimTranslation.T("RSMF.CustomCustomer.Weight"), ref pref.weight, 1f, 20f);
                if (SimUiStyle.DrawDangerButton(new Rect(row.xMax - 66f, row.y + 7f, 58f, 28f), SimTranslation.T("RSMF.CustomCustomer.Delete"), true, GameFont.Tiny))
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
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.PreferenceWeightTip"));
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
                    DrawFloatField(new Rect(viewRect.xMax - 116f, y - 2f, 108f, 28f), SimTranslation.T("RSMF.CustomCustomer.Weight"), ref existing.weight, 1f, 20f);
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
                if (removeAction != null && SimUiStyle.DrawDangerButton(new Rect(row.xMax - 74f, row.y + 3f, 66f, 24f), SimTranslation.T("RSMF.CustomCustomer.Remove"), true, GameFont.Tiny))
                    removeAction();
                else if (removeAction == null)
                    DrawBadge(new Rect(row.xMax - 74f, row.y + 6f, 66f, 18f), SimTranslation.T("RSMF.CustomCustomer.Exists"), BuiltInBadge);
            }
            else if (SimUiStyle.DrawPrimaryButton(new Rect(row.xMax - 74f, row.y + 3f, 66f, 24f), SimTranslation.T("RSMF.CustomCustomer.Append"), true, GameFont.Tiny))
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
            Widgets.Label(new Rect(rect.x, rect.y + 6f, 96f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomCustomer.TotalCount", totalCount.Named("count")));
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 188f, rect.y, 72f, 26f), SimTranslation.T("RSMF.CustomCustomer.PrevPage"), pageIndex > 0, GameFont.Tiny))
                pageIndex--;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.xMax - 110f, rect.y + 3f, 48f, 20f), $"{pageIndex + 1}/{pageCount}");
            Text.Anchor = TextAnchor.UpperLeft;
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 58f, rect.y, 58f, 26f), SimTranslation.T("RSMF.CustomCustomer.NextPage"), pageIndex < pageCount - 1, GameFont.Tiny))
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
                Messages.Message(SimTranslation.T("RSMF.CustomCustomer.RefreshedTypes", allGoodsCategories.Count.Named("count")), MessageTypeDefOf.PositiveEvent, false);
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
                Messages.Message(SimTranslation.T("RSMF.CustomCustomer.NoPawnKind"), MessageTypeDefOf.RejectInput, false);
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
                Messages.Message(SimTranslation.T("RSMF.CustomCustomer.EmptyName"), MessageTypeDefOf.RejectInput, false);
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
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(SimTranslation.T("RSMF.CustomCustomer.DeleteConfirm"), DeleteSelectedKind));
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
                activeHourMax = record.activeHourMax,
                priceSensitivity = EnsurePriceSensitivity(record).Clone()
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
            Messages.Message(SimTranslation.T("RSMF.CustomCustomer.SaveSuccess"), MessageTypeDefOf.PositiveEvent, false);
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

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(SimTranslation.T("RSMF.CustomCustomer.ReloadConfirm"), LoadDraft));
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
                if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 130f, inRect.yMax - 38f, 130f, 32f), SimTranslation.T("RSMF.CustomCustomer.CopyClipboard")))
                {
                    GUIUtility.systemCopyBuffer = text;
                    Messages.Message(SimTranslation.T("RSMF.CustomCustomer.Base64Copied"), MessageTypeDefOf.PositiveEvent, false);
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
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - CloseXReservedWidth, Text.LineHeightOf(GameFont.Medium) + 4f), SimTranslation.T("RSMF.CustomCustomer.ImportTitle"));
                Text.Font = GameFont.Tiny;
                GUI.color = MutedText;
                Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, Text.LineHeightOf(GameFont.Tiny) * 2f + 4f), SimTranslation.T("RSMF.CustomCustomer.ImportInfo"));
                Rect textRect = new Rect(inRect.x, inRect.y + 74f, inRect.width, Mathf.Max(90f, inRect.height - 122f));
                Widgets.DrawBoxSolid(textRect, new Color(0f, 0f, 0f, 0.22f));
                SimUiStyle.DrawBorder(textRect, new Color(1f, 1f, 1f, 0.10f));
                Rect viewRect = new Rect(0f, 0f, textRect.width - ScrollbarWidth, Mathf.Max(textRect.height, Text.CalcHeight(text, textRect.width - 24f) + 12f));
                Widgets.BeginScrollView(textRect.ContractedBy(4f), ref scroll, viewRect);
                text = Widgets.TextArea(viewRect, text);
                Widgets.EndScrollView();
                if (SimUiStyle.DrawSecondaryButton(new Rect(inRect.x, inRect.yMax - 38f, 130f, 32f), SimTranslation.T("RSMF.CustomCustomer.PasteClipboard")))
                    text = GUIUtility.systemCopyBuffer ?? string.Empty;
                if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 130f, inRect.yMax - 38f, 130f, 32f), SimTranslation.T("RSMF.CustomCustomer.ConfirmImport")))
                {
                    if (!CustomCustomerDatabase.TryImportBase64(text, out CustomCustomerDatabaseData data, out string error))
                    {
                        Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                    importAction?.Invoke(data);
                    Close();
                    Messages.Message(SimTranslation.T("RSMF.CustomCustomer.ImportSuccess"), MessageTypeDefOf.PositiveEvent, false);
                }
                GUI.color = Color.white;
            }
        }
    }
}
