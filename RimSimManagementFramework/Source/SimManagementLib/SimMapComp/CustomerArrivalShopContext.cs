using SimManagementLib.SimZone;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //类职责：保存单个商店的原子刷新快照，避免刷新热路径重新扫描货柜和服务设施。
    internal sealed class CustomerArrivalShopContext
    {
        public Zone_Shop Shop;
        public int CurrentCustomers;
        public int Capacity;
        public float DemandFactor = 1f;
        public bool HasCheckoutService;
        public bool IsOpen;
        public IntVec3 EntryCell = IntVec3.Invalid;
        public HashSet<string> MatchingKindIds = new HashSet<string>();

        public bool IsAtCapacity => CurrentCustomers >= Capacity;

        //判断指定顾客类型是否能被当前商店吸引，职责是只读取已经完成的快照。
        public bool CanSpawn(Pojo.RuntimeCustomerKind kind, bool requireCheckoutService = true)
        {
            return Shop != null
                && kind != null
                && !kind.pawnKindDefs.NullOrEmpty()
                && IsOpen
                && MatchingKindIds.Contains(kind.kindId ?? "")
                && (!requireCheckoutService || HasCheckoutService)
                && !IsAtCapacity;
        }
    }
}
