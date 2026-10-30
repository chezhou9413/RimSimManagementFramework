using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //在生成前一次预留同行人数，保留每人独立访问和完整失败清理。
    public partial class CustomerArrivalManager
    {
        //通过原有生成管线交付一人或两人的到客组。
        private bool TrySpawnCustomerWave(Zone_Shop shop, RuntimeCustomerKind kind, bool showArrivalMessage,
            bool respectFailureBackoff, out int spawnedCount, out string failReason, out Pawn spawnedPawn)
        {
            spawnedCount = 0;
            spawnedPawn = null;
            failReason = "";
            var context = BuildShopContext(shop, Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>(),
                RuntimeIndex.CountActiveForShop(shop.ID));
            int available = Math.Max(0, context.Capacity - context.CurrentCustomers);
            if (available == 0) { failReason = "商店接待人数已满"; return false; }
            CustomerArrivalReservation reservation = null;
            var pawns = new List<Pawn>();
            bool completed = false;
            try
            {
                reservation = SimShopCustomerApi.PrepareArrival(shop, kind, available);
                int count = reservation?.count ?? 1;
                if (count < 1 || count > 2 || count > available)
                {
                    failReason = "同行人数超出本次可用接待容量";
                    return false;
                }
                for (int i = 0; i < count; i++)
                {
                    if (!TrySpawnSingleCustomer(shop, kind, showArrivalMessage && i == count - 1,
                        respectFailureBackoff && i == 0, out _, out failReason, out Pawn pawn)) return false;
                    pawns.Add(pawn);
                }
                reservation?.owner.CompleteArrival(shop, kind, reservation, pawns);
                completed = true;
                spawnedCount = pawns.Count;
                spawnedPawn = pawns[0];
                foreach (var pawn in pawns) SimShopEvents.NotifyCustomerArrived(pawn);
                return true;
            }
            catch (Exception exception)
            {
                failReason = "顾客同行预订失败：" + exception.Message;
                Log.Error("[RSMF] " + failReason + "\n" + exception);
                return false;
            }
            finally
            {
                if (!completed)
                {
                    reservation?.owner.CancelArrival(shop, reservation, failReason);
                    foreach (var pawn in pawns) RejectSpawnedCustomerAtEdge(pawn, pawn.lord);
                }
            }
        }
    }
}
