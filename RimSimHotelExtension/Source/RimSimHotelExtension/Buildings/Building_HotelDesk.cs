using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Spa;
using System.Linq;

namespace RimSimHotelExtension.Buildings
{
    //复用收银员值班和现金储存的旅馆服务台。
    public sealed class Building_HotelDesk : Building_CashRegister
    {
        //住宿接待没有普通购物账单，也必须让入住、续住、退房和购票请求触发补位。
        public override bool HasWaitingCustomers
        {
            get
            {
                if (base.HasWaitingCustomers) return true;
                if (!Spawned) return false;
                var shop = ShopStaffUtility.FindShopFor(this);
                if (shop == null) return false;
                if (SpaMapComponent.For(Map).ActiveSessions.Any(s => s.ticketDesk == this
                    && s.stage == SpaSessionStage.BuyingTicket)) return true;
                var manager = HotelMapComponent.For(Map);
                return manager.DeskFor(shop) == this && manager.Stays.Any(s => !s.completed
                    && s.shopZoneId == shop.ID && (!s.active || s.closing || s.renewalRequested));
            }
        }
    }
}
