using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 绘制收藏品兑换二级商店窗口，职责是从 CollectibleExchangeListDef 读取立绘、文本和商品列表并发起购买。
    /// </summary>
    public class Dialog_CollectibleExchangeShop : Window
    {
        private const float LeftPanelWidth = 430f;
        private const float PanelGap = 14f;
        private const float ItemIconSize = 58f;
        private const float CurrencyIconSize = 26f;
        private const float BuyButtonWidth = 96f;
        private const float ScrollbarWidth = 18f;
        private const float PortraitDebugHeight = 164f;
        private const float DialogueBubbleHeight = 116f;
        private const float TypewriterCharsPerSecond = 18f;
        private const float DialogueMinDisplaySeconds = 2.8f;
        private const float BrowseDialogueCooldownSeconds = 3.5f;

        private static readonly Color WindowBg = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f, 0.95f);
        private static readonly Color PanelAlt = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color Accent = new Color(0.25f, 0.65f, 0.85f, 1f);
        private static readonly Color AccentSoft = new Color(0.25f, 0.65f, 0.85f, 0.16f);
        private static readonly Color MutedText = new Color(0.72f, 0.76f, 0.82f, 1f);
        private static readonly Color WarnText = new Color(0.95f, 0.72f, 0.25f, 1f);

        private readonly CollectibleExchangeListDef exchangeDef;
        private Vector2 itemScroll;
        private Texture2D portraitTex;
        private bool portraitResolved;
        private float portraitScaleDraft = 1f;
        private float portraitOffsetXDraft;
        private float portraitOffsetYDraft;
        private bool portraitDraftInitialized;
        private string dialogueText = "";
        private float dialogueStartTime;
        private float lastIdleDialogueTime;
        private float lastBrowseDialogueTime;
        private DialoguePriority dialoguePriority;
        private string lastHoveredItemId = "";

        public override Vector2 InitialSize => new Vector2(1180f, 780f);

        /// <summary>
        /// 初始化收藏品兑换二级商店窗口，负责配置窗口交互行为并保存当前 Def 引用。
        /// </summary>
        public Dialog_CollectibleExchangeShop(CollectibleExchangeListDef exchangeDef)
        {
            this.exchangeDef = exchangeDef;
            doCloseX = true;
            absorbInputAroundWindow = false;
            forcePause = false;
            draggable = true;
            resizeable = true;
            TrySetRandomDialogue(exchangeDef?.welcomeTexts, SimTranslation.T("RSMF.CollectibleExchange.Dialogue.DefaultWelcome"), DialoguePriority.Welcome, true);
        }

        /// <summary>
        /// 定义二级商店对话优先级，职责是避免浏览和停留文本频繁打断重要反馈。
        /// </summary>
        private enum DialoguePriority
        {
            Idle = 0,
            Browse = 1,
            Welcome = 2,
            Failure = 3,
            Purchase = 4
        }

        /// <summary>
        /// 绘制二级商店完整窗口，负责分配标题区、立绘区和商品列表区。
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

                float headerHeight = Mathf.Max(58f, Text.LineHeightOf(GameFont.Medium) + Text.LineHeightOf(GameFont.Tiny) + 20f);
                Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, headerHeight);
                Rect bodyRect = new Rect(inRect.x, headerRect.yMax + 10f, inRect.width, Mathf.Max(120f, inRect.height - headerHeight - 10f));
                Rect leftRect = new Rect(bodyRect.x, bodyRect.y, LeftPanelWidth, bodyRect.height);
                Rect rightRect = new Rect(leftRect.xMax + PanelGap, bodyRect.y, Mathf.Max(280f, bodyRect.width - LeftPanelWidth - PanelGap), bodyRect.height);

                DrawHeader(headerRect);
                DrawPortraitPanel(leftRect);
                DrawItemPanel(rightRect);
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
        /// 绘制顶部标题区，负责显示商店名、期数标题和 Def 介绍。
        /// </summary>
        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            string title = ShopName + " · " + ShopTitle;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 6f, rect.width - 52f, Mathf.Max(30f, Text.LineHeightOf(GameFont.Medium) + 4f)), title);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = MutedText;
            float tipY = rect.y + 38f;
            Widgets.Label(new Rect(rect.x + 16f, tipY, rect.width - 32f, Mathf.Max(20f, rect.yMax - tipY - 6f)), IntroText);
            ResetText();
        }

        /// <summary>
        /// 绘制左侧立绘和进度信息，负责从 Def 的 portraitTexPath、periodLabel、progressLabel 读取展示内容。
        /// </summary>
        private void DrawPortraitPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            UpdateIdleDialogue();
            float reservedHeight = DialogueBubbleHeight + 14f + (ShouldDrawPortraitDebugTools ? PortraitDebugHeight + 150f : 126f);
            float portraitHeight = Mathf.Max(220f, Mathf.Min(560f, rect.height - reservedHeight));
            Rect portraitFrame = new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, portraitHeight);
            Widgets.DrawBoxSolid(portraitFrame, new Color(0f, 0f, 0f, 0.26f));
            SimUiStyle.DrawBorder(portraitFrame, AccentSoft);

            Texture2D portrait = ResolvePortrait();
            if (portrait != null)
            {
                DrawPortraitTexture(portraitFrame.ContractedBy(4f), portrait);
            }
            else
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = MutedText;
                Widgets.Label(portraitFrame, SimTranslation.T("RSMF.CollectibleExchange.PortraitMissing"));
            }

            float y = portraitFrame.yMax + 14f;
            DrawDialogueBubble(new Rect(rect.x + 12f, y, rect.width - 24f, DialogueBubbleHeight));
            y += DialogueBubbleHeight + 14f;

            if (ShouldDrawPortraitDebugTools)
            {
                DrawPortraitDebugTools(new Rect(rect.x + 12f, y, rect.width - 24f, PortraitDebugHeight));
                y += PortraitDebugHeight + 10f;
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 18f, y, rect.width - 36f, Mathf.Max(28f, Text.LineHeightOf(GameFont.Small) + 6f)), PeriodText);
            y += 34f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Accent;
            string progress = string.IsNullOrWhiteSpace(exchangeDef?.progressLabel)
                ? SimTranslation.T("RSMF.CollectibleExchange.ProgressFallback")
                : exchangeDef.progressLabel;
            Widgets.Label(new Rect(rect.x + 18f, y, rect.width - 36f, Mathf.Max(40f, rect.yMax - y - 16f)), progress);
            ResetText();
        }

        /// <summary>
        /// 绘制可调立绘，负责根据 Def 参数和调试草稿参数控制缩放与偏移。
        /// </summary>
        private void DrawPortraitTexture(Rect frame, Texture2D portrait)
        {
            EnsurePortraitDraftInitialized();
            Rect drawRect = CalcPortraitDrawRect(frame, portrait, portraitScaleDraft, portraitOffsetXDraft, portraitOffsetYDraft);
            GUI.BeginGroup(frame);
            GUI.DrawTexture(new Rect(drawRect.x - frame.x, drawRect.y - frame.y, drawRect.width, drawRect.height), portrait, ScaleMode.StretchToFill, true);
            GUI.EndGroup();
        }

        /// <summary>
        /// 初始化立绘调试草稿参数，负责从 Def 字段读取当前配置。
        /// </summary>
        private void EnsurePortraitDraftInitialized()
        {
            if (portraitDraftInitialized)
                return;

            portraitDraftInitialized = true;
            portraitScaleDraft = Mathf.Clamp(exchangeDef?.portraitScale ?? 1f, 0.2f, 4f);
            portraitOffsetXDraft = Mathf.Clamp(exchangeDef?.portraitOffsetX ?? 0f, -1.5f, 1.5f);
            portraitOffsetYDraft = Mathf.Clamp(exchangeDef?.portraitOffsetY ?? 0f, -1.5f, 1.5f);
        }

        /// <summary>
        /// 计算立绘绘制矩形，负责保持原图比例并应用缩放和归一化偏移。
        /// </summary>
        private static Rect CalcPortraitDrawRect(Rect frame, Texture2D portrait, float scale, float offsetX, float offsetY)
        {
            float textureWidth = Mathf.Max(1f, portrait?.width ?? 1f);
            float textureHeight = Mathf.Max(1f, portrait?.height ?? 1f);
            float textureAspect = textureWidth / textureHeight;
            float frameAspect = frame.width / Mathf.Max(1f, frame.height);

            float baseWidth;
            float baseHeight;
            if (frameAspect > textureAspect)
            {
                baseHeight = frame.height;
                baseWidth = baseHeight * textureAspect;
            }
            else
            {
                baseWidth = frame.width;
                baseHeight = baseWidth / textureAspect;
            }

            float finalWidth = baseWidth * Mathf.Max(0.01f, scale);
            float finalHeight = baseHeight * Mathf.Max(0.01f, scale);
            Vector2 center = frame.center + new Vector2(offsetX * frame.width, offsetY * frame.height);
            return new Rect(center.x - finalWidth * 0.5f, center.y - finalHeight * 0.5f, finalWidth, finalHeight);
        }

        /// <summary>
        /// 绘制上帝模式立绘调试工具，负责调整预览参数并导出可写回 Def 的 XML 片段。
        /// </summary>
        private void DrawPortraitDebugTools(Rect rect)
        {
            EnsurePortraitDraftInitialized();
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.20f));
            SimUiStyle.DrawBorder(rect, AccentSoft);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            float titleHeight = Mathf.Max(22f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, titleHeight), SimTranslation.T("RSMF.CollectibleExchange.PortraitDebug.Title"));

            float y = rect.y + titleHeight + 8f;
            DrawPortraitDebugSlider(new Rect(rect.x + 8f, y, rect.width - 16f, 28f), SimTranslation.T("RSMF.CollectibleExchange.PortraitDebug.Scale"), ref portraitScaleDraft, 0.2f, 4f);
            y += 34f;
            DrawPortraitDebugSlider(new Rect(rect.x + 8f, y, rect.width - 16f, 28f), SimTranslation.T("RSMF.CollectibleExchange.PortraitDebug.OffsetX"), ref portraitOffsetXDraft, -1.5f, 1.5f);
            y += 34f;
            DrawPortraitDebugSlider(new Rect(rect.x + 8f, y, rect.width - 16f, 28f), SimTranslation.T("RSMF.CollectibleExchange.PortraitDebug.OffsetY"), ref portraitOffsetYDraft, -1.5f, 1.5f);

            Rect resetRect = new Rect(rect.x + 8f, rect.yMax - 34f, 92f, 26f);
            if (SimUiStyle.DrawSecondaryButton(resetRect, SimTranslation.T("RSMF.CollectibleExchange.PortraitDebug.Reset"), true, GameFont.Tiny))
            {
                portraitScaleDraft = 1f;
                portraitOffsetXDraft = 0f;
                portraitOffsetYDraft = 0f;
            }

            Rect copyRect = new Rect(resetRect.xMax + 8f, rect.yMax - 34f, 126f, 26f);
            if (SimUiStyle.DrawPrimaryButton(copyRect, SimTranslation.T("RSMF.CollectibleExchange.PortraitDebug.CopyXml"), true, GameFont.Tiny))
            {
                GUIUtility.systemCopyBuffer = BuildPortraitXmlSnippet();
                Messages.Message(SimTranslation.T("RSMF.CollectibleExchange.PortraitDebug.Copied"), MessageTypeDefOf.PositiveEvent, false);
            }

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = MutedText;
            Widgets.Label(new Rect(copyRect.xMax + 8f, rect.yMax - 34f, rect.xMax - copyRect.xMax - 16f, 26f),
                SimTranslation.T("RSMF.CollectibleExchange.PortraitDebug.Values",
                    portraitScaleDraft.ToString("0.###").Named("scale"),
                    portraitOffsetXDraft.ToString("0.###").Named("x"),
                    portraitOffsetYDraft.ToString("0.###").Named("y")));
            ResetText();
        }

        /// <summary>
        /// 绘制立绘调试滑条，负责用统一样式调整一个浮点参数。
        /// </summary>
        private static void DrawPortraitDebugSlider(Rect rect, string label, ref float value, float min, float max)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = MutedText;
            Rect labelRect = new Rect(rect.x, rect.y, 74f, Mathf.Max(rect.height, Text.LineHeightOf(GameFont.Tiny) + 4f));
            Widgets.Label(labelRect, label);

            Rect sliderRect = new Rect(labelRect.xMax + 8f, rect.y + 3f, Mathf.Max(80f, rect.width - labelRect.width - 78f), 24f);
            value = Widgets.HorizontalSlider(sliderRect, value, min, max, false);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.xMax - 60f, rect.y, 60f, Mathf.Max(rect.height, Text.LineHeightOf(GameFont.Tiny) + 4f)), value.ToString("0.###"));
        }

        /// <summary>
        /// 构建立绘调试 XML 片段，负责让开发者把当前预览参数写回 Def。
        /// </summary>
        private string BuildPortraitXmlSnippet()
        {
            return "<portraitScale>" + portraitScaleDraft.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "</portraitScale>\n"
                + "<portraitOffsetX>" + portraitOffsetXDraft.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "</portraitOffsetX>\n"
                + "<portraitOffsetY>" + portraitOffsetYDraft.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "</portraitOffsetY>";
        }

        /// <summary>
        /// 绘制右侧商品列表面板，负责为空列表和长列表提供稳定布局。
        /// </summary>
        private void DrawItemPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            List<CollectibleExchangeItemEntry> items = exchangeDef?.items?.Where(item => item != null).ToList() ?? new List<CollectibleExchangeItemEntry>();
            if (items.Count == 0)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = MutedText;
                Widgets.Label(rect, SimTranslation.T("RSMF.CollectibleExchange.EmptyItems"));
                ResetText();
                return;
            }

            Rect listRect = rect.ContractedBy(12f);
            float viewWidth = Mathf.Max(240f, listRect.width - ScrollbarWidth);
            List<float> heights = items.Select(item => CalcItemCardHeight(item, viewWidth)).ToList();
            float viewHeight = Mathf.Max(listRect.height + 1f, heights.Sum() + 8f);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

            Widgets.BeginScrollView(listRect, ref itemScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                float cardHeight = heights[i] - 8f;
                DrawItemCard(new Rect(0f, y, viewWidth, cardHeight), items[i], i);
                y += heights[i];
            }
            Widgets.EndScrollView();
            ResetText();
        }

        /// <summary>
        /// 计算商品卡片高度，负责根据商品说明、价格行和限购文本适配中文长文本。
        /// </summary>
        private static float CalcItemCardHeight(CollectibleExchangeItemEntry item, float width)
        {
            float textWidth = Mathf.Max(120f, width - ItemIconSize - BuyButtonWidth - 58f);
            Text.Font = GameFont.Small;
            float titleHeight = Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f);
            Text.Font = GameFont.Tiny;
            float descHeight = Text.CalcHeight(item?.DisplayDescription ?? "", textWidth);
            float priceHeight = Mathf.Max(30f, CurrencyIconSize + 4f);
            float remainingHeight = Mathf.Max(22f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            float textHeight = 10f + titleHeight + 4f + descHeight + 8f + priceHeight + 6f + remainingHeight + 12f;
            float iconHeight = 14f + ItemIconSize + 14f;
            float buttonHeight = 12f + Mathf.Max(32f, Text.LineHeightOf(GameFont.Small) + 12f) + 12f;
            return Mathf.Max(136f, Mathf.Max(textHeight, Mathf.Max(iconHeight, buttonHeight)));
        }

        /// <summary>
        /// 绘制单个商品卡片，负责显示图标、名称、说明、价格、剩余数量和购买按钮。
        /// </summary>
        private void DrawItemCard(Rect rect, CollectibleExchangeItemEntry item, int index)
        {
            Widgets.DrawBoxSolid(rect, index % 2 == 0 ? PanelAlt : new Color(0f, 0f, 0f, 0.10f));
            bool mouseOver = Mouse.IsOver(rect);
            if (mouseOver)
            {
                Widgets.DrawBoxSolid(rect, AccentSoft);
                NotifyBrowsingItem(item);
            }
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Rect iconRect = new Rect(rect.x + 12f, rect.y + 14f, ItemIconSize, ItemIconSize);
            DrawItemIcon(iconRect, item);

            float buttonHeight = Mathf.Max(32f, Text.LineHeightOf(GameFont.Small) + 12f);
            Rect buyRect = new Rect(rect.xMax - BuyButtonWidth - 12f, rect.yMax - buttonHeight - 12f, BuyButtonWidth, buttonHeight);
            Rect textRect = new Rect(iconRect.xMax + 12f, rect.y + 10f, Mathf.Max(120f, buyRect.x - iconRect.xMax - 24f), rect.height - 20f);

            DrawItemText(textRect, item);
            DrawPurchaseArea(buyRect, item);
        }

        /// <summary>
        /// 绘制商品图标，负责优先使用自定义贴图并在缺省时回退到 ThingDef 默认图标。
        /// </summary>
        private static void DrawItemIcon(Rect rect, CollectibleExchangeItemEntry item)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.22f));
            Texture2D customIcon = item?.ResolveIconTexture();
            if (customIcon != null)
                GUI.DrawTexture(rect.ContractedBy(5f), customIcon, ScaleMode.ScaleToFit, true);
            else if (item?.thingDef != null)
                Widgets.ThingIcon(rect.ContractedBy(5f), item.thingDef);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
        }

        /// <summary>
        /// 绘制商品名称、说明、价格和剩余数量文本，负责让不同货币配置在 UI 中独立显示。
        /// </summary>
        private void DrawItemText(Rect rect, CollectibleExchangeItemEntry item)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            float titleHeight = Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, titleHeight), item?.DisplayLabel ?? SimTranslation.T("RSMF.CollectibleExchange.InvalidItem"));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            float descY = rect.y + titleHeight + 4f;
            string description = item?.DisplayDescription ?? "";
            float descHeight = Text.CalcHeight(description, rect.width);
            Widgets.Label(new Rect(rect.x, descY, rect.width, descHeight), description);

            float priceY = descY + descHeight + 8f;
            DrawPriceLine(new Rect(rect.x, priceY, rect.width, Mathf.Max(30f, CurrencyIconSize + 4f)), item);

            float remainingY = priceY + Mathf.Max(30f, CurrencyIconSize + 4f) + 6f;
            GUI.color = RemainingCount(item) > 0 ? Accent : WarnText;
            string countText = SimTranslation.T("RSMF.CollectibleExchange.SinglePurchaseCount", (item?.PurchaseCount ?? 1).Named("count"));
            Widgets.Label(new Rect(rect.x, remainingY, rect.width, Mathf.Max(22f, Text.LineHeightOf(GameFont.Tiny) + 4f)), RemainingText(item) + "  ·  " + countText);
        }

        /// <summary>
        /// 绘制价格行，负责同时展示货币图标和货币名称。
        /// </summary>
        private static void DrawPriceLine(Rect rect, CollectibleExchangeItemEntry item)
        {
            ThingDef currency = item?.currencyDef;
            if (currency != null)
                Widgets.ThingIcon(new Rect(rect.x, rect.y, CurrencyIconSize, CurrencyIconSize), currency);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            string currencyLabel = currency?.LabelCap.RawText ?? SimTranslation.T("RSMF.CollectibleExchange.InvalidCurrency");
            string priceText = SimTranslation.T("RSMF.CollectibleExchange.PriceLine", (item?.price ?? 0).Named("price"), currencyLabel.Named("currency"));
            Widgets.Label(new Rect(rect.x + CurrencyIconSize + 8f, rect.y, rect.width - CurrencyIconSize - 8f, Mathf.Max(CurrencyIconSize, Text.LineHeightOf(GameFont.Tiny) + 4f)), priceText);
        }

        /// <summary>
        /// 绘制购买按钮区域，负责把点击购买结果反馈为游戏消息。
        /// </summary>
        private void DrawPurchaseArea(Rect buyRect, CollectibleExchangeItemEntry item)
        {
            bool configValid = item != null && item.thingDef != null && item.currencyDef != null && item.price > 0;
            int remaining = RemainingCount(item);
            int availableCurrency = configValid ? CollectibleExchangePurchaseUtility.CountAvailableCurrencyForCurrentPlayerMap(item.currencyDef) : 0;
            bool hasEnoughCurrency = configValid && availableCurrency >= item.price;
            bool canBuy = configValid && remaining > 0 && hasEnoughCurrency;
            string label = remaining <= 0 ? SimTranslation.T("RSMF.CollectibleExchange.SoldOut") : SimTranslation.T("RSMF.CollectibleExchange.Buy");
            if (configValid && remaining > 0 && !hasEnoughCurrency)
            {
                TooltipHandler.TipRegion(buyRect, SimTranslation.T(
                    "RSMF.CollectibleExchange.NotEnoughCurrency",
                    item.currencyDef.LabelCap.Named("currency"),
                    item.price.Named("need"),
                    availableCurrency.Named("have")));
            }

            bool clicked = canBuy
                ? SimUiStyle.DrawPrimaryButton(buyRect, label, true, GameFont.Small)
                : SimUiStyle.DrawDisabledClickableButton(buyRect, label, GameFont.Small);

            if (clicked)
            {
                if (!CollectibleExchangePurchaseUtility.TryPurchase(exchangeDef, item, out string failReason, out CollectibleExchangePurchaseFailKind failKind))
                {
                    Messages.Message(failReason, MessageTypeDefOf.RejectInput, false);
                    if (failKind == CollectibleExchangePurchaseFailKind.NotEnoughCurrency)
                    {
                        TrySetRandomDialogue(exchangeDef?.notEnoughCurrencyTexts, SimTranslation.T("RSMF.CollectibleExchange.Dialogue.DefaultNotEnoughCurrency"), DialoguePriority.Failure, true);
                    }
                }
                else
                {
                    TrySetRandomDialogue(exchangeDef?.purchaseTexts, SimTranslation.T("RSMF.CollectibleExchange.Dialogue.DefaultPurchase"), DialoguePriority.Purchase, true);
                }
            }
        }

        /// <summary>
        /// 绘制立绘下方对话气泡，负责按打字机进度流式显示当前商店文本。
        /// </summary>
        private void DrawDialogueBubble(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.30f));
            SimUiStyle.DrawBorder(rect, AccentSoft);

            string visibleText = GetVisibleDialogueText();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
            Widgets.Label(rect.ContractedBy(12f), visibleText);
            ResetText();
        }

        /// <summary>
        /// 读取当前应显示的打字机文本，负责根据真实时间逐步增加可见字符数。
        /// </summary>
        private string GetVisibleDialogueText()
        {
            if (dialogueText.NullOrEmpty())
                return "";

            int count = Mathf.Clamp(Mathf.FloorToInt((Time.realtimeSinceStartup - dialogueStartTime) * TypewriterCharsPerSecond), 0, dialogueText.Length);
            return dialogueText.Substring(0, count);
        }

        /// <summary>
        /// 记录商品浏览事件，负责在玩家鼠标停留到新商品时触发浏览文本。
        /// </summary>
        private void NotifyBrowsingItem(CollectibleExchangeItemEntry item)
        {
            string itemId = item?.StableId ?? "";
            if (itemId.NullOrEmpty() || itemId == lastHoveredItemId)
                return;

            if (Time.realtimeSinceStartup - lastBrowseDialogueTime < BrowseDialogueCooldownSeconds)
                return;

            lastHoveredItemId = itemId;
            if (TrySetRandomDialogue(exchangeDef?.browseTexts, SimTranslation.T("RSMF.CollectibleExchange.Dialogue.DefaultBrowse", (item?.DisplayLabel ?? "").Named("item")), DialoguePriority.Browse))
                lastBrowseDialogueTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// 更新停留闲聊文本，负责玩家长时间停留在商店时定时触发随机文本。
        /// </summary>
        private void UpdateIdleDialogue()
        {
            float interval = Mathf.Max(4f, exchangeDef?.idleTextIntervalSeconds ?? 12f);
            if (Time.realtimeSinceStartup - lastIdleDialogueTime < interval)
                return;

            lastIdleDialogueTime = Time.realtimeSinceStartup;
            TrySetRandomDialogue(exchangeDef?.idleTexts, SimTranslation.T("RSMF.CollectibleExchange.Dialogue.DefaultIdle"), DialoguePriority.Idle);
        }

        /// <summary>
        /// 从文本池尝试切换当前对话，负责按优先级和最短展示时间避免高频打断。
        /// </summary>
        private bool TrySetRandomDialogue(List<string> texts, string fallback, DialoguePriority priority, bool force = false)
        {
            if (!force && !CanReplaceDialogue(priority))
                return false;

            List<string> candidates = texts?.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
            string next = candidates.NullOrEmpty() ? fallback : candidates[Rand.Range(0, candidates.Count)];
            if (string.IsNullOrWhiteSpace(next))
                return false;

            dialogueText = next;
            dialogueStartTime = Time.realtimeSinceStartup;
            lastIdleDialogueTime = Time.realtimeSinceStartup;
            dialoguePriority = priority;
            return true;
        }

        /// <summary>
        /// 判断当前对话是否允许被新文本替换，负责让重要文本完整显示并限制低优先级覆盖。
        /// </summary>
        private bool CanReplaceDialogue(DialoguePriority priority)
        {
            if (dialogueText.NullOrEmpty())
                return true;

            float age = Time.realtimeSinceStartup - dialogueStartTime;
            if (age < DialogueMinDisplaySeconds && priority <= dialoguePriority)
                return false;

            if (priority < dialoguePriority && !IsDialogueFullyVisible())
                return false;

            return priority >= dialoguePriority || IsDialogueFullyVisible();
        }

        /// <summary>
        /// 判断当前打字机文本是否已经完整显示，负责让低优先级文本等待当前句子播完。
        /// </summary>
        private bool IsDialogueFullyVisible()
        {
            return dialogueText.NullOrEmpty()
                || (Time.realtimeSinceStartup - dialogueStartTime) * TypewriterCharsPerSecond >= dialogueText.Length;
        }

        /// <summary>
        /// 读取商品剩余购买次数，负责处理游戏组件缺失时的安全兜底。
        /// </summary>
        private int RemainingCount(CollectibleExchangeItemEntry item)
        {
            GameComponent_CollectibleExchangeManager manager = Current.Game?.GetComponent<GameComponent_CollectibleExchangeManager>();
            return manager?.GetRemainingCount(exchangeDef, item) ?? 0;
        }

        /// <summary>
        /// 读取剩余数量显示文本，负责把售罄状态和剩余次数统一格式化。
        /// </summary>
        private string RemainingText(CollectibleExchangeItemEntry item)
        {
            int remaining = RemainingCount(item);
            if (remaining <= 0)
                return SimTranslation.T("RSMF.CollectibleExchange.SoldOut");

            return SimTranslation.T("RSMF.CollectibleExchange.Remaining", remaining.Named("count"));
        }

        /// <summary>
        /// 读取并缓存商店立绘贴图，负责避免每帧重复 ContentFinder 查询。
        /// </summary>
        private Texture2D ResolvePortrait()
        {
            if (portraitResolved)
                return portraitTex;

            portraitResolved = true;
            portraitTex = string.IsNullOrWhiteSpace(exchangeDef?.portraitTexPath)
                ? null
                : ContentFinder<Texture2D>.Get(exchangeDef.portraitTexPath, false);
            return portraitTex;
        }

        /// <summary>
        /// 判断是否绘制立绘调试工具，负责限制调试控件只在开发者模式和上帝模式启用时出现。
        /// </summary>
        private static bool ShouldDrawPortraitDebugTools => Prefs.DevMode && DebugSettings.godMode;

        /// <summary>
        /// 恢复 RimWorld 文本绘制状态，负责避免自定义窗口污染后续 IMGUI。
        /// </summary>
        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }

        /// <summary>
        /// 读取商店名，负责从 Def 字段回退到翻译兜底文本。
        /// </summary>
        private string ShopName => string.IsNullOrWhiteSpace(exchangeDef?.shopName)
            ? SimTranslation.T("RSMF.Business.CollectibleExchange.DefaultShopName")
            : exchangeDef.shopName;

        /// <summary>
        /// 读取商店标题，负责从 Def 字段回退到 label 或 defName。
        /// </summary>
        private string ShopTitle
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(exchangeDef?.title))
                    return exchangeDef.title;
                if (exchangeDef != null && !exchangeDef.LabelCap.RawText.NullOrEmpty())
                    return exchangeDef.LabelCap.RawText;
                return exchangeDef?.defName ?? "";
            }
        }

        /// <summary>
        /// 读取商店介绍，负责从 Def 字段回退到 description 或翻译兜底文本。
        /// </summary>
        private string IntroText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(exchangeDef?.intro))
                    return exchangeDef.intro;
                if (!string.IsNullOrWhiteSpace(exchangeDef?.description))
                    return exchangeDef.description;
                return SimTranslation.T("RSMF.Business.CollectibleExchange.NoIntro");
            }
        }

        /// <summary>
        /// 读取期数文本，负责从 Def 字段回退到通用当前期文本。
        /// </summary>
        private string PeriodText => string.IsNullOrWhiteSpace(exchangeDef?.periodLabel)
            ? SimTranslation.T("RSMF.CollectibleExchange.PeriodFallback")
            : exchangeDef.periodLabel;
    }
}
