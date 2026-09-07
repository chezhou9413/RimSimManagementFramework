using RimWorld;
using RimWorld.Planet;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.GameComp
{
    /// <summary>
    /// 负责在重力舰搬迁前后保存和恢复随船移动的商店区域。
    /// </summary>
    public class GameComponent_ShopZoneTransportManager : GameComponent
    {
        private Dictionary<string, List<MoveableShopZone>> zonesByGravshipId = new Dictionary<string, List<MoveableShopZone>>();

        /// <summary>
        /// 初始化商店区域搬迁管理组件。
        /// </summary>
        public GameComponent_ShopZoneTransportManager(Game game)
        {
        }

        /// <summary>
        /// 保存商店区域搬迁数据，确保跨存档读写后仍能恢复随船区域。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            List<string> keys = null;
            List<List<MoveableShopZone>> values = null;
            Scribe_Collections.Look(ref zonesByGravshipId, "zonesByGravshipId", LookMode.Value, LookMode.Deep, ref keys, ref values);
            if (zonesByGravshipId == null) zonesByGravshipId = new Dictionary<string, List<MoveableShopZone>>();
        }

        /// <summary>
        /// 捕获位于重力舰地板上的商店区域格子，并从旧地图移除已搬迁区域。
        /// </summary>
        public void CaptureShopZones(Gravship gravship, Map oldMap, IntVec3 origin, HashSet<IntVec3> engineFloors)
        {
            if (gravship == null || oldMap?.zoneManager == null || engineFloors == null || engineFloors.Count == 0) return;

            string gravshipId = GetStableGravshipKey(gravship);
            if (string.IsNullOrEmpty(gravshipId)) return;

            List<MoveableShopZone> movedZones = new List<MoveableShopZone>();
            for (int i = oldMap.zoneManager.AllZones.Count - 1; i >= 0; i--)
            {
                if (!(oldMap.zoneManager.AllZones[i] is Zone_Shop zone)) continue;

                MoveableShopZone moveableZone = null;
                foreach (IntVec3 cell in zone.Cells)
                {
                    if (!engineFloors.Contains(cell)) continue;

                    if (moveableZone == null)
                    {
                        moveableZone = zone.CreateMoveableZoneSnapshot();
                        moveableZone.cells.Clear();
                    }

                    moveableZone.cells.Add(cell - origin);
                }

                if (moveableZone == null) continue;

                movedZones.Add(moveableZone);
                zone.Delete();
            }

            if (movedZones.Count <= 0)
            {
                zonesByGravshipId.Remove(gravshipId);
                return;
            }

            zonesByGravshipId[gravshipId] = movedZones;
        }

        /// <summary>
        /// 在目标地图按重力舰旋转和落点恢复此前捕获的商店区域格子。
        /// </summary>
        public void RestoreShopZones(Gravship gravship, Map map, IntVec3 root)
        {
            if (gravship == null || map?.zoneManager == null) return;

            string gravshipId = GetStableGravshipKey(gravship);
            if (string.IsNullOrEmpty(gravshipId)) return;
            if (!TryGetMovedZones(gravship, gravshipId, out List<MoveableShopZone> movedZones, out string matchedKey) || movedZones.NullOrEmpty()) return;

            foreach (MoveableShopZone movedZone in movedZones)
            {
                if (movedZone == null || movedZone.cells.NullOrEmpty()) continue;

                Zone_Shop zone = new Zone_Shop(map.zoneManager)
                {
                    ID = movedZone.zoneId
                };
                zone.ApplyMoveableZoneSnapshot(movedZone);
                map.zoneManager.RegisterZone(zone);

                foreach (IntVec3 relativeCell in movedZone.cells)
                {
                    IntVec3 destinationCell = root + PrefabUtility.GetAdjustedLocalPosition(relativeCell, gravship.Rotation);
                    if (!destinationCell.InBounds(map)) continue;
                    if (map.zoneManager.ZoneAt(destinationCell) != null) continue;
                    zone.AddCell(destinationCell);
                }

                if (zone.Cells.NullOrEmpty())
                    zone.Deregister();
            }

            zonesByGravshipId.Remove(matchedKey);
        }

        /// <summary>
        /// 生成重力舰搬迁缓存键，负责避开构造期 WorldObject 尚未分配正式 ID 的问题。
        /// </summary>
        private static string GetStableGravshipKey(Gravship gravship)
        {
            if (gravship?.Engine != null)
                return "GravEngine_" + gravship.Engine.thingIDNumber;

            return gravship?.GetUniqueLoadID();
        }

        /// <summary>
        /// 读取重力舰商店区搬迁快照，负责兼容旧逻辑使用 WorldObject ID 保存的缓存。
        /// </summary>
        private bool TryGetMovedZones(Gravship gravship, string gravshipId, out List<MoveableShopZone> movedZones, out string matchedKey)
        {
            if (zonesByGravshipId.TryGetValue(gravshipId, out movedZones))
            {
                matchedKey = gravshipId;
                return true;
            }

            string loadId = gravship?.GetUniqueLoadID();
            if (!string.IsNullOrEmpty(loadId) && zonesByGravshipId.TryGetValue(loadId, out movedZones))
            {
                matchedKey = loadId;
                return true;
            }

            if (zonesByGravshipId.TryGetValue("WorldObject_-1", out movedZones))
            {
                matchedKey = "WorldObject_-1";
                return true;
            }

            movedZones = null;
            matchedKey = null;
            return false;
        }
    }
}
