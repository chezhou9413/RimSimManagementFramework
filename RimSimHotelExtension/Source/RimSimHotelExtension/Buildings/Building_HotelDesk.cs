using System.Linq;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Spa;
using RimSimHotelExtension.Tool;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using Verse;

namespace RimSimHotelExtension.Buildings
{
    //复用收银员值班和现金储存的旅馆服务台。
    public sealed class Building_HotelDesk : Building_CashRegister
    {
        //只在顾客真正前往或排队办理前台入住、续住、退房或温泉购票时视为有等待顾客。
        public override bool HasWaitingCustomers
        {
            get
            {
                if (base.HasWaitingCustomers) return true;
                if (!Spawned) return false;
                var shop = ShopStaffUtility.FindShopFor(this);
                if (shop == null) return false;
                var sessions = SpaMapComponent.For(Map)?.Sessions;
                if (sessions != null)
                {
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (!session.IsTerminal
                            && session.ticketDesk == this
                            && session.stage == SpaSessionStage.BuyingTicket
                            && session.customer?.CurJobDef?.defName == "RSH_UseSpa")
                            return true;
                    }
                }
                var manager = HotelMapComponent.For(Map);
                if (manager.DeskFor(shop) != this) return false;
                for (int i = 0; i < manager.ActiveStays.Count; i++)
                {
                    var stay = manager.ActiveStays[i];
                    if (stay.completed || stay.shopZoneId != shop.ID) continue;
                    for (int g = 0; g < stay.guests.Count; g++)
                    {
                        Pawn guest = stay.guests[g].pawn;
                        if (guest?.Spawned == true && guest.CurJobDef?.defName == "RSH_Reception"
                            && guest.CurJob?.targetA.Thing == this
                            && guest.Position.DistanceToSquared(Position) <= 900)
                            return true;
                    }
                }
                return false;
            }
        }

        //选中服务台时展示当前旅馆接待状态、空床数、在住人数与房费摘要。
        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            if (!Spawned || Map == null) return text;
            var shop = ShopStaffUtility.FindShopFor(this);
            if (shop == null) return text;
            var manager = HotelMapComponent.For(Map);
            var settings = manager.SettingsFor(shop);
            int totalBeds = settings.registeredBeds.Count(b => b != null && b.Spawned && shop.ContainsCell(b.Position));
            int vacantBeds = manager.AvailableBeds(shop).Count();
            int stayingGuests = manager.ActiveStays.Where(s => !s.completed && s.active && s.shopZoneId == shop.ID).Sum(s => s.guests.Count);
            int heldPrepay = manager.ActiveStays.Where(s => !s.completed && s.shopZoneId == shop.ID).Sum(s => s.escrow);
            if (!text.NullOrEmpty()) text += "\n";
            text += HotelText.T("RSH.Inspect.DeskSummary",
                HotelText.T(settings.enabled ? "RSH.Ui.Open" : "RSH.Ui.Paused").Named("STATE"),
                vacantBeds.Named("VACANT"), totalBeds.Named("TOTAL"), stayingGuests.Named("GUESTS"));
            text += "\n" + HotelText.T("RSH.Inspect.DeskFinance",
                settings.earnedRoomRevenueToday.Named("REVENUE"), heldPrepay.Named("PREPAY"));
            string issue = manager.GetIssue(shop);
            if (!issue.NullOrEmpty()) text += "\n" + issue;
            return text;
        }
    }
}
