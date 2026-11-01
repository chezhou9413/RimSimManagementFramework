using RimWorld;
using RimSimHotelExtension.GameComp;
using SimManagementLib.Api;
using Verse;

namespace RimSimHotelExtension.Buildings
{
    //旅馆专用床仅负责建成时的自动登记，其余行为沿用通用床位逻辑。
    public sealed class Building_HotelBed : Building_Bed
    {
        //新建旅馆床在条件满足时自动登记，读档只恢复已有记录。
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad) return;
            var shop = SimShopServiceApi.FindShop(map, Position);
            if (shop != null) HotelMapComponent.For(map).RegisterBed(shop, this, true, out _);
        }

    }
}
