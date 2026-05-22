using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager
    {
        /// <summary>
        /// 绘制网络蓝图子页，负责状态栏、分页列表和详情面板布局。
        /// </summary>
        private void DrawBlueprintNetworkPage(Rect rect)
        {
            float topHeight = CalculateBlueprintNetworkTopBarHeight(rect.width);
            Rect topRect = new Rect(rect.x, rect.y, rect.width, topHeight);
            DrawBlueprintNetworkTopBar(topRect);

            Rect bodyRect = new Rect(rect.x, topRect.yMax + 8f, rect.width, rect.height - topRect.height - 8f);
            bool hasListItems = blueprintNetworkPagedList != null && !blueprintNetworkPagedList.items.NullOrEmpty();
            bool detailOnlyState = !hasListItems && blueprintNetworkDetail == null && blueprintNetworkDetailTask == null;
            if (detailOnlyState)
            {
                DrawBlueprintNetworkListPanel(bodyRect);
                return;
            }

            float detailWidth = Mathf.Min(396f, Mathf.Max(340f, bodyRect.width * 0.36f));
            Rect listRect = new Rect(bodyRect.x, bodyRect.y, bodyRect.width - detailWidth - 10f, bodyRect.height);
            Rect detailRect = new Rect(listRect.xMax + 10f, bodyRect.y, detailWidth, bodyRect.height);

            DrawBlueprintNetworkListPanel(listRect);
            DrawBlueprintNetworkDetailPanel(detailRect);
        }

        /// <summary>
        /// 绘制网络蓝图顶部状态和筛选栏。
        /// </summary>
        private void DrawBlueprintNetworkTopBar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.22f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));

            Rect innerRect = rect.ContractedBy(10f);
            float titleHeight = Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f);
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, titleHeight);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(titleRect, SimTranslation.T("RSMF.Blueprint.Network.Title"));

            float y = titleRect.yMax + 6f;
            float statusHeight = CalculateBlueprintNetworkStatusLineHeight(innerRect.width);
            Rect statusRect = new Rect(innerRect.x, y, innerRect.width, statusHeight);
            DrawBlueprintNetworkStatusLine(statusRect);

            y = statusRect.yMax + 8f;
            float codeBarHeight = CalculateBlueprintNetworkCodeBarHeight(innerRect.width);
            Rect codeRect = new Rect(innerRect.x, y, innerRect.width, codeBarHeight);
            DrawBlueprintNetworkCodeBar(codeRect);

            y = codeRect.yMax + 8f;
            float tabsHeight = CalculateBlueprintNetworkSortTabsHeight(innerRect.width);
            Rect tabsRect = new Rect(innerRect.x, y, innerRect.width, tabsHeight);
            DrawBlueprintNetworkSortTabs(tabsRect);

            y = tabsRect.yMax + 8f;
            float pagerHeight = Mathf.Max(28f, Text.LineHeightOf(GameFont.Tiny) + 8f);
            Rect pagerRect = new Rect(innerRect.x, y, innerRect.width, pagerHeight);
            DrawBlueprintNetworkPager(pagerRect);
            ResetText();
        }

        /// <summary>
        /// 绘制蓝图码直达区域，负责让玩家输入蓝图码后直接查看详情或下载。
        /// </summary>
        private void DrawBlueprintNetworkCodeBar(Rect rect)
        {
            float lineHeight = Mathf.Max(30f, Text.LineHeightOf(GameFont.Tiny) + 10f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            float labelWidth = Mathf.Max(72f, Text.CalcSize(SimTranslation.T("RSMF.Blueprint.Network.CodeInputLabel")).x + 8f);
            float buttonWidth = Mathf.Max(94f, Text.CalcSize(SimTranslation.T("RSMF.Blueprint.Network.DirectDownload")).x + 20f);
            float gap = 8f;
            bool wrapButtons = labelWidth + 160f + buttonWidth * 2f + gap * 3f > rect.width;

            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, lineHeight);
            Widgets.Label(labelRect, SimTranslation.T("RSMF.Blueprint.Network.CodeInputLabel"));

            float inputWidth = wrapButtons
                ? Mathf.Max(120f, rect.width - labelRect.width - gap)
                : Mathf.Max(120f, rect.width - labelRect.width - buttonWidth * 2f - gap * 3f);
            Rect inputRect = new Rect(labelRect.xMax + gap, rect.y, inputWidth, lineHeight);
            blueprintNetworkCodeBuffer = Widgets.TextField(inputRect, blueprintNetworkCodeBuffer ?? "");

            float buttonY = wrapButtons ? inputRect.yMax + 6f : rect.y;
            float buttonStartX = wrapButtons ? rect.xMax - buttonWidth * 2f - gap : inputRect.xMax + gap;
            Rect detailRect = new Rect(buttonStartX, buttonY, buttonWidth, lineHeight);
            bool canOpen = !string.IsNullOrWhiteSpace(blueprintNetworkCodeBuffer) && blueprintNetworkDetailTask == null;
            if (SimUiStyle.DrawSecondaryButton(detailRect, SimTranslation.T("RSMF.Blueprint.Network.OpenByCode"), canOpen, GameFont.Tiny))
                OpenBlueprintNetworkDetailByCodeInput();

            Rect downloadRect = new Rect(detailRect.xMax + gap, buttonY, buttonWidth, lineHeight);
            if (SimUiStyle.DrawPrimaryButton(downloadRect, SimTranslation.T("RSMF.Blueprint.Network.DirectDownload"), canOpen, GameFont.Tiny))
                DownloadBlueprintByCodeInput();

            ResetText();
        }

        /// <summary>
        /// 绘制网络蓝图状态行，负责展示 Steam 和服务预检结果。
        /// </summary>
        private void DrawBlueprintNetworkStatusLine(Rect rect)
        {
            string steamStatus = BuildSteamStatusLine();
            string serviceStatus = BuildServiceStatusLine();
            string message = !string.IsNullOrWhiteSpace(blueprintNetworkError) ? blueprintNetworkError : blueprintNetworkMessage;
            float width = Mathf.Max(220f, rect.width);
            float tinyLine = Mathf.Max(18f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            float steamHeight = Mathf.Max(tinyLine, Text.CalcHeight(steamStatus, width));
            float serviceHeight = Mathf.Max(tinyLine, Text.CalcHeight(serviceStatus, width));
            float messageHeight = string.IsNullOrWhiteSpace(message)
                ? tinyLine
                : Mathf.Max(tinyLine, Text.CalcHeight(message, width));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = CDim;
            float y = rect.y;
            Widgets.Label(new Rect(rect.x, y, rect.width, steamHeight), steamStatus);
            y += steamHeight + 2f;
            Widgets.Label(new Rect(rect.x, y, rect.width, serviceHeight), serviceStatus);
            y += serviceHeight + 4f;

            Color statusColor = !string.IsNullOrWhiteSpace(blueprintNetworkError) ? new Color(1f, 0.45f, 0.45f, 1f) : CWarn;
            GUI.color = string.IsNullOrWhiteSpace(message) ? CDim : statusColor;
            Widgets.Label(new Rect(rect.x, y, rect.width, Mathf.Max(messageHeight, rect.yMax - y)), message ?? "");
            ResetText();
        }

        /// <summary>
        /// 绘制网络蓝图分类标签。
        /// </summary>
        private void DrawBlueprintNetworkSortTabs(Rect rect)
        {
            BlueprintNetworkSortMode[] modes =
            {
                BlueprintNetworkSortMode.Latest,
                BlueprintNetworkSortMode.Hot,
                BlueprintNetworkSortMode.Downloads,
                BlueprintNetworkSortMode.Mine,
                BlueprintNetworkSortMode.Compatible
            };

            float buttonHeight = Mathf.Max(30f, Text.LineHeightOf(GameFont.Tiny) + 12f);
            float x = rect.x;
            float y = rect.y;
            for (int i = 0; i < modes.Length; i++)
            {
                string label = GetBlueprintNetworkSortLabel(modes[i]);
                float width = Mathf.Max(96f, Text.CalcSize(label).x + 24f);
                if (x > rect.x && x + width > rect.xMax)
                {
                    x = rect.x;
                    y += buttonHeight + 6f;
                }

                Rect tabRect = new Rect(x, y, width, buttonHeight);
                if (SimUiStyle.DrawTabButton(tabRect, label, blueprintNetworkSortMode == modes[i], CDim))
                    SwitchBlueprintNetworkSortMode(modes[i]);
                x += width + 8f;
            }
        }

        /// <summary>
        /// 绘制网络蓝图分页栏。
        /// </summary>
        private void DrawBlueprintNetworkPager(Rect rect)
        {
            BlueprintNetworkPagedListData paged = blueprintNetworkPagedList ?? new BlueprintNetworkPagedListData();
            int totalPages = Math.Max(1, paged.totalPages);
            int currentPage = Math.Max(1, paged.page > 0 ? paged.page : blueprintNetworkPage);
            float buttonHeight = Mathf.Max(30f, Text.LineHeightOf(GameFont.Tiny) + 10f);
            float sideWidth = 94f;
            Rect prevRect = new Rect(rect.x, rect.y, sideWidth, buttonHeight);
            Rect refreshRect = new Rect(rect.xMax - sideWidth, rect.y, sideWidth, buttonHeight);

            if (SimUiStyle.DrawSecondaryButton(prevRect, SimTranslation.T("RSMF.Common.PreviousPage"), currentPage > 1 && blueprintNetworkListTask == null, GameFont.Tiny))
                ChangeBlueprintNetworkPage(currentPage - 1);

            Rect nextRect = new Rect(refreshRect.x - sideWidth, rect.y, sideWidth, buttonHeight);
            if (SimUiStyle.DrawSecondaryButton(nextRect, SimTranslation.T("RSMF.Common.NextPage"), currentPage < totalPages && blueprintNetworkListTask == null, GameFont.Tiny))
                ChangeBlueprintNetworkPage(currentPage + 1);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = true;
            GUI.color = Color.white;
            Rect statusRect = new Rect(prevRect.xMax + 10f, rect.y, Mathf.Max(80f, nextRect.x - prevRect.xMax - 20f), buttonHeight);
            Widgets.Label(statusRect,
                SimTranslation.T("RSMF.Blueprint.Network.PageStatus",
                    currentPage.Named("page"),
                    totalPages.Named("pages"),
                    paged.totalCount.Named("count")));

            if (SimUiStyle.DrawSecondaryButton(refreshRect, SimTranslation.T("RSMF.Blueprint.Refresh"), blueprintNetworkListTask == null, GameFont.Tiny))
                RefreshBlueprintNetworkList();
            ResetText();
        }

        /// <summary>
        /// 绘制网络蓝图列表面板。
        /// </summary>
        private void DrawBlueprintNetworkListPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.20f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));

            Rect innerRect = rect.ContractedBy(8f);
            if (!string.IsNullOrWhiteSpace(blueprintNetworkError) && (blueprintNetworkPagedList == null || blueprintNetworkPagedList.items.NullOrEmpty()))
            {
                DrawCenteredPanelMessage(innerRect, blueprintNetworkError, new Color(1f, 0.45f, 0.45f, 1f));
                return;
            }

            if (blueprintNetworkListTask != null && (blueprintNetworkPagedList == null || blueprintNetworkPagedList.items.NullOrEmpty()))
            {
                DrawCenteredPanelMessage(innerRect, SimTranslation.T("RSMF.Blueprint.Network.Loading"), CDim);
                return;
            }

            List<BlueprintNetworkListItemData> items = blueprintNetworkPagedList?.items;
            if (items.NullOrEmpty())
            {
                DrawCenteredPanelMessage(innerRect, SimTranslation.T("RSMF.Blueprint.Network.Empty"), CDim);
                return;
            }

            float rowHeight = CalculateBlueprintNetworkListRowHeight();
            float viewWidth = innerRect.width - 18f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, items.Count * rowHeight);
            Widgets.BeginScrollView(innerRect, ref blueprintNetworkScrollPos, viewRect);

            for (int i = 0; i < items.Count; i++)
                DrawBlueprintNetworkListRow(new Rect(0f, i * rowHeight, viewWidth, rowHeight - 6f), items[i], i);

            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制单条网络蓝图列表记录。
        /// </summary>
        private void DrawBlueprintNetworkListRow(Rect row, BlueprintNetworkListItemData item, int index)
        {
            bool selected = blueprintNetworkDetail != null && blueprintNetworkDetail.blueprintCode == item.blueprintCode;
            Color bg = selected ? new Color(CAccent.r, CAccent.g, CAccent.b, 0.14f) : (index % 2 == 0 ? CPanelAlt : new Color(0f, 0f, 0f, 0.08f));
            Widgets.DrawBoxSolid(row, bg);
            DrawBorder(row, selected ? new Color(CAccent.r, CAccent.g, CAccent.b, 0.55f) : new Color(1f, 1f, 1f, 0.10f));

            Rect previewRect = new Rect(row.x + 6f, row.y + 6f, 72f, 72f);
            DrawBlueprintRemotePreview(previewRect, item.previewUrl);

            float textX = previewRect.xMax + 10f;
            float actionWidth = 96f;
            float textWidth = Mathf.Max(120f, row.width - previewRect.width - actionWidth - 34f);
            float titleHeight = Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f);
            float metaHeight = Mathf.Max(20f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            float statsHeight = Mathf.Max(metaHeight, Text.CalcHeight(
                SimTranslation.T("RSMF.Blueprint.Network.ListStats",
                    item.likeCount.Named("likes"),
                    item.downloadCount.Named("downloads"),
                    item.requiredModCount.Named("mods"),
                    FormatBlueprintDisplayTime(item.createdAt).Named("time")),
                textWidth));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(textX, row.y + 8f, textWidth, titleHeight), item.name ?? SimTranslation.T("RSMF.Common.UnnamedShop"));

            Text.Font = GameFont.Tiny;
            GUI.color = CDim;
            float metaY = row.y + 8f + titleHeight + 2f;
            Widgets.Label(new Rect(textX, metaY, textWidth, metaHeight), SimTranslation.T("RSMF.Blueprint.Network.ListAuthor",
                BuildAuthorDisplayName(item.steamId).Named("author")));
            Widgets.Label(new Rect(textX, metaY + metaHeight + 2f, textWidth, statsHeight), SimTranslation.T("RSMF.Blueprint.Network.ListStats",
                item.likeCount.Named("likes"),
                item.downloadCount.Named("downloads"),
                item.requiredModCount.Named("mods"),
                FormatBlueprintDisplayTime(item.createdAt).Named("time")));
            ResetText();

            float buttonHeight = Mathf.Max(30f, Text.LineHeightOf(GameFont.Tiny) + 10f);
            Rect detailRect = new Rect(row.xMax - actionWidth, row.y + Mathf.Max(8f, (row.height - buttonHeight) / 2f), 88f, buttonHeight);
            if (SimUiStyle.DrawSecondaryButton(detailRect, SimTranslation.T("RSMF.Blueprint.Network.ViewDetail"), blueprintNetworkDetailTask == null, GameFont.Tiny))
                OpenBlueprintNetworkDetail(item.blueprintCode);

            if (Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width - 106f, row.height), false))
                OpenBlueprintNetworkDetail(item.blueprintCode);
        }

        /// <summary>
        /// 绘制网络蓝图详情面板。
        /// </summary>
        private void DrawBlueprintNetworkDetailPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.20f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));

            Rect inner = rect.ContractedBy(10f);
            if (blueprintNetworkDetailTask != null && blueprintNetworkDetail == null)
            {
                DrawCenteredPanelMessage(inner, SimTranslation.T("RSMF.Blueprint.Network.LoadingDetail"), CDim);
                return;
            }

            if (blueprintNetworkDetail == null)
            {
                DrawCenteredPanelMessage(inner, SimTranslation.T("RSMF.Blueprint.Network.DetailHint"), CDim);
                return;
            }

            BlueprintNetworkDetailData detail = blueprintNetworkDetail;
            BlueprintCompatibilityCheckResult compatibility = BlueprintModCompatibilityChecker.CheckCompatibility(detail.requiredMods);
            float actionsHeight = CalculateBlueprintNetworkActionAreaHeight();
            Rect actionsRect = new Rect(inner.x, inner.yMax - actionsHeight, inner.width, actionsHeight);
            Rect contentOutRect = new Rect(inner.x, inner.y, inner.width, Mathf.Max(40f, actionsRect.y - inner.y - 8f));
            float contentHeight = CalculateBlueprintNetworkDetailContentHeight(inner.width, detail, compatibility);
            Rect contentViewRect = new Rect(0f, 0f, Mathf.Max(0f, contentOutRect.width - 18f), contentHeight);
            Widgets.BeginScrollView(contentOutRect, ref blueprintNetworkDetailScrollPos, contentViewRect);
            DrawBlueprintNetworkDetailContent(contentViewRect, detail, compatibility);
            Widgets.EndScrollView();
            DrawDetailActions(actionsRect, detail, compatibility);
        }

        /// <summary>
        /// 绘制详情标题行。
        /// </summary>
        private void DrawDetailTitleLine(Rect rect, BlueprintNetworkDetailData detail)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(rect, detail.name ?? SimTranslation.T("RSMF.Common.UnnamedShop"));
            ResetText();
        }

        /// <summary>
        /// 绘制详情元信息行。
        /// </summary>
        private void DrawDetailMetaLine(Rect rect, BlueprintNetworkDetailData detail)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = CDim;
            Widgets.Label(rect, SimTranslation.T("RSMF.Blueprint.Network.DetailMeta",
                BuildAuthorDisplayName(detail.steamId).Named("author"),
                detail.likeCount.Named("likes"),
                detail.downloadCount.Named("downloads"),
                FormatBlueprintDisplayTime(detail.createdAt).Named("time")));
            ResetText();
        }

        /// <summary>
        /// 绘制详情说明段落。
        /// </summary>
        private void DrawDetailParagraph(Rect rect, string text)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.03f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = CDim;
            Widgets.Label(rect.ContractedBy(6f), text ?? "");
            ResetText();
        }

        /// <summary>
        /// 绘制蓝图码展示行。
        /// </summary>
        private void DrawDetailCodeLine(Rect rect, string blueprintCode)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
            Widgets.Label(rect, SimTranslation.T("RSMF.Blueprint.Network.CodeLine", (blueprintCode ?? "").Named("code")));
            ResetText();
        }

        /// <summary>
        /// 绘制兼容状态摘要。
        /// </summary>
        private void DrawCompatibilitySummary(Rect rect, BlueprintCompatibilityCheckResult compatibility)
        {
            string text = compatibility.IsCompatible
                ? SimTranslation.T("RSMF.Blueprint.Network.Compatible")
                : SimTranslation.T("RSMF.Blueprint.Network.MissingCount", compatibility.MissingMods.Count.Named("count"));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = compatibility.IsCompatible ? COk : CWarn;
            Widgets.Label(rect, text);
            ResetText();
        }

        /// <summary>
        /// 绘制依赖模组列表。
        /// </summary>
        private void DrawRequiredModsList(Rect rect, List<ShopBlueprintRequiredModData> requiredMods, BlueprintCompatibilityCheckResult compatibility)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.03f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Rect inner = rect.ContractedBy(6f);
            float tinyLine = Mathf.Max(18f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, tinyLine), SimTranslation.T("RSMF.Blueprint.Network.RequiredMods"));
            ResetText();

            if (requiredMods.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = CDim;
                Widgets.Label(new Rect(inner.x, inner.y + tinyLine + 4f, inner.width, tinyLine), SimTranslation.T("RSMF.Blueprint.Network.NoRequiredMods"));
                ResetText();
                return;
            }

            HashSet<string> missing = new HashSet<string>(
                compatibility.MissingMods.Where(m => m != null && !string.IsNullOrWhiteSpace(m.packageId)).Select(m => m.packageId),
                StringComparer.OrdinalIgnoreCase);

            float y = inner.y + tinyLine + 6f;
            float rowHeight = Mathf.Max(40f, Text.LineHeightOf(GameFont.Tiny) * 2f + 12f);
            float viewHeight = requiredMods.Count * rowHeight;
            Rect outRect = new Rect(inner.x, y, inner.width, Math.Max(24f, inner.yMax - y));
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, viewHeight);
            Widgets.BeginScrollView(outRect, ref blueprintNetworkModScrollPos, viewRect);
            for (int i = 0; i < requiredMods.Count; i++)
            {
                ShopBlueprintRequiredModData mod = requiredMods[i];
                Rect row = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 4f);
                DrawRequiredModRow(row, mod, missing.Contains(mod?.packageId ?? ""));
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制单个依赖模组行。
        /// </summary>
        private void DrawRequiredModRow(Rect rect, ShopBlueprintRequiredModData mod, bool missing)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.02f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.06f));

            string label = mod?.displayName;
            if (string.IsNullOrWhiteSpace(label))
                label = mod?.packageId ?? SimTranslation.T("RSMF.Common.Unknown");

            string status = missing ? SimTranslation.T("RSMF.Blueprint.Network.MissingTag") : SimTranslation.T("RSMF.Blueprint.Network.InstalledTag");
            float tinyLine = Mathf.Max(18f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            float buttonWidth = 84f;
            float buttonHeight = Mathf.Max(26f, Text.LineHeightOf(GameFont.Tiny) + 8f);
            float textWidth = Mathf.Max(80f, rect.width - buttonWidth - 18f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = missing ? CWarn : Color.white;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, textWidth, tinyLine), label);
            GUI.color = CDim;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f + tinyLine, textWidth, tinyLine), status + " · " + (mod?.packageId ?? ""));
            ResetText();

            bool canOpenWorkshop = missing && mod != null && !string.IsNullOrWhiteSpace(mod.steamWorkshopUrl);
            Rect openRect = new Rect(rect.xMax - buttonWidth, rect.y + Mathf.Max(4f, (rect.height - buttonHeight) / 2f), 76f, buttonHeight);
            if (SimUiStyle.DrawSecondaryButton(openRect, SimTranslation.T("RSMF.Blueprint.Network.OpenWorkshop"), canOpenWorkshop, GameFont.Tiny))
                Application.OpenURL(mod.steamWorkshopUrl);
        }

        /// <summary>
        /// 绘制详情操作按钮。
        /// </summary>
        private void DrawDetailActions(Rect rect, BlueprintNetworkDetailData detail, BlueprintCompatibilityCheckResult compatibility)
        {
            float buttonHeight = Mathf.Max(30f, Text.LineHeightOf(GameFont.Tiny) + 10f);
            float halfWidth = (rect.width - 8f) / 2f;
            Rect topLeft = new Rect(rect.x, rect.y, halfWidth, buttonHeight);
            Rect topRight = new Rect(rect.x + halfWidth + 8f, rect.y, halfWidth, buttonHeight);
            Rect bottomLeft = new Rect(rect.x, rect.y + buttonHeight + 6f, halfWidth, buttonHeight);
            Rect bottomRight = new Rect(rect.x + halfWidth + 8f, rect.y + buttonHeight + 6f, halfWidth, buttonHeight);

            bool canDownload = compatibility.IsCompatible && blueprintNetworkDetailTask == null;
            if (SimUiStyle.DrawPrimaryButton(topLeft, SimTranslation.T("RSMF.Blueprint.Network.DownloadImport"), canDownload, GameFont.Tiny))
                DownloadBlueprintNetworkDetail(detail, compatibility);

            if (SimUiStyle.DrawSecondaryButton(topRight, SimTranslation.T("RSMF.Blueprint.Network.Like"), blueprintNetworkDetailTask == null, GameFont.Tiny))
                LikeBlueprintNetworkDetail(detail);

            if (SimUiStyle.DrawSecondaryButton(bottomLeft, SimTranslation.T("RSMF.Blueprint.Network.CopyCode"), true, GameFont.Tiny))
                CopyBlueprintCode(detail.blueprintCode);

            bool isMine = blueprintSteamSession != null
                && blueprintSteamSession.IsAvailable
                && !string.IsNullOrWhiteSpace(detail.steamId)
                && string.Equals(blueprintSteamSession.SteamId, detail.steamId, StringComparison.OrdinalIgnoreCase);
            if (SimUiStyle.DrawDangerButton(bottomRight, SimTranslation.T("RSMF.Blueprint.Network.DeleteMine"), isMine && blueprintNetworkDetailTask == null, GameFont.Tiny))
                DeleteMyBlueprintNetworkDetail(detail);
        }

        /// <summary>
        /// 负责尝试打开网络蓝图标签并执行前置检查。
        /// </summary>
        private void TryOpenBlueprintNetworkTab()
        {
            blueprintShowNetworkTab = true;
            blueprintNetworkError = "";
            blueprintNetworkMessage = "";
            blueprintSteamSession = SteamSessionResolver.TryGetCurrentSession();

            if (!blueprintSteamSession.IsAvailable)
            {
                blueprintNetworkError = blueprintSteamSession.ErrorMessage;
                blueprintNetworkStatus = null;
                return;
            }

            if (blueprintNetworkStatus == null && blueprintNetworkStatusTask == null)
                RefreshBlueprintNetworkStatus();
            if (blueprintNetworkPagedList == null && blueprintNetworkListTask == null)
                RefreshBlueprintNetworkList();
        }

        /// <summary>
        /// 切换网络蓝图排序模式并重置分页。
        /// </summary>
        private void SwitchBlueprintNetworkSortMode(BlueprintNetworkSortMode mode)
        {
            if (blueprintNetworkSortMode == mode)
                return;

            blueprintNetworkSortMode = mode;
            blueprintNetworkPage = 1;
            blueprintNetworkPagedList = null;
            blueprintNetworkDetail = null;
            blueprintNetworkScrollPos = Vector2.zero;
            blueprintNetworkDetailScrollPos = Vector2.zero;
            blueprintNetworkModScrollPos = Vector2.zero;
            RefreshBlueprintNetworkList();
        }

        /// <summary>
        /// 翻页并刷新当前网络蓝图列表。
        /// </summary>
        private void ChangeBlueprintNetworkPage(int page)
        {
            blueprintNetworkPage = Math.Max(1, page);
            blueprintNetworkScrollPos = Vector2.zero;
            blueprintNetworkDetailScrollPos = Vector2.zero;
            RefreshBlueprintNetworkList();
        }

        /// <summary>
        /// 发起网络蓝图服务状态检查。
        /// </summary>
        private void RefreshBlueprintNetworkStatus()
        {
            EnsureBlueprintNetworkCts();
            blueprintNetworkStatusTask = BlueprintNetworkApiClient.GetStatusAsync(blueprintNetworkCts.Token);
        }

        /// <summary>
        /// 发起网络蓝图分页列表请求。
        /// </summary>
        private void RefreshBlueprintNetworkList()
        {
            if (blueprintSteamSession == null || !blueprintSteamSession.IsAvailable)
                blueprintSteamSession = SteamSessionResolver.TryGetCurrentSession();

            if (!blueprintSteamSession.IsAvailable)
            {
                blueprintNetworkError = blueprintSteamSession.ErrorMessage;
                return;
            }

            EnsureBlueprintNetworkCts();
            blueprintNetworkError = "";
            blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.Loading");
            IEnumerable<string> activePackageIds = blueprintNetworkSortMode == BlueprintNetworkSortMode.Compatible
                ? BlueprintModCompatibilityChecker.GetActivePackageIds()
                : null;
            string steamId = blueprintNetworkSortMode == BlueprintNetworkSortMode.Mine ? blueprintSteamSession.SteamId : "";
            blueprintNetworkListTask = BlueprintNetworkApiClient.GetPagedListAsync(
                blueprintNetworkSortMode,
                blueprintNetworkPage,
                BlueprintNetworkPageSize,
                steamId,
                activePackageIds,
                blueprintNetworkCts.Token);
        }

        /// <summary>
        /// 拉取网络蓝图详情。
        /// </summary>
        private void OpenBlueprintNetworkDetail(string blueprintCode)
        {
            if (string.IsNullOrWhiteSpace(blueprintCode))
                return;

            EnsureBlueprintNetworkCts();
            blueprintNetworkError = "";
            blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.LoadingDetail");
            blueprintNetworkDetailScrollPos = Vector2.zero;
            blueprintNetworkModScrollPos = Vector2.zero;
            blueprintNetworkDetailTask = BlueprintNetworkApiClient.GetDetailAsync(blueprintCode, blueprintNetworkCts.Token);
        }

        /// <summary>
        /// 轮询并收取网络蓝图异步任务结果。
        /// </summary>
        private void PollBlueprintNetworkTasks()
        {
            PollStatusTask();
            PollListTask();
            PollDetailTask();
            PollRemotePreviewTasks();
        }

        /// <summary>
        /// 上传本地蓝图到网络平台。
        /// </summary>
        private async void UploadBlueprintRecordToNetwork(ShopBlueprintLocalRecord record)
        {
            if (record?.Data == null)
                return;

            if (blueprintSteamSession == null || !blueprintSteamSession.IsAvailable)
                blueprintSteamSession = SteamSessionResolver.TryGetCurrentSession();
            if (!blueprintSteamSession.IsAvailable)
            {
                Messages.Message(blueprintSteamSession.ErrorMessage, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(record.Data.remoteBlueprintCode))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(SimTranslation.T("RSMF.Blueprint.Network.UpdateConfirm"), delegate
                {
                    ExecuteBlueprintUpload(record);
                }));
                return;
            }

            ExecuteBlueprintUpload(record);
        }

        /// <summary>
        /// 执行本地蓝图上传，并在成功后同步本地远端来源信息。
        /// </summary>
        private async void ExecuteBlueprintUpload(ShopBlueprintLocalRecord record)
        {
            if (record?.Data == null)
                return;

            try
            {
                EnsureBlueprintNetworkCts();
                bool isUpdate = !string.IsNullOrWhiteSpace(record.Data.remoteBlueprintCode);
                blueprintNetworkMessage = isUpdate
                    ? SimTranslation.T("RSMF.Blueprint.Network.Updating")
                    : SimTranslation.T("RSMF.Blueprint.Network.Uploading");
                BlueprintNetworkDetailData detail = await BlueprintNetworkApiClient.UploadAsync(record, blueprintSteamSession.SteamId, blueprintNetworkCts.Token);
                if (detail != null)
                    SyncUploadedBlueprintRecord(record, detail);
                blueprintNetworkMessage = isUpdate
                    ? SimTranslation.T("RSMF.Blueprint.Network.UpdateSuccess")
                    : SimTranslation.T("RSMF.Blueprint.Network.UploadSuccess");
                Messages.Message(blueprintNetworkMessage, MessageTypeDefOf.PositiveEvent, false);
                blueprintShowNetworkTab = true;
                blueprintNetworkDetail = detail;
                blueprintNetworkPage = 1;
                RefreshBlueprintNetworkList();
            }
            catch (Exception ex)
            {
                Log.Error("[RSMF 网络蓝图] 上传蓝图失败\n"
                    + "标签=" + (record?.Data?.label ?? "<null>") + "\n"
                    + ex.GetType().Name + ": " + GetSafeBlueprintNetworkErrorMessage(ex));
                string message = SimTranslation.T("RSMF.Blueprint.Network.Error.UploadFailedWithMessage", GetSafeBlueprintNetworkErrorMessage(ex).Named("message"));
                blueprintNetworkError = message;
                Messages.Message(message, MessageTypeDefOf.RejectInput, false);
            }
        }

        /// <summary>
        /// 下载网络蓝图并导入本地蓝图库。
        /// </summary>
        private async void DownloadBlueprintNetworkDetail(BlueprintNetworkDetailData detail, BlueprintCompatibilityCheckResult compatibility)
        {
            if (detail == null)
                return;
            if (!compatibility.IsCompatible)
            {
                Messages.Message(BuildMissingModsMessage(compatibility), MessageTypeDefOf.RejectInput, false);
                return;
            }

            try
            {
                EnsureBlueprintNetworkCts();
                blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.Downloading");
                byte[] blueprintBytes = await BlueprintNetworkApiClient.DownloadBlueprintAsync(detail.blueprintCode, blueprintNetworkCts.Token);
                byte[] previewBytes = await BlueprintNetworkApiClient.DownloadPreviewAsync(detail.blueprintCode, blueprintNetworkCts.Token);
                if (!BlueprintNetworkImportService.TryImport(detail, blueprintBytes, previewBytes, out ShopBlueprintLocalRecord _, out string error))
                {
                    string importError = error ?? SimTranslation.T("RSMF.Blueprint.Network.Error.ImportFailed");
                    blueprintNetworkError = importError;
                    Messages.Message(importError, MessageTypeDefOf.RejectInput, false);
                    return;
                }

                ReloadBlueprintRecords();
                blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.DownloadSuccess");
                Messages.Message(blueprintNetworkMessage, MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                string message = SimTranslation.T("RSMF.Blueprint.Network.Error.DownloadFailedWithMessage", GetSafeBlueprintNetworkErrorMessage(ex).Named("message"));
                blueprintNetworkError = message;
                Messages.Message(message, MessageTypeDefOf.RejectInput, false);
            }
        }

        /// <summary>
        /// 通过顶部输入框中的蓝图码打开详情。
        /// </summary>
        private void OpenBlueprintNetworkDetailByCodeInput()
        {
            string blueprintCode = (blueprintNetworkCodeBuffer ?? "").Trim();
            if (string.IsNullOrWhiteSpace(blueprintCode))
            {
                blueprintNetworkError = SimTranslation.T("RSMF.Blueprint.Network.Error.CodeEmpty");
                Messages.Message(blueprintNetworkError, MessageTypeDefOf.RejectInput, false);
                return;
            }

            OpenBlueprintNetworkDetail(blueprintCode);
        }

        /// <summary>
        /// 通过顶部输入框中的蓝图码直接拉取详情并执行导入。
        /// </summary>
        private async void DownloadBlueprintByCodeInput()
        {
            string blueprintCode = (blueprintNetworkCodeBuffer ?? "").Trim();
            if (string.IsNullOrWhiteSpace(blueprintCode))
            {
                blueprintNetworkError = SimTranslation.T("RSMF.Blueprint.Network.Error.CodeEmpty");
                Messages.Message(blueprintNetworkError, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (blueprintSteamSession == null || !blueprintSteamSession.IsAvailable)
                blueprintSteamSession = SteamSessionResolver.TryGetCurrentSession();
            if (blueprintSteamSession == null || !blueprintSteamSession.IsAvailable)
            {
                blueprintNetworkError = blueprintSteamSession?.ErrorMessage ?? SimTranslation.T("RSMF.Blueprint.Network.Error.SteamNotLoggedIn");
                Messages.Message(blueprintNetworkError, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (blueprintNetworkStatus == null || !blueprintNetworkStatus.available)
            {
                blueprintNetworkError = SimTranslation.T("RSMF.Blueprint.Network.Error.ServiceUnavailable");
                Messages.Message(blueprintNetworkError, MessageTypeDefOf.RejectInput, false);
                return;
            }

            try
            {
                EnsureBlueprintNetworkCts();
                blueprintNetworkError = "";
                blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.LoadingDetail");
                BlueprintNetworkDetailData detail = await BlueprintNetworkApiClient.GetDetailAsync(blueprintCode, blueprintNetworkCts.Token);
                blueprintNetworkDetail = detail;
                blueprintNetworkDetailScrollPos = Vector2.zero;
                blueprintNetworkModScrollPos = Vector2.zero;

                BlueprintCompatibilityCheckResult compatibility = BlueprintModCompatibilityChecker.CheckCompatibility(detail?.requiredMods);
                if (detail == null)
                {
                    string message = SimTranslation.T("RSMF.Blueprint.Network.Error.DetailFailed");
                    blueprintNetworkError = message;
                    Messages.Message(message, MessageTypeDefOf.RejectInput, false);
                    return;
                }

                if (!compatibility.IsCompatible)
                {
                    string message = BuildMissingModsMessage(compatibility);
                    blueprintNetworkError = message;
                    Messages.Message(message, MessageTypeDefOf.RejectInput, false);
                    return;
                }

                DownloadBlueprintNetworkDetail(detail, compatibility);
            }
            catch (Exception ex)
            {
                string message = SimTranslation.T("RSMF.Blueprint.Network.Error.DownloadByCodeFailedWithMessage", GetSafeBlueprintNetworkErrorMessage(ex).Named("message"));
                blueprintNetworkError = message;
                Messages.Message(message, MessageTypeDefOf.RejectInput, false);
            }
        }

        /// <summary>
        /// 删除自己上传的网络蓝图。
        /// </summary>
        private async void DeleteMyBlueprintNetworkDetail(BlueprintNetworkDetailData detail)
        {
            if (detail == null || blueprintSteamSession == null || !blueprintSteamSession.IsAvailable)
                return;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(SimTranslation.T("RSMF.Blueprint.Network.DeleteConfirm"), async delegate
            {
                try
                {
                    EnsureBlueprintNetworkCts();
                    bool ok = await BlueprintNetworkApiClient.DeleteOwnBlueprintAsync(detail.blueprintCode, blueprintSteamSession.SteamId, blueprintNetworkCts.Token);
                    if (!ok)
                    {
                        Messages.Message(SimTranslation.T("RSMF.Blueprint.Network.Error.DeleteFailed"), MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    blueprintNetworkDetail = null;
                    blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.DeleteSuccess");
                    Messages.Message(blueprintNetworkMessage, MessageTypeDefOf.PositiveEvent, false);
                    RefreshBlueprintNetworkList();
                }
                catch (Exception ex)
                {
                    Messages.Message(SimTranslation.T("RSMF.Blueprint.Network.Error.DeleteFailedWithMessage", GetSafeBlueprintNetworkErrorMessage(ex).Named("message")), MessageTypeDefOf.RejectInput, false);
                }
            }));
        }

        /// <summary>
        /// 点赞当前网络蓝图详情。
        /// </summary>
        private async void LikeBlueprintNetworkDetail(BlueprintNetworkDetailData detail)
        {
            if (detail == null)
                return;
            if (blueprintSteamSession == null || !blueprintSteamSession.IsAvailable)
                blueprintSteamSession = SteamSessionResolver.TryGetCurrentSession();
            if (!blueprintSteamSession.IsAvailable)
            {
                Messages.Message(blueprintSteamSession.ErrorMessage, MessageTypeDefOf.RejectInput, false);
                return;
            }

            try
            {
                EnsureBlueprintNetworkCts();
                bool ok = await BlueprintNetworkApiClient.LikeAsync(detail.blueprintCode, blueprintSteamSession.SteamId, blueprintNetworkCts.Token);
                if (!ok)
                {
                    Messages.Message(SimTranslation.T("RSMF.Blueprint.Network.Error.LikeFailed"), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.LikeSuccess");
                Messages.Message(blueprintNetworkMessage, MessageTypeDefOf.PositiveEvent, false);
                OpenBlueprintNetworkDetail(detail.blueprintCode);
                RefreshBlueprintNetworkList();
            }
            catch (Exception ex)
            {
                Messages.Message(SimTranslation.T("RSMF.Blueprint.Network.Error.LikeFailedWithMessage", GetSafeBlueprintNetworkErrorMessage(ex).Named("message")), MessageTypeDefOf.RejectInput, false);
            }
        }

        /// <summary>
        /// 复制蓝图码到系统剪贴板。
        /// </summary>
        private void CopyBlueprintCode(string blueprintCode)
        {
            GUIUtility.systemCopyBuffer = blueprintCode ?? "";
            blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.CodeCopied");
            Messages.Message(blueprintNetworkMessage, MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>
        /// 同步本地蓝图的远端上传信息，避免重复上传并让列表能识别已上传状态。
        /// </summary>
        private void SyncUploadedBlueprintRecord(ShopBlueprintLocalRecord record, BlueprintNetworkDetailData detail)
        {
            if (record?.Data == null || detail == null || string.IsNullOrWhiteSpace(detail.blueprintCode))
                return;

            record.Data.remoteBlueprintCode = detail.blueprintCode ?? "";
            record.Data.remoteAuthorSteamId = detail.steamId ?? blueprintSteamSession?.SteamId ?? "";
            record.Data.remoteImportedAtTicks = DateTime.UtcNow.Ticks;
            record.Data.requiredMods = detail.requiredMods ?? record.Data.requiredMods ?? new List<ShopBlueprintRequiredModData>();
            ShopBlueprintLibrary.EnsureDataDefaults(record.Data);
            if (!ShopBlueprintLibrary.TryUpdateRecord(record, record.Data, out string error))
                Log.Warning("[RSMF 网络蓝图] 上传成功后同步本地蓝图状态失败：" + (error ?? "未知错误"));
            ReloadBlueprintRecords();
        }

        /// <summary>
        /// 取消网络蓝图页正在进行的请求。
        /// </summary>
        private void CancelBlueprintNetworkRequests()
        {
            if (blueprintNetworkCts == null)
                goto ClearCache;

            try
            {
                blueprintNetworkCts.Cancel();
                blueprintNetworkCts.Dispose();
            }
            catch
            {
            }
            finally
            {
                blueprintNetworkCts = null;
                blueprintNetworkStatusTask = null;
                blueprintNetworkListTask = null;
                blueprintNetworkDetailTask = null;
            }

        ClearCache:
            if (blueprintRemotePreviewCache != null)
            {
                foreach (Texture2D texture in blueprintRemotePreviewCache.Values)
                {
                    if (texture != null)
                        UnityEngine.Object.Destroy(texture);
                }

                blueprintRemotePreviewCache.Clear();
            }

            if (blueprintRemotePreviewTasks != null)
                blueprintRemotePreviewTasks.Clear();
        }

        /// <summary>
        /// 确保网络蓝图请求令牌存在。
        /// </summary>
        private void EnsureBlueprintNetworkCts()
        {
            if (blueprintNetworkCts == null || blueprintNetworkCts.IsCancellationRequested)
            {
                blueprintNetworkCts?.Dispose();
                blueprintNetworkCts = new CancellationTokenSource();
            }
        }

        /// <summary>
        /// 收取服务状态异步任务结果。
        /// </summary>
        private void PollStatusTask()
        {
            if (blueprintNetworkStatusTask == null || !blueprintNetworkStatusTask.IsCompleted)
                return;

            if (blueprintNetworkStatusTask.IsFaulted || blueprintNetworkStatusTask.IsCanceled)
            {
                blueprintNetworkError = SimTranslation.T("RSMF.Blueprint.Network.Error.ServiceUnavailable");
                blueprintNetworkStatus = null;
            }
            else
            {
                blueprintNetworkStatus = blueprintNetworkStatusTask.Result;
                if (blueprintNetworkStatus != null && blueprintNetworkStatus.available)
                    blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.ServiceAvailable");
                else
                    blueprintNetworkError = SimTranslation.T("RSMF.Blueprint.Network.Error.ServiceUnavailable");
            }

            blueprintNetworkStatusTask = null;
        }

        /// <summary>
        /// 收取分页列表异步任务结果。
        /// </summary>
        private void PollListTask()
        {
            if (blueprintNetworkListTask == null || !blueprintNetworkListTask.IsCompleted)
                return;

            if (blueprintNetworkListTask.IsFaulted || blueprintNetworkListTask.IsCanceled)
            {
                blueprintNetworkError = ExtractTaskError(blueprintNetworkListTask, SimTranslation.T("RSMF.Blueprint.Network.Error.ListFailed"));
                blueprintNetworkPagedList = new BlueprintNetworkPagedListData
                {
                    page = blueprintNetworkPage,
                    pageSize = BlueprintNetworkPageSize,
                    totalPages = 1
                };
            }
            else
            {
                blueprintNetworkPagedList = blueprintNetworkListTask.Result ?? new BlueprintNetworkPagedListData();
                blueprintNetworkPage = Math.Max(1, blueprintNetworkPagedList.page > 0 ? blueprintNetworkPagedList.page : blueprintNetworkPage);
                blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.ListLoaded");
            }

            blueprintNetworkListTask = null;
        }

        /// <summary>
        /// 收取详情异步任务结果。
        /// </summary>
        private void PollDetailTask()
        {
            if (blueprintNetworkDetailTask == null || !blueprintNetworkDetailTask.IsCompleted)
                return;

            if (blueprintNetworkDetailTask.IsFaulted || blueprintNetworkDetailTask.IsCanceled)
            {
                blueprintNetworkError = ExtractTaskError(blueprintNetworkDetailTask, SimTranslation.T("RSMF.Blueprint.Network.Error.DetailFailed"));
            }
            else
            {
                blueprintNetworkDetail = blueprintNetworkDetailTask.Result;
                blueprintNetworkMessage = SimTranslation.T("RSMF.Blueprint.Network.DetailLoaded");
            }

            blueprintNetworkDetailTask = null;
        }

        /// <summary>
        /// 计算网络蓝图顶部栏高度，负责为多行状态和分页区域预留空间。
        /// </summary>
        private float CalculateBlueprintNetworkTopBarHeight(float width)
        {
            float innerWidth = Mathf.Max(240f, width - 20f);
            float titleHeight = Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f);
            float statusHeight = CalculateBlueprintNetworkStatusLineHeight(innerWidth);
            float codeBarHeight = CalculateBlueprintNetworkCodeBarHeight(innerWidth);
            float tabsHeight = CalculateBlueprintNetworkSortTabsHeight(innerWidth);
            float pagerHeight = Mathf.Max(28f, Text.LineHeightOf(GameFont.Tiny) + 8f);
            return 20f + titleHeight + 6f + statusHeight + 8f + codeBarHeight + 8f + tabsHeight + 8f + pagerHeight;
        }

        /// <summary>
        /// 计算蓝图码输入栏高度，负责给输入框和按钮预留安全空间。
        /// </summary>
        private float CalculateBlueprintNetworkCodeBarHeight(float width)
        {
            float lineHeight = Mathf.Max(30f, Text.LineHeightOf(GameFont.Tiny) + 10f);
            float labelWidth = Mathf.Max(72f, Text.CalcSize(SimTranslation.T("RSMF.Blueprint.Network.CodeInputLabel")).x + 8f);
            float buttonWidth = Mathf.Max(94f, Text.CalcSize(SimTranslation.T("RSMF.Blueprint.Network.DirectDownload")).x + 20f);
            float gap = 8f;
            bool wrapButtons = labelWidth + 160f + buttonWidth * 2f + gap * 3f > width;
            return wrapButtons ? lineHeight * 2f + 6f : lineHeight;
        }

        /// <summary>
        /// 计算网络蓝图状态区高度，负责容纳状态文本与错误提示换行。
        /// </summary>
        private float CalculateBlueprintNetworkStatusLineHeight(float width)
        {
            string message = !string.IsNullOrWhiteSpace(blueprintNetworkError) ? blueprintNetworkError : blueprintNetworkMessage;
            float lineHeight = Mathf.Max(18f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            float steamHeight = Mathf.Max(lineHeight, Text.CalcHeight(BuildSteamStatusLine(), Mathf.Max(220f, width)));
            float serviceHeight = Mathf.Max(lineHeight, Text.CalcHeight(BuildServiceStatusLine(), Mathf.Max(220f, width)));
            float messageHeight = string.IsNullOrWhiteSpace(message)
                ? lineHeight
                : Mathf.Max(lineHeight, Text.CalcHeight(message, Mathf.Max(220f, width)));
            return steamHeight + 2f + serviceHeight + 4f + messageHeight;
        }

        /// <summary>
        /// 计算分类标签区高度，负责让按钮在宽度不足时自动换行。
        /// </summary>
        private float CalculateBlueprintNetworkSortTabsHeight(float width)
        {
            BlueprintNetworkSortMode[] modes =
            {
                BlueprintNetworkSortMode.Latest,
                BlueprintNetworkSortMode.Hot,
                BlueprintNetworkSortMode.Downloads,
                BlueprintNetworkSortMode.Mine,
                BlueprintNetworkSortMode.Compatible
            };

            float buttonHeight = Mathf.Max(30f, Text.LineHeightOf(GameFont.Tiny) + 12f);
            float x = 0f;
            float y = 0f;
            for (int i = 0; i < modes.Length; i++)
            {
                string label = GetBlueprintNetworkSortLabel(modes[i]);
                float buttonWidth = Mathf.Max(96f, Text.CalcSize(label).x + 24f);
                if (x > 0f && x + buttonWidth > width)
                {
                    x = 0f;
                    y += buttonHeight + 6f;
                }

                x += buttonWidth + 8f;
            }

            return y + buttonHeight;
        }

        /// <summary>
        /// 计算详情内容总高度，负责驱动详情滚动区域。
        /// </summary>
        private float CalculateBlueprintNetworkDetailContentHeight(float width, BlueprintNetworkDetailData detail, BlueprintCompatibilityCheckResult compatibility)
        {
            float titleHeight = Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f);
            float previewHeight = 164f;
            float metaHeight = Mathf.Max(22f, Text.CalcHeight(BuildDetailMetaText(detail), width));
            float descHeight = Mathf.Max(56f, Text.CalcHeight(detail.description ?? SimTranslation.T("RSMF.Common.NoDescription"), Mathf.Max(60f, width - 12f)) + 12f);
            float codeHeight = Mathf.Max(22f, Text.CalcHeight(BuildDetailCodeText(detail.blueprintCode), width));
            float compatibilityHeight = Mathf.Max(22f, Text.CalcHeight(BuildCompatibilitySummaryText(compatibility), width));
            float modsHeight = CalculateRequiredModsListHeight(width, detail.requiredMods);
            return previewHeight + 8f + titleHeight + 6f + metaHeight + 8f + descHeight + 8f + codeHeight + 8f + compatibilityHeight + 8f + modsHeight;
        }

        /// <summary>
        /// 在面板中央绘制安全换行的提示文本，避免长中文跨出当前面板并与邻近区域重叠。
        /// </summary>
        private void DrawCenteredPanelMessage(Rect rect, string text, Color color)
        {
            Rect inner = rect.ContractedBy(14f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = true;
            GUI.color = color;
            Widgets.Label(inner, text ?? "");
            ResetText();
        }

        /// <summary>
        /// 计算网络蓝图列表单行高度，负责给中文统计文本和按钮留出足够空间。
        /// </summary>
        private float CalculateBlueprintNetworkListRowHeight()
        {
            float titleHeight = Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f);
            float metaHeight = Mathf.Max(20f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            return Mathf.Max(94f, 14f + titleHeight + metaHeight * 2f + 8f);
        }

        /// <summary>
        /// 计算详情操作区高度，负责避免中文按钮在双行区域内发生裁切。
        /// </summary>
        private float CalculateBlueprintNetworkActionAreaHeight()
        {
            float buttonHeight = Mathf.Max(30f, Text.LineHeightOf(GameFont.Tiny) + 10f);
            return buttonHeight * 2f + 6f;
        }

        /// <summary>
        /// 绘制详情滚动内容，负责把预览、说明和依赖列表按顺序排布。
        /// </summary>
        private void DrawBlueprintNetworkDetailContent(Rect rect, BlueprintNetworkDetailData detail, BlueprintCompatibilityCheckResult compatibility)
        {
            float y = rect.y;
            Rect previewRect = new Rect(rect.x, y, rect.width, 164f);
            DrawBlueprintRemotePreview(previewRect, detail.previewUrl);
            y = previewRect.yMax + 8f;

            float titleHeight = Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f);
            Rect titleRect = new Rect(rect.x, y, rect.width, titleHeight);
            DrawDetailTitleLine(titleRect, detail);
            y = titleRect.yMax + 6f;

            float metaHeight = Mathf.Max(22f, Text.CalcHeight(BuildDetailMetaText(detail), rect.width));
            Rect metaRect = new Rect(rect.x, y, rect.width, metaHeight);
            DrawDetailMetaLine(metaRect, detail);
            y = metaRect.yMax + 8f;

            float descHeight = Mathf.Max(56f, Text.CalcHeight(detail.description ?? SimTranslation.T("RSMF.Common.NoDescription"), Mathf.Max(60f, rect.width - 12f)) + 12f);
            Rect descRect = new Rect(rect.x, y, rect.width, descHeight);
            DrawDetailParagraph(descRect, detail.description ?? SimTranslation.T("RSMF.Common.NoDescription"));
            y = descRect.yMax + 8f;

            float codeHeight = Mathf.Max(22f, Text.CalcHeight(BuildDetailCodeText(detail.blueprintCode), rect.width));
            Rect codeRect = new Rect(rect.x, y, rect.width, codeHeight);
            DrawDetailCodeLine(codeRect, detail.blueprintCode);
            y = codeRect.yMax + 8f;

            float compatibilityHeight = Mathf.Max(22f, Text.CalcHeight(BuildCompatibilitySummaryText(compatibility), rect.width));
            Rect compatibilityRect = new Rect(rect.x, y, rect.width, compatibilityHeight);
            DrawCompatibilitySummary(compatibilityRect, compatibility);
            y = compatibilityRect.yMax + 8f;

            float modsHeight = CalculateRequiredModsListHeight(rect.width, detail.requiredMods);
            Rect modsRect = new Rect(rect.x, y, rect.width, modsHeight);
            DrawRequiredModsList(modsRect, detail.requiredMods, compatibility);
        }

        /// <summary>
        /// 计算依赖模组列表区域高度，负责为滚动列表预留安全空间。
        /// </summary>
        private float CalculateRequiredModsListHeight(float width, List<ShopBlueprintRequiredModData> requiredMods)
        {
            float titleHeight = Mathf.Max(18f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            if (requiredMods.NullOrEmpty())
                return 12f + titleHeight + 6f + titleHeight + 6f;

            float rowHeight = Mathf.Max(40f, Text.LineHeightOf(GameFont.Tiny) * 2f + 12f);
            float visibleRows = Mathf.Min(4f, requiredMods.Count);
            return 12f + titleHeight + 6f + visibleRows * rowHeight + 6f;
        }

        /// <summary>
        /// 构建详情元信息文本，负责统一绘制前的高度测量与展示内容。
        /// </summary>
        private string BuildDetailMetaText(BlueprintNetworkDetailData detail)
        {
            return SimTranslation.T("RSMF.Blueprint.Network.DetailMeta",
                BuildAuthorDisplayName(detail.steamId).Named("author"),
                detail.likeCount.Named("likes"),
                detail.downloadCount.Named("downloads"),
                FormatBlueprintDisplayTime(detail.createdAt).Named("time"));
        }

        /// <summary>
        /// 构建详情蓝图码文本，负责统一绘制前的高度测量与展示内容。
        /// </summary>
        private string BuildDetailCodeText(string blueprintCode)
        {
            return SimTranslation.T("RSMF.Blueprint.Network.CodeLine", (blueprintCode ?? "").Named("code"));
        }

        /// <summary>
        /// 构建兼容状态文本，负责统一绘制前的高度测量与展示内容。
        /// </summary>
        private string BuildCompatibilitySummaryText(BlueprintCompatibilityCheckResult compatibility)
        {
            return compatibility.IsCompatible
                ? SimTranslation.T("RSMF.Blueprint.Network.Compatible")
                : SimTranslation.T("RSMF.Blueprint.Network.MissingCount", compatibility.MissingMods.Count.Named("count"));
        }

        /// <summary>
        /// 绘制远端蓝图预览图，负责在没有缓存时显示占位。
        /// </summary>
        private void DrawBlueprintRemotePreview(Rect rect, string previewUrl)
        {
            if (TryDrawCachedRemotePreview(rect, previewUrl))
                return;

            DrawBlueprintPreview(rect, ResolveRemotePreviewPath(previewUrl));
        }

        /// <summary>
        /// 返回网络排序标签文本。
        /// </summary>
        private string GetBlueprintNetworkSortLabel(BlueprintNetworkSortMode mode)
        {
            switch (mode)
            {
                case BlueprintNetworkSortMode.Hot:
                    return SimTranslation.T("RSMF.Blueprint.Network.Sort.Hot");
                case BlueprintNetworkSortMode.Downloads:
                    return SimTranslation.T("RSMF.Blueprint.Network.Sort.Downloads");
                case BlueprintNetworkSortMode.Mine:
                    return SimTranslation.T("RSMF.Blueprint.Network.Sort.Mine");
                case BlueprintNetworkSortMode.Compatible:
                    return SimTranslation.T("RSMF.Blueprint.Network.Sort.Compatible");
                default:
                    return SimTranslation.T("RSMF.Blueprint.Network.Sort.Latest");
            }
        }

        /// <summary>
        /// 构建 Steam 状态展示文本。
        /// </summary>
        private string BuildSteamStatusLine()
        {
            if (blueprintSteamSession == null || !blueprintSteamSession.IsAvailable)
                return SimTranslation.T("RSMF.Blueprint.Network.SteamStatusOffline");

            return SimTranslation.T("RSMF.Blueprint.Network.SteamStatusOnline",
                blueprintSteamSession.PersonaName.Named("name"),
                blueprintSteamSession.SteamId.Named("id"));
        }

        /// <summary>
        /// 构建服务状态展示文本。
        /// </summary>
        private string BuildServiceStatusLine()
        {
            if (blueprintNetworkStatus == null || !blueprintNetworkStatus.available)
                return SimTranslation.T("RSMF.Blueprint.Network.ServiceStatusOffline");

            return SimTranslation.T("RSMF.Blueprint.Network.ServiceStatusOnline",
                (blueprintNetworkStatus.version ?? SimTranslation.T("RSMF.Common.Unknown")).Named("version"));
        }

        /// <summary>
        /// 将详情或列表中的作者 SteamId 转成展示文本。
        /// </summary>
        private string BuildAuthorDisplayName(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId))
                return SimTranslation.T("RSMF.Blueprint.Network.UnknownAuthor");

            if (blueprintSteamSession != null
                && blueprintSteamSession.IsAvailable
                && string.Equals(blueprintSteamSession.SteamId, steamId, StringComparison.OrdinalIgnoreCase))
                return SimTranslation.T("RSMF.Blueprint.Network.Me");

            return steamId;
        }

        /// <summary>
        /// 格式化网络蓝图时间字符串。
        /// </summary>
        private string FormatBlueprintDisplayTime(string createdAt)
        {
            if (string.IsNullOrWhiteSpace(createdAt))
                return SimTranslation.T("RSMF.Common.Unknown");

            if (DateTimeOffset.TryParse(createdAt, out DateTimeOffset parsed))
                return parsed.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

            return createdAt;
        }

        /// <summary>
        /// 从详情兼容结果拼出缺失模组提示文本。
        /// </summary>
        private string BuildMissingModsMessage(BlueprintCompatibilityCheckResult compatibility)
        {
            if (compatibility == null || compatibility.MissingMods.NullOrEmpty())
                return SimTranslation.T("RSMF.Blueprint.Network.Error.ImportMissingMods");

            string names = string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), compatibility.MissingMods.Select(GetRequiredModDisplayName).ToArray());
            return SimTranslation.T("RSMF.Blueprint.Network.Error.ImportMissingModsWithList", names.Named("mods"));
        }

        /// <summary>
        /// 返回依赖模组显示名。
        /// </summary>
        private string GetRequiredModDisplayName(ShopBlueprintRequiredModData mod)
        {
            if (mod == null)
                return SimTranslation.T("RSMF.Common.Unknown");
            if (!string.IsNullOrWhiteSpace(mod.displayName))
                return mod.displayName;
            if (!string.IsNullOrWhiteSpace(mod.packageId))
                return mod.packageId;
            return SimTranslation.T("RSMF.Common.Unknown");
        }

        /// <summary>
        /// 负责把预览 URL 映射为本地缓存路径，目前先复用下载后本地蓝图预览。
        /// </summary>
        private string ResolveRemotePreviewPath(string previewUrl)
        {
            if (blueprintRecords.NullOrEmpty() || string.IsNullOrWhiteSpace(blueprintNetworkDetail?.blueprintCode))
                return null;

            ShopBlueprintLocalRecord imported = blueprintRecords.FirstOrDefault(record =>
                record?.Data != null &&
                !string.IsNullOrWhiteSpace(record.Data.remoteBlueprintCode) &&
                string.Equals(record.Data.remoteBlueprintCode, blueprintNetworkDetail.blueprintCode, StringComparison.OrdinalIgnoreCase));
            return imported?.PreviewPath;
        }

        /// <summary>
        /// 尝试绘制已缓存的远端预览图。
        /// </summary>
        private bool TryDrawCachedRemotePreview(Rect rect, string previewUrl)
        {
            if (string.IsNullOrWhiteSpace(previewUrl) || blueprintRemotePreviewCache == null)
                return false;

            if (!blueprintRemotePreviewCache.TryGetValue(previewUrl, out Texture2D texture) || texture == null)
            {
                TryCacheRemotePreview(previewUrl);
                blueprintRemotePreviewCache.TryGetValue(previewUrl, out texture);
            }

            if (texture == null)
                return false;

            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.35f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.16f));
            GUI.DrawTexture(rect.ContractedBy(4f), texture, ScaleMode.ScaleToFit);
            return true;
        }

        /// <summary>
        /// 负责同步拉取一次远端预览图并缓存在当前窗口生命周期内。
        /// </summary>
        private void TryCacheRemotePreview(string previewUrl)
        {
            if (string.IsNullOrWhiteSpace(previewUrl)
                || blueprintRemotePreviewCache == null
                || blueprintRemotePreviewTasks == null
                || blueprintRemotePreviewCache.ContainsKey(previewUrl)
                || blueprintRemotePreviewTasks.ContainsKey(previewUrl))
                return;

            blueprintRemotePreviewTasks[previewUrl] = DownloadRemotePreviewAsync(previewUrl);
        }

        /// <summary>
        /// 轮询远端预览图下载任务，并把结果转成纹理缓存。
        /// </summary>
        private void PollRemotePreviewTasks()
        {
            if (blueprintRemotePreviewTasks == null || blueprintRemotePreviewTasks.Count == 0)
                return;

            List<string> keys = blueprintRemotePreviewTasks.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                string url = keys[i];
                Task<byte[]> task = blueprintRemotePreviewTasks[url];
                if (task == null || !task.IsCompleted)
                    continue;

                if (!task.IsFaulted && !task.IsCanceled)
                    blueprintRemotePreviewCache[url] = CreatePreviewTexture(task.Result);
                else
                    blueprintRemotePreviewCache[url] = null;

                blueprintRemotePreviewTasks.Remove(url);
            }
        }

        /// <summary>
        /// 异步下载一张远端预览图。
        /// </summary>
        private async Task<byte[]> DownloadRemotePreviewAsync(string previewUrl)
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                return await client.GetByteArrayAsync(previewUrl);
            }
        }

        /// <summary>
        /// 把下载到的 PNG/JPG 数据转成可绘制纹理。
        /// </summary>
        private Texture2D CreatePreviewTexture(byte[] bytes)
        {
            if (bytes == null || bytes.Length <= 0)
                return null;

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(bytes))
                return texture;

            UnityEngine.Object.Destroy(texture);
            return null;
        }

        /// <summary>
        /// 提取任务异常里的可展示错误信息。
        /// </summary>
        private string ExtractTaskError(Task task, string fallback)
        {
            Exception ex = task.Exception?.GetBaseException();
            if (ex == null)
                return fallback;
            return fallback + " " + GetSafeBlueprintNetworkErrorMessage(ex);
        }

        /// <summary>
        /// 负责把网络蓝图异常文本脱敏，避免把服务地址直接显示给玩家或写入日志。
        /// </summary>
        private string GetSafeBlueprintNetworkErrorMessage(Exception ex)
        {
            string message = ex?.GetBaseException()?.Message ?? SimTranslation.T("RSMF.Common.Unknown");
            if (string.IsNullOrWhiteSpace(message))
                return SimTranslation.T("RSMF.Common.Unknown");

            string sanitized = message.Replace("https://", string.Empty).Replace("http://", string.Empty);
            sanitized = sanitized.Replace("chezhou.icu", "网络蓝图服务");
            sanitized = sanitized.Replace("blueprint-api", "服务接口");
            sanitized = sanitized.Replace("/api/blueprints", string.Empty);
            sanitized = sanitized.Replace("/api/admin", string.Empty);
            return sanitized;
        }
    }
}
