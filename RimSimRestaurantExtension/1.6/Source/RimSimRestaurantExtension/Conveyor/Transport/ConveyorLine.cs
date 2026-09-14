using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Conveyor.Stocking;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Transport
{
    //维护相连线路的菜单与推进时钟，职责是独立管理库存和供电状态。
    public sealed class ConveyorLine : IExposable
    {
        public int id, anchorId, clock, lostPlates;
        public float wasteCost;
        public bool paused;
        public string notice = "";
        public List<ConveyorStockRule> rules = new List<ConveyorStockRule>();
        public List<Building_SushiConveyor> segments = new List<Building_SushiConveyor>();
        public readonly Rendering.ConveyorRenderClock renderClock = new Rendering.ConveyorRenderClock();
        internal readonly ConveyorTransportCache transport = new ConveyorTransportCache();
        private Zone_Shop shop;
        private bool sameShop;
        public Zone_Shop Shop => shop;
        public bool SameShop => sameShop;
        public bool Powered => transport.Powered();
        public int Occupied
        {
            //实时读取当前容器，职责是让同一步内的取餐和上架立即反映在容量统计中。
            get
            {
                int count = 0;
                for (int i = 0; i < segments.Count; i++) if (segments[i].Food != null) count++;
                return count;
            }
        }
        public bool CanStock => SameShop && Powered && !paused && RestaurantOrderUtility.Settings.GetOrCreate(Shop.ID).enabled;
        public int Interval => transport.interval;

        //刷新店铺归属缓存，职责是定期感知商店区域编辑并避免每格绘制重复扫描。
        public void RefreshShop()
        {
            shop = segments.Count == 0 ? null : SimShopServiceApi.FindShop(segments[0].Map, segments[0].Position);
            sameShop = shop != null;
            if (!sameShop) return;
            var zones = shop.Map.zoneManager;
            for (int i = 0; i < segments.Count; i++)
            {
                var cell = segments[i].Position;
                if (zones.ZoneAt(cell) != shop && !shop.Cells.Contains(cell)) { sameShop = false; break; }
            }
        }

        //按规则统计已上架盘数，职责是把有实物的盘子计入容量。
        public int Count(string ruleId)
        {
            int count = 0;
            for (int i = 0; i < segments.Count; i++)
                if (segments[i].Food != null && segments[i].plate?.ruleId == ruleId) count++;
            return count;
        }

        //序列化线路配置，拓扑成员由建筑线路编号重建。
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref anchorId, "anchorId");
            Scribe_Values.Look(ref clock, "clock");
            Scribe_Values.Look(ref lostPlates, "lostPlates");
            Scribe_Values.Look(ref wasteCost, "wasteCost");
            Scribe_Values.Look(ref paused, "paused");
            Scribe_Values.Look(ref notice, "notice", "");
            Scribe_Collections.Look(ref rules, "rules", LookMode.Deep);
        }
    }
}
