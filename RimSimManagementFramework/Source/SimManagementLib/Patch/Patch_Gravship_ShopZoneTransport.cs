using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using SimManagementLib.GameComp;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Patch
{
    [HarmonyPatch(typeof(Gravship), "TransferZones")]
    public static class Patch_Gravship_TransferZones_ShopZone
    {
        public static void Postfix(Gravship __instance, Map oldMap, IntVec3 origin, HashSet<IntVec3> engineFloors)
        {
            Current.Game?.GetComponent<GameComponent_ShopZoneTransportManager>()
                ?.CaptureShopZones(__instance, oldMap, origin, engineFloors);
        }
    }

    [HarmonyPatch(typeof(GravshipPlacementUtility), "CopyZonesIntoMap")]
    public static class Patch_GravshipPlacementUtility_CopyZonesIntoMap_ShopZone
    {
        public static void Postfix(Gravship gravship, Map map, IntVec3 root)
        {
            Current.Game?.GetComponent<GameComponent_ShopZoneTransportManager>()
                ?.RestoreShopZones(gravship, map, root);
        }
    }
}
