using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //在原版用途菜单中提供即时出租登记，左右键使用同一组操作。
    public sealed class Command_HotelBedUse : Command_SetBedOwnerType
    {
        private readonly Building_Bed bed;

        //沿用原版图标，登记后直接在用途按钮上显示旅客专属。
        public Command_HotelBedUse(Building_Bed bed) : base(bed)
        {
            this.bed = bed;
            if (HotelMapComponent.For(bed.Map).IsRegisteredBed(bed))
                defaultLabel = HotelText.T("RSH.Bed.ForGuests");
            else if (!HasHumanOwnerTypes)
                defaultLabel = HotelText.T("RSH.Bed.NormalUse");
            defaultDesc = HotelText.T("RSH.Bed.UseDescription");
        }

        private bool HasHumanOwnerTypes => bed.def.building.bed_humanlike && !bed.ForHumanBabies;
        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions => Options();

        //左键与原版用途按钮一样打开浮动菜单。
        public override void ProcessInput(Event ev) => Find.WindowStack.Add(new FloatMenu(Options().ToList()));

        //保留原版人类床用途，其他床也能在普通用途和旅客专属之间切换。
        private IEnumerable<FloatMenuOption> Options()
        {
            yield return new FloatMenuOption(HasHumanOwnerTypes ? "CommandBedSetForColonistsLabel".Translate().ToString()
                : HotelText.T("RSH.Bed.NormalUse"), () => bed.SetBedOwnerTypeByInterface(BedOwnerType.Colonist));
            if (HasHumanOwnerTypes)
            {
                yield return new FloatMenuOption("CommandBedSetForPrisonersLabel".Translate(), () =>
                {
                    if (!Building_Bed.RoomCanBePrisonCell(bed.GetRoom()) && !bed.ForPrisoners)
                        Messages.Message("CommandBedSetForPrisonersFailOutdoors".Translate(), bed, MessageTypeDefOf.RejectInput, false);
                    else bed.SetBedOwnerTypeByInterface(BedOwnerType.Prisoner);
                });
                if (ModsConfig.IdeologyActive)
                    yield return new FloatMenuOption("CommandBedSetForSlavesLabel".Translate(),
                        () => bed.SetBedOwnerTypeByInterface(BedOwnerType.Slave));
            }
            yield return new FloatMenuOption(HotelText.T("RSH.Bed.ForGuests"), RegisterSelectedBeds);
        }

        //批量登记先检查全部床，任意一张不合格时整次操作都不改变状态。
        private void RegisterSelectedBeds()
        {
            var beds = Find.Selector.SelectedObjects.OfType<Building_Bed>().ToList();
            if (!beds.Contains(bed)) beds.Add(bed);
            foreach (var selected in beds)
            {
                if (!selected.Spawned)
                {
                    Messages.Message(HotelText.T("RSH.Bed.Unavailable"), selected, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                var manager = HotelMapComponent.For(selected.Map);
                var shop = SimShopServiceApi.FindShop(selected.Map, selected.Position);
                string issue = manager.BedRegistrationIssue(shop, selected, true);
                if (issue.NullOrEmpty()) continue;
                Messages.Message(HotelText.T("RSH.Bed.CannotRegister", selected.LabelCap.Named("BED"), issue.Named("REASON")),
                    selected, MessageTypeDefOf.RejectInput, false);
                return;
            }
            foreach (var selected in beds)
            {
                var shop = SimShopServiceApi.FindShop(selected.Map, selected.Position);
                HotelMapComponent.For(selected.Map).RegisterBed(shop, selected, true, out _);
            }
        }
    }
}
