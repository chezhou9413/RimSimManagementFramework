using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager
    {
        private void DrawBottomBar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.2f));
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);
            float btnY = rect.y + (rect.height - 30f) / 2f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextDim;
            Widgets.Label(new Rect(rect.x + 12f, btnY, 420f, 30f), "提示：修改后点击“确认保存”才会生效，“取消”将放弃所有更改。");
            ResetText();

            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 230f, btnY, 100f, 30f), "取消", true, GameFont.Tiny))
            {
                Close();
            }

            Rect saveRect = new Rect(rect.xMax - 120f, btnY, 108f, 30f);
            if (SimUiStyle.DrawPrimaryButton(saveRect, "✔  确认保存", true, GameFont.Tiny))
            {
                HashSet<Building_SimContainer> storages = ShopDataUtility.GetStoragesInZone(shopZone);
                int totalTrimmed = 0;
                int trimmedStorageCount = 0;

                foreach (Building_SimContainer storage in storages)
                {
                    ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
                    if (comp != null && !string.IsNullOrEmpty(comp.ActiveGoodsDefName))
                    {
                        Dictionary<string, GoodsItemData> settingsCopy = draftItemData.ToDictionary(
                            kv => kv.Key,
                            kv => new GoodsItemData
                            {
                                enabled = kv.Value.enabled,
                                count = kv.Value.count,
                                price = kv.Value.price
                            });

                        Dictionary<string, GoodsItemData> clamped = storage.ClampSettingsToCapacity(comp.ActiveGoodsDefName, settingsCopy, out int trimmed);
                        if (trimmed > 0)
                        {
                            totalTrimmed += trimmed;
                            trimmedStorageCount++;
                        }

                        comp.ApplySettings(comp.ActiveGoodsDefName, clamped);
                    }
                }

                if (totalTrimmed > 0)
                {
                    Messages.Message($"已按货柜容量上限自动调整目标量：{trimmedStorageCount} 个货柜共裁剪 {totalTrimmed} 件超额配置。", MessageTypeDefOf.NeutralEvent, false);
                }

                Close();
            }
        }

        private void DrawTableHeader(Rect rect, System.Action drawColumns)
        {
            Widgets.DrawBoxSolid(rect, CHeaderBg);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), CDivider);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextMid;
            drawColumns();
            ResetText();
        }

        private void DrawHdrLabel(Rect rect, string text, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
        }

        private void DrawRowBg(Rect row, int index, bool highlighted)
        {
            if (highlighted) Widgets.DrawBoxSolid(row, CCheckedBg);
            else if (index % 2 == 1) Widgets.DrawBoxSolid(row, CRowAlt);
        }

        private void DrawFieldLabel(Rect rect, string text)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextDim;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
        }

        private void DrawDash(Rect rect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.25f, 0.25f, 0.25f);
            Widgets.Label(rect, "—");
            ResetText();
        }

        private void DrawBorderRect(Rect rect, Color color, float thickness)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private bool MatchSearch(string label)
        {
            return string.IsNullOrEmpty(searchQuery)
                || label.IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
