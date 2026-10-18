using System.Linq;
using RimWorld;
using Verse;

namespace SimManagementLib.SimThingClass
{
    //让顾问读取与实际货柜一致的来源和槽位限制，不在查询时修复或分配槽位。
    public partial class Building_UniqueGoodsContainer
    {
        //返回明确原因，避免将禁用、分类不符和已指派混成“不可用”。
        internal string AdvisorSourceIssue(Thing source)
        {
            if (source == null || source.Destroyed || !source.Spawned || source.Map != Map)
                return "来源不在本地图地面上，可能正被携带；请重新查询 stock/unique_sources。";
            if (source.IsForbidden(Faction.OfPlayer))
                return "forbidden=true：物品被禁用。确需出售时先用 order command=allow targetId=" + source.thingIDNumber + " 解禁该物品，再提交上架。";
            if (CompBiocodable.IsBiocoded(source)) return "物品已生物编码，不能用于专业货柜。";
            if (source is Apparel apparel && apparel.WornByCorpse) return "衣物沾染尸臭，不能用于专业货柜。";
            if (!IsEligibleUniqueThing(source)) return "物品分类或品质/材质条件不符合该货柜，请查询 unique_sources 获取合格来源。";
            if (IsSourceAssignedAnywhere(source.thingIDNumber)) return "物品已被专业货柜指派，请查询 inventory 的 pendingSourceThingId/storedThingId，不要重复指派。";
            if (!uniqueSlots.Any(slot => !slot.IsOccupied && IsEligibleUniqueThingForSlot(source, slot.index)))
                return "没有兼容的空槽；待搬运和顾客预约也会占槽，请查询 inventory。";
            return "";
        }
    }
}
