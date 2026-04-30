using RimWorld;
using RimWorld.Planet;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.GameComp
{
    public class GameComponent_ShopZoneTransportManager : GameComponent
    {
        private Dictionary<string, List<MoveableShopZone>> zonesByGravshipId = new Dictionary<string, List<MoveableShopZone>>();

        public GameComponent_ShopZoneTransportManager(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();

            List<string> keys = null;
            List<List<MoveableShopZone>> values = null;
            Scribe_Collections.Look(ref zonesByGravshipId, "zonesByGravshipId", LookMode.Value, LookMode.Deep, ref keys, ref values);
            if (zonesByGravshipId == null) zonesByGravshipId = new Dictionary<string, List<MoveableShopZone>>();
        }

        public void CaptureShopZones(Gravship gravship, Map oldMap, IntVec3 origin, HashSet<IntVec3> engineFloors)
        {
            if (gravship == null || oldMap?.zoneManager == null || engineFloors == null || engineFloors.Count == 0) return;

            string gravshipId = gravship.GetUniqueLoadID();
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

        public void RestoreShopZones(Gravship gravship, Map map, IntVec3 root)
        {
            if (gravship == null || map?.zoneManager == null) return;

            string gravshipId = gravship.GetUniqueLoadID();
            if (string.IsNullOrEmpty(gravshipId)) return;
            if (!zonesByGravshipId.TryGetValue(gravshipId, out List<MoveableShopZone> movedZones) || movedZones.NullOrEmpty()) return;

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
                    if (!CanOverlapAllThingsAt(map, destinationCell)) continue;
                    zone.AddCell(destinationCell);
                }

                if (zone.Cells.NullOrEmpty())
                    zone.Deregister();
            }

            zonesByGravshipId.Remove(gravshipId);
        }

        private static bool CanOverlapAllThingsAt(Map map, IntVec3 cell)
        {
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (!things[i].def.CanOverlapZones)
                    return false;
            }

            return true;
        }
    }
}
