using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //用大缩略图浏览招牌图库，支持搜索名称和直接导入本地图片。
    public sealed class Dialog_SignImageBrowser : Window
    {
        private readonly Action<string> selectAction;
        private List<SignImageRecord> images;
        private string search = "";
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(940f, UI.screenWidth - 48f), Mathf.Min(720f, UI.screenHeight - 48f));

        //读取图库快照，选择图片后交给调用方处理。
        public Dialog_SignImageBrowser(Action<string> selectAction)
        {
            this.selectAction = selectAction;
            ReloadImages();
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnAccept = false;
        }

        //按窗口宽度调整卡片列数，标题、搜索和导入按钮不进入滚动区。
        public override void DoWindowContents(Rect rect)
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            Color color = GUI.color;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                float y = ShopUiVisualUtility.DrawPageHeading(rect, SimTranslation.T("RSMF.CustomSign.SelectGalleryImage"),
                    SimTranslation.T("RSMF.CustomSign.GalleryHelp"), true);
                float h = Mathf.Max(32f, Text.LineHeightOf(GameFont.Small) + 10f);
                float refreshWidth = Mathf.Max(110f, Text.CalcSize(SimTranslation.T("RSMF.CustomSign.RefreshGallery")).x + 24f);
                string next = ShopUiVisualUtility.DrawSearchField(new Rect(rect.x, y, rect.width - refreshWidth - 8f, h),
                    search, SimTranslation.T("RSMF.CustomSign.SearchImagePlaceholder"));
                if (next != search) { search = next; scroll = Vector2.zero; }
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - refreshWidth, y, refreshWidth, h), SimTranslation.T("RSMF.CustomSign.RefreshGallery")))
                    ReloadImages();
                y += h + 10f;
                Rect outer = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - h - y - 12f));
                DrawCards(outer, h);
                float half = (rect.width - 8f) / 2f;
                if (SimUiStyle.DrawPrimaryButton(new Rect(rect.x, rect.yMax - h, half, h), SimTranslation.T("RSMF.CustomSign.ImportPath")))
                    Find.WindowStack.Add(new Dialog_SignImagePathImport(record => Choose(record.imageId)));
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - half, rect.yMax - h, half, h), SimTranslation.T("RSMF.Common.Cancel"))) Close();
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
        }

        //只绘制可见卡片，避免图库较大时逐帧加载所有图片。
        private void DrawCards(Rect rect, float h)
        {
            var filtered = images.Where(r => search.NullOrEmpty() || (r.label ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || r.imageId.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (filtered.Count == 0)
            {
                Widgets.Label(rect.ContractedBy(12f), SimTranslation.T("RSMF.CustomSign.EmptyGallery"));
                return;
            }
            float viewWidth = rect.width - 16f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((viewWidth + 8f) / 184f));
            float width = (viewWidth - (columns - 1) * 8f) / columns;
            float line = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float height = 136f + line * 2f + h + 20f;
            float stride = height + 8f;
            int rows = Mathf.CeilToInt(filtered.Count / (float)columns);
            Rect view = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, rows * stride));
            Widgets.BeginScrollView(rect, ref scroll, view);
            try
            {
                int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / stride) * columns);
                int last = Mathf.Min(filtered.Count, Mathf.CeilToInt((scroll.y + rect.height) / stride) * columns);
                for (int i = first; i < last; i++)
                    DrawCard(new Rect((i % columns) * (width + 8f), (i / columns) * stride, width, height), filtered[i], h, line);
            }
            finally { Widgets.EndScrollView(); }
        }

        //卡片提供足够大的图片预览，缺失图片显示明确状态且不可选。
        private void DrawCard(Rect rect, SignImageRecord record, float h, float line)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.17f, 0.20f));
            Rect image = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 120f);
            Widgets.DrawBoxSolid(image, new Color(0.25f, 0.27f, 0.30f));
            GUI.DrawTexture(image, SignTextureCache.GetTexture(record.imageId), ScaleMode.ScaleToFit, true);
            bool exists = SignImageLibrary.ImageExists(record.imageId);
            float y = image.yMax + 6f;
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + 8f, y, rect.width - 16f, line), (record.label ?? record.imageId).Truncate(rect.width - 16f));
            y += line + 2f;
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + 8f, y, rect.width - 16f, line),
                exists ? record.width + " × " + record.height : SimTranslation.T("RSMF.CustomSign.ImageMissing"), Color.gray);
            TooltipHandler.TipRegion(rect, record.label ?? record.imageId);
            if (SimUiStyle.DrawPrimaryButton(new Rect(rect.x + 8f, rect.yMax - h - 8f, rect.width - 16f, h), SimTranslation.T("RSMF.CustomSign.Select"), exists))
                Choose(record.imageId);
        }

        //完成选择并关闭图库。
        private void Choose(string imageId)
        {
            selectAction(imageId);
            Close();
        }

        //刷新当前索引快照并回到列表起点。
        private void ReloadImages()
        {
            images = SignImageLibrary.Load().images.OrderBy(r => r.label).ThenBy(r => r.imageId).ToList();
            scroll = Vector2.zero;
        }
    }
}
