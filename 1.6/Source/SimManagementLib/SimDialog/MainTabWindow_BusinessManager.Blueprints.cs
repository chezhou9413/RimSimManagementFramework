using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager
    {
        /// <summary>
        /// 绘制蓝图管理页顶层结构，负责本地蓝图和网络蓝图双标签切换。
        /// </summary>
        private void DrawBlueprintPage(Rect rect)
        {
            EnsureBlueprintRecords();
            PollBlueprintNetworkTasks();

            Rect tabRect = new Rect(rect.x, rect.y, rect.width, 34f);
            DrawBlueprintTabs(tabRect);

            Rect contentRect = new Rect(rect.x, tabRect.yMax + 8f, rect.width, rect.height - tabRect.height - 8f);
            if (blueprintShowNetworkTab)
            {
                DrawBlueprintNetworkPage(contentRect);
                return;
            }

            DrawBlueprintLocalPage(contentRect);
        }

        /// <summary>
        /// 绘制蓝图页内标签栏，负责切换本地蓝图和网络蓝图。
        /// </summary>
        private void DrawBlueprintTabs(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.18f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            float tabWidth = 140f;
            Rect localRect = new Rect(rect.x + 8f, rect.y + 2f, tabWidth, rect.height - 4f);
            Rect networkRect = new Rect(localRect.xMax + 8f, rect.y + 2f, tabWidth, rect.height - 4f);

            if (SimUiStyle.DrawTabButton(localRect, SimTranslation.T("RSMF.Blueprint.Tab.Local"), !blueprintShowNetworkTab, CDim))
                blueprintShowNetworkTab = false;

            if (SimUiStyle.DrawTabButton(networkRect, SimTranslation.T("RSMF.Blueprint.Tab.Network"), blueprintShowNetworkTab, CDim))
                TryOpenBlueprintNetworkTab();
        }

        /// <summary>
        /// 绘制本地蓝图子页，负责复用原有本地蓝图工具栏和列表。
        /// </summary>
        private void DrawBlueprintLocalPage(Rect rect)
        {
            Rect topRect = new Rect(rect.x, rect.y, rect.width, 118f);
            DrawBlueprintToolbar(topRect);

            Rect listRect = new Rect(rect.x, topRect.yMax + 8f, rect.width, rect.height - topRect.height - 8f);
            DrawBlueprintList(listRect);
        }

        /// <summary>
        /// 绘制蓝图保存工具栏和固定尺寸提示区域。
        /// </summary>
        private void DrawBlueprintToolbar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.22f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 6f, 360f, 28f), SimTranslation.T("RSMF.Blueprint.Title"));

            Text.Font = GameFont.Tiny;
            GUI.color = CDim;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 34f, rect.width - 20f, 40f), SimTranslation.T("RSMF.Blueprint.Description"));
            ResetText();

            float y = rect.yMax - 36f;
            Rect sizeLabel = new Rect(rect.x + 10f, y, 96f, 28f);
            Rect sizeValue = new Rect(sizeLabel.xMax + 6f, y, 82f, 28f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(sizeLabel, SimTranslation.T("RSMF.Blueprint.MaxSize"));
            Widgets.Label(sizeValue, SimTranslation.T("RSMF.Blueprint.MaxSizeValue", BlueprintMaxSize.Named("max")));
            TooltipHandler.TipRegion(sizeValue, SimTranslation.T("RSMF.Blueprint.MaxSizeTip"));
            ResetText();

            float buttonW = 150f;
            Rect saveRect = new Rect(sizeValue.xMax + 14f, y, buttonW, 28f);
            if (SimUiStyle.DrawPrimaryButton(saveRect, SimTranslation.T("RSMF.Blueprint.SaveSelection"), Find.CurrentMap != null, GameFont.Tiny))
                StartBlueprintRectSelection();

            Rect refreshRect = new Rect(saveRect.xMax + 8f, y, 96f, 28f);
            if (SimUiStyle.DrawSecondaryButton(refreshRect, SimTranslation.T("RSMF.Blueprint.Refresh"), true, GameFont.Tiny))
                ReloadBlueprintRecords();

            Rect folderRect = new Rect(refreshRect.xMax + 8f, y, 112f, 28f);
            if (SimUiStyle.DrawSecondaryButton(folderRect, SimTranslation.T("RSMF.Blueprint.OpenFolder"), true, GameFont.Tiny))
                OpenBlueprintFolder();

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = CDim;
            Widgets.Label(new Rect(folderRect.xMax + 8f, y, rect.xMax - folderRect.xMax - 18f, 28f),
                SimTranslation.T("RSMF.Blueprint.Count", (blueprintRecords?.Count ?? 0).Named("count")));
            ResetText();
        }

        /// <summary>
        /// 绘制本地蓝图列表。
        /// </summary>
        private void DrawBlueprintList(Rect rect)
        {
            if (blueprintRecords.NullOrEmpty())
            {
                Widgets.NoneLabel(rect.center.y, rect.width, SimTranslation.T("RSMF.Blueprint.Empty"));
                return;
            }

            float rowHeight = 138f;
            float viewWidth = rect.width - 18f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, blueprintRecords.Count * rowHeight);
            Widgets.BeginScrollView(rect, ref blueprintScrollPos, viewRect);

            for (int i = 0; i < blueprintRecords.Count; i++)
            {
                ShopBlueprintLocalRecord record = blueprintRecords[i];
                DrawBlueprintRow(new Rect(0f, i * rowHeight, viewWidth, rowHeight - 6f), record, i);
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制单条本地蓝图记录。
        /// </summary>
        private void DrawBlueprintRow(Rect row, ShopBlueprintLocalRecord record, int index)
        {
            ShopBlueprintData data = record?.Data;
            if (data == null)
                return;

            Widgets.DrawBoxSolid(row, index % 2 == 0 ? CPanelAlt : new Color(0f, 0f, 0f, 0.08f));
            DrawBorder(row, new Color(1f, 1f, 1f, 0.12f));

            Rect previewRect = new Rect(row.x + 8f, row.y + 8f, 112f, 112f);
            DrawBlueprintPreview(previewRect, record.PreviewPath);

            float textX = previewRect.xMax + 12f;
            float textW = row.width - 388f;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(textX, row.y + 8f, textW, 24f), data.label ?? SimTranslation.T("RSMF.Common.UnnamedShop"));

            Text.Font = GameFont.Tiny;
            GUI.color = CDim;
            Widgets.Label(new Rect(textX, row.y + 34f, textW, 20f), SimTranslation.T("RSMF.Blueprint.MetaLine",
                data.width.Named("width"),
                data.height.Named("height"),
                (data.buildings?.Count ?? 0).Named("buildings"),
                (data.zoneCells?.Count ?? 0).Named("cells")));
            Widgets.Label(new Rect(textX, row.y + 54f, textW, 20f), SimTranslation.T("RSMF.Blueprint.CreatedLine",
                FormatBlueprintTime(data.createdAtTicks).Named("time")));
            Widgets.Label(new Rect(textX, row.y + 76f, textW, 24f), BuildBlueprintFeatureLine(data));
            ResetText();

            float btnW = 90f;
            float btnH = 28f;
            float bx = row.xMax - btnW - 10f;
            float by = row.y + 10f;

            if (SimUiStyle.DrawPrimaryButton(new Rect(bx, by, btnW, btnH), SimTranslation.T("RSMF.Blueprint.Place"), Find.CurrentMap != null, GameFont.Tiny))
                StartBlueprintPlacement(record);

            by += btnH + 8f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), SimTranslation.T("RSMF.Blueprint.Edit"), true, GameFont.Tiny))
                OpenBlueprintEditor(record);

            by += btnH + 8f;
            if (SimUiStyle.DrawDangerButton(new Rect(bx, by, btnW, btnH), SimTranslation.T("RSMF.Common.Delete"), true, GameFont.Tiny))
                ConfirmDeleteBlueprint(record);

            Rect leftTopRect = new Rect(bx - btnW - 8f, row.y + 10f, btnW, btnH);
            if (SimUiStyle.DrawSecondaryButton(leftTopRect, SimTranslation.T("RSMF.Blueprint.Open"), true, GameFont.Tiny))
                OpenRecordFolder(record);

            Rect uploadRect = new Rect(bx - btnW - 8f, row.y + 46f, btnW, btnH);
            SteamSessionInfo uploadSession = blueprintSteamSession;
            if (uploadSession == null || string.IsNullOrWhiteSpace(uploadSession.SteamId))
                uploadSession = SteamSessionResolver.TryGetCurrentSession();
            if (uploadSession != null && uploadSession.IsAvailable)
                blueprintSteamSession = uploadSession;
            bool alreadyUploaded = BlueprintOwnershipUtility.IsUploadedByCurrentSteam(data, uploadSession?.SteamId ?? "");
            bool importedFromNetwork = BlueprintOwnershipUtility.IsImportedFromNetwork(data);
            bool canUpload = uploadSession != null && uploadSession.IsAvailable;
            string uploadLabel = alreadyUploaded
                ? SimTranslation.T("RSMF.Blueprint.Network.Update")
                : SimTranslation.T("RSMF.Blueprint.Network.Upload");
            bool canPressUpload = canUpload && (alreadyUploaded || BlueprintOwnershipUtility.CanUploadAsNew(data));
            if (SimUiStyle.DrawSecondaryButton(uploadRect, uploadLabel, canPressUpload, GameFont.Tiny))
                UploadBlueprintRecordToNetwork(record);
            TooltipHandler.TipRegion(uploadRect, canUpload
                ? alreadyUploaded
                    ? SimTranslation.T("RSMF.Blueprint.Network.UpdateTip")
                    : importedFromNetwork
                        ? SimTranslation.T("RSMF.Blueprint.Network.ImportedCannotUpload")
                        : SimTranslation.T("RSMF.Blueprint.Network.UploadTip")
                : alreadyUploaded
                    ? SimTranslation.T("RSMF.Blueprint.Network.UpdateNeedSteam")
                    : SimTranslation.T("RSMF.Blueprint.Network.UploadNeedSteam"));

            Rect sourceRect = new Rect(bx - btnW - 8f, row.y + 82f, btnW, btnH);
            string sourceLabel = importedFromNetwork
                ? SimTranslation.T("RSMF.Blueprint.Network.ImportedTag")
                : alreadyUploaded
                    ? SimTranslation.T("RSMF.Blueprint.Network.UploadedTag")
                    : SimTranslation.T("RSMF.Blueprint.Network.LocalOnly");
            SimUiStyle.DrawDisabledClickableButton(sourceRect, sourceLabel, GameFont.Tiny);
        }

        /// <summary>
        /// 绘制本地保存的蓝图预览图片。
        /// </summary>
        private static void DrawBlueprintPreview(Rect rect, string previewPath)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.35f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.16f));

            Texture2D texture = LoadPreviewTexture(previewPath);
            if (texture != null)
            {
                GUI.DrawTexture(rect.ContractedBy(4f), texture, ScaleMode.ScaleToFit);
                return;
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(rect, SimTranslation.T("RSMF.Blueprint.NoPreview"));
            ResetText();
        }

        /// <summary>
        /// 从磁盘读取预览图纹理。
        /// </summary>
        private static Texture2D LoadPreviewTexture(string previewPath)
        {
            if (string.IsNullOrEmpty(previewPath) || !File.Exists(previewPath))
                return null;

            try
            {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                return texture.LoadImage(File.ReadAllBytes(previewPath)) ? texture : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 进入地图框选保存模式。
        /// </summary>
        private void StartBlueprintRectSelection()
        {
            MainButtonDef reopenTab = def;
            blueprintSelectionActive = true;
            Find.MainTabsRoot.EscapeCurrentTab(playSound: false);
            Find.DesignatorManager.Select(new Designator_SaveShopBlueprintRect(BlueprintMaxSize, () => FinishBlueprintRectSelection(reopenTab)));
            Messages.Message(SimTranslation.T("RSMF.Blueprint.SelectRectHint", BlueprintMaxSize.Named("max")), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// 结束地图框选保存模式，并恢复经营管理窗口显示。
        /// </summary>
        private void FinishBlueprintRectSelection(MainButtonDef reopenTab)
        {
            blueprintSelectionActive = false;
            ReloadBlueprintRecords();
            if (reopenTab != null && Find.MainTabsRoot.OpenTab != reopenTab)
                Find.MainTabsRoot.SetCurrentTab(reopenTab, playSound: false);
        }

        /// <summary>
        /// 进入地图蓝图放置模式。
        /// </summary>
        private void StartBlueprintPlacement(ShopBlueprintLocalRecord record)
        {
            if (record?.Data == null)
                return;

            MainButtonDef reopenTab = def;
            blueprintSelectionActive = true;
            Find.MainTabsRoot.EscapeCurrentTab(playSound: false);
            Find.DesignatorManager.Select(new Designator_PlaceShopBlueprint(record.Data, () => FinishBlueprintPlacement(reopenTab)));
            Messages.Message(SimTranslation.T("RSMF.Blueprint.Place.Hint"), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// 结束地图蓝图放置模式，并恢复经营管理窗口显示。
        /// </summary>
        private void FinishBlueprintPlacement(MainButtonDef reopenTab)
        {
            blueprintSelectionActive = false;
            if (reopenTab != null && Find.MainTabsRoot.OpenTab != reopenTab)
                Find.MainTabsRoot.SetCurrentTab(reopenTab, playSound: false);
        }

        /// <summary>
        /// 打开本地蓝图编辑窗口。
        /// </summary>
        private void OpenBlueprintEditor(ShopBlueprintLocalRecord record)
        {
            Find.WindowStack.Add(new Dialog_EditShopBlueprint(record, ReloadBlueprintRecords));
        }

        /// <summary>
        /// 确保蓝图列表已读取。
        /// </summary>
        private void EnsureBlueprintRecords()
        {
            if (blueprintRecords == null)
                ReloadBlueprintRecords();
        }

        /// <summary>
        /// 从本地磁盘重新读取蓝图列表。
        /// </summary>
        private void ReloadBlueprintRecords()
        {
            blueprintRecords = ShopBlueprintLibrary.LoadRecords();
        }

        /// <summary>
        /// 打开蓝图库根目录。
        /// </summary>
        private static void OpenBlueprintFolder()
        {
            Directory.CreateDirectory(ShopBlueprintLibrary.LibraryDirectory);
            Process.Start(ShopBlueprintLibrary.LibraryDirectory);
        }

        /// <summary>
        /// 打开单个蓝图所在目录。
        /// </summary>
        private static void OpenRecordFolder(ShopBlueprintLocalRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.DirectoryPath))
                return;

            Directory.CreateDirectory(record.DirectoryPath);
            Process.Start(record.DirectoryPath);
        }

        /// <summary>
        /// 弹出确认框并删除本地蓝图。
        /// </summary>
        private void ConfirmDeleteBlueprint(ShopBlueprintLocalRecord record)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(SimTranslation.T("RSMF.Blueprint.DeleteConfirm"), delegate
            {
                if (!ShopBlueprintLibrary.TryDelete(record, out string error))
                {
                    Messages.Message(error ?? SimTranslation.T("RSMF.Blueprint.Error.DeleteFailedUnknown"), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                ReloadBlueprintRecords();
                Messages.Message(SimTranslation.T("RSMF.Blueprint.DeleteSuccess"), MessageTypeDefOf.PositiveEvent, false);
            }));
        }

        /// <summary>
        /// 返回蓝图包含的经营组件摘要。
        /// </summary>
        private static string BuildBlueprintFeatureLine(ShopBlueprintData data)
        {
            int goods = data.buildings?.Count(b => b.goods != null) ?? 0;
            int signs = data.buildings?.Count(b => b.sign != null) ?? 0;
            int services = data.buildings?.Count(b => b.service != null) ?? 0;
            int vending = data.buildings?.Count(b => b.vending != null) ?? 0;
            int floors = data.terrains?.Count(t => !string.IsNullOrEmpty(t.terrainDefName)) ?? 0;
            return SimTranslation.T("RSMF.Blueprint.FeatureLine",
                goods.Named("goods"),
                signs.Named("signs"),
                services.Named("services"),
                vending.Named("vending"),
                floors.Named("floors"));
        }

        /// <summary>
        /// 将蓝图创建时间格式化为本地可读文本。
        /// </summary>
        private static string FormatBlueprintTime(long ticks)
        {
            if (ticks <= 0)
                return SimTranslation.T("RSMF.Common.Unknown");

            return new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
    }
}
