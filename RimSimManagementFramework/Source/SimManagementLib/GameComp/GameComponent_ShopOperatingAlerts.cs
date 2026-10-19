using SimManagementLib.Alerts;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.GameComp
{
    //让各项经营警报共享一次诊断与延迟计时，避免按警报数量重复扫描。
    public sealed class GameComponent_ShopOperatingAlerts : GameComponent
    {
        private const int RefreshTicks = 120;
        private const int GraceTicks = 300;
        private readonly List<ShopOperatingIssue> issues = new List<ShopOperatingIssue>();
        private readonly Dictionary<(Zone_Shop shop, string key), int> firstSeen = new Dictionary<(Zone_Shop, string), int>();
        private LoadedLanguage language;
        private int lastTick = -1;
        private int nextRefreshTick;
        private float nextRefreshTime;

        //每局游戏独立保存诊断快照，不写入存档。
        public GameComponent_ShopOperatingAlerts(Game game)
        {
        }

        //读取短周期快照，暂停时也刷新；缺员等运行问题持续一段时间才显示。
        internal IReadOnlyList<ShopOperatingIssue> GetIssues(bool force = false)
        {
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null
                || !SimManagementLibMod.Settings.showShopOperatingAlerts)
            {
                Clear();
                return issues;
            }
            int now = Find.TickManager.TicksGame;
            if (language != LanguageDatabase.activeLanguage || now < lastTick)
                Clear();
            language = LanguageDatabase.activeLanguage;
            lastTick = now;
            if (!force && now < nextRefreshTick && Time.realtimeSinceStartup < nextRefreshTime) return issues;
            nextRefreshTick = now + RefreshTicks;
            nextRefreshTime = Time.realtimeSinceStartup + 2f;

            var candidates = new List<ShopOperatingIssue>();
            foreach (Map map in Find.Maps)
                foreach (Zone_Shop shop in map.zoneManager.AllZones.OfType<Zone_Shop>())
                    ShopOperatingIssueUtility.Collect(shop, candidates);

            issues.Clear();
            var activeKeys = new HashSet<(Zone_Shop, string)>();
            foreach (ShopOperatingIssue issue in candidates)
            {
                if (!issue.Target.IsValid || issue.Target.ThingDestroyed) continue;
                var key = (issue.Shop, issue.Key);
                activeKeys.Add(key);
                if (!firstSeen.TryGetValue(key, out int since))
                    firstSeen[key] = since = now;
                if (!issue.Delay || now - since >= GraceTicks)
                    issues.Add(issue);
            }
            foreach (var key in firstSeen.Keys.Where(key => !activeKeys.Contains(key)).ToList())
                firstSeen.Remove(key);
            return issues;
        }

        //释放旧游戏引用与延迟记录，关闭显示或切换存档后重新开始检查。
        private void Clear()
        {
            issues.Clear();
            firstSeen.Clear();
            language = null;
            lastTick = -1;
            nextRefreshTick = 0;
            nextRefreshTime = 0f;
        }
    }
}
