using HarmonyLib;
using SimManagementLib.SimZone;
using Verse;

namespace SimManagementLib.Patch
{
    [HarmonyPatch(typeof(ZoneManager), "Notify_NoZoneOverlapThingSpawned")]
    public static class Patch_ZoneManager_Notify_NoZoneOverlapThingSpawned
    {
        public static bool Prefix(ZoneManager __instance, Thing thing)
        {
            if (__instance == null || thing == null) return true;

            CellRect cellRect = thing.OccupiedRect();
            bool overlapsShopZone = false;
            for (int z = cellRect.minZ; z <= cellRect.maxZ && !overlapsShopZone; z++)
            {
                for (int x = cellRect.minX; x <= cellRect.maxX; x++)
                {
                    Zone zone = __instance.ZoneAt(new IntVec3(x, 0, z));
                    if (zone is Zone_Shop)
                    {
                        overlapsShopZone = true;
                        break;
                    }
                }
            }

            // 不涉及商店区时，交回原版处理，降低兼容风险。
            if (!overlapsShopZone) return true;

            // 涉及商店区时：保留商店区，移除其他区划。
            for (int z = cellRect.minZ; z <= cellRect.maxZ; z++)
            {
                for (int x = cellRect.minX; x <= cellRect.maxX; x++)
                {
                    IntVec3 c = new IntVec3(x, 0, z);
                    Zone zone = __instance.ZoneAt(c);
                    if (zone == null) continue;
                    if (zone is Zone_Shop) continue;

                    zone.RemoveCell(c);
                    zone.CheckContiguous();
                }
            }

            // 已手动完成处理，跳过原版。
            return false;
        }
    }
}
