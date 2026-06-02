using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存商店岗位的一条员工分配记录，负责在 Pawn 离图或引用丢失后仍保留可显示和可移除的信息。
    /// </summary>
    public class ShopRoleAssignment : IExposable
    {
        public string roleDefName;
        public Pawn pawn;
        public int pawnThingId = -1;
        public string pawnLabel = "";
        public string factionLabel = "";

        /// <summary>
        /// 从 Pawn 写入显示快照，负责让临时员工离开地图后仍能在店员界面被识别。
        /// </summary>
        public void CapturePawn(Pawn source)
        {
            if (source == null) return;
            pawn = source;
            pawnThingId = source.thingIDNumber;
            pawnLabel = source.LabelShortCap;
            factionLabel = source.Faction?.Name ?? "";
        }

        /// <summary>
        /// 返回员工显示名，负责优先使用当前 Pawn 名称并在引用失效时回退到快照。
        /// </summary>
        public string DisplayLabel()
        {
            if (pawn != null && !pawn.Destroyed)
                return pawn.LabelShortCap;
            if (!string.IsNullOrEmpty(pawnLabel))
                return pawnLabel;
            if (pawnThingId >= 0)
                return "Pawn " + pawnThingId;
            return "未知员工";
        }

        /// <summary>
        /// 判断记录中的员工是否仍能在指定地图执行店员工作。
        /// </summary>
        public bool HasUsablePawnOn(Map map)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.Spawned
                && pawn.Map == map;
        }

        /// <summary>
        /// 判断记录是否指向指定 Pawn，负责移除或去重时兼容旧引用和快照编号。
        /// </summary>
        public bool MatchesPawn(Pawn target)
        {
            if (target == null) return false;
            if (pawn == target) return true;
            return pawnThingId >= 0 && pawnThingId == target.thingIDNumber;
        }

        /// <summary>
        /// 复制当前分配记录，负责在商店区域搬迁快照中保留员工快照信息。
        /// </summary>
        public ShopRoleAssignment Clone()
        {
            return new ShopRoleAssignment
            {
                roleDefName = roleDefName,
                pawn = pawn,
                pawnThingId = pawnThingId,
                pawnLabel = pawnLabel,
                factionLabel = factionLabel
            };
        }

        /// <summary>
        /// 读写岗位分配存档数据。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref roleDefName, "roleDefName", "");
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref pawnThingId, "pawnThingId", -1);
            Scribe_Values.Look(ref pawnLabel, "pawnLabel", "");
            Scribe_Values.Look(ref factionLabel, "factionLabel", "");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pawn != null)
                CapturePawn(pawn);
        }
    }
}
