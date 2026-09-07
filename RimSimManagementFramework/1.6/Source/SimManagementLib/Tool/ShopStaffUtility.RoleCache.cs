using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 管理商店岗位可见性缓存，负责减少 WorkGiver 权限判断中的重复区域扫描。
    /// </summary>
    public static partial class ShopStaffUtility
    {
        private const int VisibleRoleCacheTicks = 97;
        private static readonly Dictionary<int, VisibleRoleCache> visibleRoleCaches = new Dictionary<int, VisibleRoleCache>();
        private static readonly List<ShopStaffRoleDef> EmptyRoleList = new List<ShopStaffRoleDef>(0);

        /// <summary>
        /// 通知指定商店配置发生变化，负责让岗位可见性缓存尽快重新计算。
        /// </summary>
        public static void NotifyShopChanged(Zone_Shop zone)
        {
            if (zone == null) return;
            visibleRoleCaches.Remove(GetZoneCacheKey(zone));
        }

        /// <summary>
        /// 返回缓存后的可见岗位列表，负责让 WorkGiver 权限判断避免每个候选都扫描商店格子。
        /// </summary>
        private static List<ShopStaffRoleDef> GetCachedVisibleRoles(Zone_Shop zone)
        {
            if (zone?.Map == null) return EmptyRoleList;

            int now = Find.TickManager?.TicksGame ?? 0;
            int key = GetZoneCacheKey(zone);
            if (visibleRoleCaches.TryGetValue(key, out VisibleRoleCache cache)
                && cache != null
                && cache.Matches(zone)
                && now < cache.expireTick)
            {
                return cache.visibleRoles;
            }

            if (cache == null)
            {
                cache = new VisibleRoleCache();
                visibleRoleCaches[key] = cache;
            }

            cache.mapId = zone.Map.uniqueID;
            cache.zoneId = zone.ID;
            cache.cellCount = zone.CellCount;
            cache.expireTick = now + VisibleRoleCacheTicks;
            cache.visibleRoles.Clear();

            IReadOnlyList<ShopStaffRoleDef> roles = Roles;
            for (int i = 0; i < roles.Count; i++)
            {
                ShopStaffRoleDef role = roles[i];
                if (RoleMatchesShop(zone, role))
                    cache.visibleRoles.Add(role);
            }

            cache.visibleRoles.Sort(CompareRoleOrder);
            return cache.visibleRoles;
        }

        /// <summary>
        /// 判断岗位是否绑定指定 WorkGiver，负责避免在高频路径中创建 LINQ 中间列表。
        /// </summary>
        private static bool RoleContainsWorkGiver(ShopStaffRoleDef role, WorkGiverDef workGiverDef)
        {
            if (role?.workGivers.NullOrEmpty() != false || workGiverDef == null)
                return false;

            for (int i = 0; i < role.workGivers.Count; i++)
            {
                if (role.workGivers[i] == workGiverDef)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 比较岗位显示顺序，负责保持岗位配置中的排序语义。
        /// </summary>
        private static int CompareRoleOrder(ShopStaffRoleDef left, ShopStaffRoleDef right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            int indexCompare = left.index.CompareTo(right.index);
            if (indexCompare != 0) return indexCompare;
            return string.CompareOrdinal(left.defName, right.defName);
        }

        /// <summary>
        /// 生成商店缓存键，负责按地图和区划区分不同商店。
        /// </summary>
        private static int GetZoneCacheKey(Zone_Shop zone)
        {
            unchecked
            {
                int zoneId = zone.ID >= 0 ? zone.ID : zone.GetHashCode();
                return zone.Map.uniqueID * 397 ^ zoneId;
            }
        }

        /// <summary>
        /// 保存单个商店的岗位可见性缓存，负责短时间复用需要扫格子的岗位匹配结果。
        /// </summary>
        private class VisibleRoleCache
        {
            public int mapId = -1;
            public int zoneId = -1;
            public int cellCount = -1;
            public int expireTick = -1;
            public readonly List<ShopStaffRoleDef> visibleRoles = new List<ShopStaffRoleDef>();

            /// <summary>
            /// 判断缓存是否仍对应当前商店，负责在搬迁或区划变化后自动放弃旧结果。
            /// </summary>
            public bool Matches(Zone_Shop zone)
            {
                return zone?.Map != null
                    && mapId == zone.Map.uniqueID
                    && zoneId == zone.ID
                    && cellCount == zone.CellCount;
            }
        }
    }
}
