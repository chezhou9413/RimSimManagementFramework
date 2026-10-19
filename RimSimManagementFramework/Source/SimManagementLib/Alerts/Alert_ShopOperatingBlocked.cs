using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace SimManagementLib.Alerts
{
    //按单项经营问题显示原版警报，悬停高亮并循环定位受影响商店。
    public abstract class Alert_ShopOperatingBlocked : Alert
    {
        private readonly List<ShopOperatingIssue> issues = new List<ShopOperatingIssue>();
        private int jumpIndex = -1;
        protected abstract string IssueCategory { get; }

        //使用普通警报级别，避免经营问题触发危急警报音效。
        protected Alert_ShopOperatingBlocked()
        {
            defaultPriority = AlertPriority.Medium;
        }

        //直接显示问题名称，多家店存在同一问题时补充店铺数量。
        public override string GetLabel()
        {
            string label = SimTranslation.T("RSMF.Alert.Shop.Label." + IssueCategory);
            int count = issues.Select(issue => issue.Shop).Distinct().Count();
            return count > 1 ? SimTranslation.T("RSMF.Alert.Shop.LabelCount", label.Named("label"), count.Named("count")) : label;
        }

        //返回有效定位目标，交给原版警报绘制悬停高亮。
        public override AlertReport GetReport()
        {
            Refresh();
            return AlertReport.CulpritsAre(issues.Select(issue => issue.Target).Distinct().ToList());
        }

        //显示店名和原因，限制长列表高度并保留所有问题的循环定位。
        public override TaggedString GetExplanation()
        {
            StringBuilder text = new StringBuilder(SimTranslation.T("RSMF.Alert.Shop.Explanation"));
            foreach (ShopOperatingIssue issue in issues.Take(6))
            {
                text.Append("\n\n").Append(SimTranslation.T("RSMF.Alert.Shop.Entry",
                    issue.Shop.label.Named("shop"), issue.Reason.Named("reason")));
            }
            if (issues.Count > 6)
                text.Append("\n\n").Append(SimTranslation.T("RSMF.Alert.Shop.More", (issues.Count - 6).Named("count")));
            return text.ToString();
        }

        public override string GetJumpToTargetsText => SimTranslation.T("RSMF.Alert.Shop.Jump");

        //按说明顺序循环跳转，设施直接选中，格子问题同时选中其商店区域。
        protected override void OnClick()
        {
            Refresh(force: true);
            if (issues.Count == 0) return;
            jumpIndex = GenMath.PositiveMod(jumpIndex + (Event.current.button == 1 ? -1 : 1), issues.Count);
            ShopOperatingIssue issue = issues[jumpIndex];
            CameraJumper.TryJumpAndSelect(issue.Target);
            if (!issue.Target.HasThing)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(issue.Shop);
            }
        }

        //从共享诊断缓存筛选本条警报，避免每种问题分别扫描全部店铺。
        private void Refresh(bool force = false)
        {
            issues.Clear();
            if (Current.Game != null)
                issues.AddRange(Current.Game.GetComponent<GameComponent_ShopOperatingAlerts>().GetIssues(force)
                    .Where(issue => issue.Category == IssueCategory));
            if (issues.Count == 0) jumpIndex = -1;
        }
    }
}
