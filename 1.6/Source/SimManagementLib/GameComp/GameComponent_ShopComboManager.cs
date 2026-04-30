using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace SimManagementLib.GameComp
{ 
    public class GameComponent_ShopComboManager : GameComponent
    {
        // key = Zone的ID, value = 该商店的套餐列表
        public Dictionary<int, List<ComboData>> shopCombos = new Dictionary<int, List<ComboData>>();

        // 序列化使用的临时缓存
        private List<int> tmpKeys;
        private List<List<ComboData>> tmpValues;

        public GameComponent_ShopComboManager(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref shopCombos, "shopCombos", LookMode.Value, LookMode.Deep, ref tmpKeys, ref tmpValues);
            if (shopCombos == null) shopCombos = new Dictionary<int, List<ComboData>>();
        }

        // 辅助方法：获取某个商店的套餐列表
        public List<ComboData> GetCombosForZone(Zone zone)
        {
            if (!shopCombos.TryGetValue(zone.ID, out var list))
            {
                list = new List<ComboData>();
                shopCombos[zone.ID] = list;
            }
            return list;
        }
    }
}
