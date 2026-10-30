using System.Collections.Generic;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using Verse;

namespace RimSimHotelExtension.Buildings
{
    //保留原版睡眠、材质和品质逻辑，同时将床位出租给旅馆顾客。
    public sealed class Building_HotelBed : Building_Bed
    {
        //放在商店内的新床自动登记，位于客房内的床可通过经营面板登记。
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad) return;
            var shop = SimShopServiceApi.FindShop(map, Position);
            if (shop != null) HotelMapComponent.For(map).RegisterBed(shop, this, true);
        }

        //入住中的床不提供殖民者强行占床菜单。
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn)
        {
            var manager = HotelMapComponent.For(Map);
            if (manager != null)
                foreach (var stay in manager.Stays)
                    if (!stay.completed && stay.bed == this)
                    {
                        yield return new FloatMenuOption(HotelText.T("RSH.Text.ThisBedIsRentedToAGuest"), null);
                        yield break;
                    }
            foreach (var option in base.GetFloatMenuOptions(pawn)) yield return option;
        }
    }
}
