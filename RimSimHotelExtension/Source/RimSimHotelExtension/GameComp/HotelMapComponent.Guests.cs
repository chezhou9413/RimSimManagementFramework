using System.Collections.Generic;
using RimSimHotelExtension.Models;
using Verse;

namespace RimSimHotelExtension.GameComp
{
    //按住客维护当前合同与个人状态，避免生活调度反复扫描全部住宿历史。
    public sealed partial class HotelMapComponent
    {
        private readonly Dictionary<Pawn, HotelStay> staysByPawn = new Dictionary<Pawn, HotelStay>();
        private readonly Dictionary<Pawn, HotelGuestState> guestsByPawn = new Dictionary<Pawn, HotelGuestState>();

        //按住客直接取得尚未结束的合同。
        public HotelStay FindStay(Pawn pawn) => pawn != null && staysByPawn.TryGetValue(pawn, out var stay)
            && !stay.completed ? stay : null;

        //复用合同索引查询个人余额与生活进度。
        public HotelGuestState GuestFor(Pawn pawn) => FindStay(pawn) != null ? guestsByPawn[pawn] : null;

        //合同建立后立即登记，使后续到客回调可以读取钱包和床位。
        private void IndexStay(HotelStay stay)
        {
            roomPrivacyDirty = true;
            activeStays.Add(stay);
            foreach (var guest in stay.guests)
            {
                if (guest.pawn == null) continue;
                staysByPawn[guest.pawn] = stay;
                guestsByPawn[guest.pawn] = guest;
            }
        }

        //完成退款和离店回调后释放住客引用。
        private void RemoveStayFromIndex(HotelStay stay)
        {
            roomPrivacyDirty = true;
            foreach (var guest in stay.guests)
            {
                if (guest.pawn == null) continue;
                staysByPawn.Remove(guest.pawn);
                guestsByPawn.Remove(guest.pawn);
            }
        }

        //读档只为未结束的合同重建运行索引。
        private void RebuildGuestIndex()
        {
            roomPrivacyDirty = true;
            staysByPawn.Clear();
            guestsByPawn.Clear();
            activeStays.Clear();
            foreach (var stay in stays)
                if (!stay.completed) IndexStay(stay);
        }
    }
}
