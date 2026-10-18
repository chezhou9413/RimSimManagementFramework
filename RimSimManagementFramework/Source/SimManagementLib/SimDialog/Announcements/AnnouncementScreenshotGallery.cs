using SimManagementLib.Api;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //绘制公告截图画廊，职责是从本地资源加载图片并通过缩略图切换大图。
    internal sealed class AnnouncementScreenshotGallery
    {
        private readonly List<Texture2D> screenshots = new List<Texture2D>();
        private int selected;

        //加载公告的本地截图，职责是复用游戏纹理缓存并让缺失资源输出原版错误。
        public AnnouncementScreenshotGallery(List<string> paths)
        {
            foreach (string path in paths)
                screenshots.Add(ContentFinder<Texture2D>.Get(path));
        }

        //计算画廊高度，职责是给大图、缩略图和截图序号预留独立空间。
        public float Height(float width)
        {
            return screenshots.Count == 0 ? 0f : PreviewHeight(width) + 84f + ShopUiVisualUtility.LineHeight(GameFont.Small);
        }

        //计算大图高度，职责是让不同屏幕宽度下的截图保持合理可读面积。
        private static float PreviewHeight(float width)
        {
            return Mathf.Clamp(width * 0.5625f, 200f, 400f);
        }

        //绘制画廊及缩略图，职责是按原始比例显示截图并突出当前选择。
        public void Draw(Rect rect)
        {
            if (screenshots.Count == 0) return;
            float line = ShopUiVisualUtility.LineHeight(GameFont.Small);
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x, rect.y, rect.width, line),
                SimTranslation.T("RSMF.Announcement.Screenshots", (selected + 1).Named("index"), screenshots.Count.Named("count")),
                ShopUiVisualUtility.MutedText);
            Rect preview = new Rect(rect.x, rect.y + line + 8f, rect.width, PreviewHeight(rect.width));
            ShopUiVisualUtility.DrawSection(preview);
            Widgets.DrawTextureFitted(preview.ContractedBy(4f), screenshots[selected], 1f);

            //缩略图只承担切图操作，不向游戏或 Steam 发起图片请求。
            float width = Mathf.Min(112f, (rect.width - 8f * (screenshots.Count - 1)) / screenshots.Count);
            for (int i = 0; i < screenshots.Count; i++)
            {
                Rect thumbnail = new Rect(rect.x + i * (width + 8f), preview.yMax + 8f, width, 60f);
                ShopUiVisualUtility.DrawSection(thumbnail);
                Widgets.DrawTextureFitted(thumbnail.ContractedBy(3f), screenshots[i], 1f);
                ShopUiVisualUtility.DrawBorder(thumbnail, i == selected ? ShopUiVisualUtility.Accent : ShopUiVisualUtility.Divider);
                if (Widgets.ButtonInvisible(thumbnail)) selected = i;
            }
        }
    }
}
