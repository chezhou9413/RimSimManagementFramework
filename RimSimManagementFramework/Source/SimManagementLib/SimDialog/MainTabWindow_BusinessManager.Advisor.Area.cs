using RimWorld;
using SimManagementLib.GameComp;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //把玩家框选范围作为下一条消息的附件，不生成区域、蓝图或其他游戏指令。
    public partial class MainTabWindow_BusinessManager
    {
        private Map advisorMarkedMap;
        private CellRect advisorMarkedRect;

        //切换到地图框选，完成或取消后恢复顾问页和输入草稿。
        private void StartAdvisorAreaSelection()
        {
            Game game = Current.Game;
            Map map = Find.CurrentMap;
            MainButtonDef tab = def;
            Find.MainTabsRoot.EscapeCurrentTab(playSound: false);
            Find.DesignatorManager.Select(new Designator_MarkAdvisorArea(map, bounds =>
            {
                if (Current.Game != game) return;
                if (bounds.HasValue && Find.Maps.Contains(map))
                {
                    advisorMarkedMap = map;
                    advisorMarkedRect = bounds.Value;
                }
                advisorFocusInput = true;
                if (Find.MainTabsRoot.OpenTab == null) Find.MainTabsRoot.SetCurrentTab(tab, playSound: false);
            }));
        }

        //区块附件可检查地图、坐标和大小，也可在发送前移除。
        private void DrawAdvisorMarkedArea(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(CAccent.r, CAccent.g, CAccent.b, 0.12f));
            string label = AdvisorText("MarkedArea")
                .Replace("{map}", advisorMarkedMap.Parent.Label)
                .Replace("{x}", advisorMarkedRect.minX.ToString()).Replace("{z}", advisorMarkedRect.minZ.ToString())
                .Replace("{maxX}", advisorMarkedRect.maxX.ToString()).Replace("{maxZ}", advisorMarkedRect.maxZ.ToString())
                .Replace("{width}", advisorMarkedRect.Width.ToString()).Replace("{height}", advisorMarkedRect.Height.ToString());
            AdvisorLabel(new Rect(rect.x + 8f, rect.y, rect.width - 116f, rect.height), label, AdvisorText("MarkedAreaHint"));
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 100f, rect.y, 100f, rect.height), AdvisorText("RemoveArea")))
                advisorMarkedMap = null;
        }

        //快捷问题和手动消息都附上固定范围，成功提交后移除附件，避免误用到下一项任务。
        private bool SendAdvisorMessage(GameComponent_BusinessAdvisor advisor, string text)
        {
            if (advisor.Busy || advisor.HasPendingPlan) return false;
            string area = "";
            if (advisorMarkedMap != null)
            {
                if (!Find.Maps.Contains(advisorMarkedMap))
                {
                    Messages.Message(AdvisorText("MarkedAreaUnavailable"), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                area = AdvisorText("MarkedAreaAttachment") + "\nmapId=" + advisorMarkedMap.uniqueID
                    + " x=" + advisorMarkedRect.minX + " z=" + advisorMarkedRect.minZ
                    + " width=" + advisorMarkedRect.Width + " height=" + advisorMarkedRect.Height
                    + " maxX=" + advisorMarkedRect.maxX + " maxZ=" + advisorMarkedRect.maxZ;
            }
            advisor.Send(text, area);
            if (advisor.HasError) return false;
            advisorMarkedMap = null;
            advisorFocusInput = true;
            return true;
        }
    }
}
