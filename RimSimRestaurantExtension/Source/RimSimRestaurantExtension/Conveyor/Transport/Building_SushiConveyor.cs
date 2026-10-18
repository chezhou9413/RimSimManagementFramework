using SimManagementLib.Tool;
using System.Collections.Generic;
using RimSimRestaurantExtension.Conveyor.Rendering;
using RimSimRestaurantExtension.Conveyor.UI;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Transport
{
    //承载一格传送带及一盘真实食品，职责是保存物品、绘制线路并提供配置入口。
    public sealed class Building_SushiConveyor : Building, IThingHolder
    {
        private ThingOwner<Thing> contents;
        public ConveyorPlate plate;
        public int lineId;
        public float progress = 0.5f;
        public Pawn reservedBy;
        public string reservedRuleId;
        public string reservationKey;
        internal ConveyorTransportNode transportNode;
        public Thing Food => contents.Count == 0 ? null : contents[0];
        public MapComponent_SushiConveyor Manager => Map.GetComponent<MapComponent_SushiConveyor>();
        public ConveyorLine Line => Spawned ? Manager.LineFor(this) : null;

        //建立不自动合堆的容器，职责是保持餐盘实物与来源一一对应。
        public Building_SushiConveyor() { contents = new ThingOwner<Thing>(this, false); }

        //提供原版持有容器，使食品组件按环境温度正常更新。
        public ThingOwner GetDirectlyHeldThings() => contents;

        //枚举嵌套容器，职责是让原版正确遍历持有物。
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, contents);

        //注册线路成员，职责是让新建和读档后的地图重算拓扑。
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            Manager.Register(this);
        }

        //释放内部食品并注销节点，职责是避免拆除、卸载和移装吞掉实物。
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Spawned)
            {
                if (!contents.TryDropAll(Position, Map, ThingPlaceMode.Near))
                    throw new System.InvalidOperationException("传送带移除前无法释放内部食品");
                plate = null;
                Manager.Unregister(this);
                transportNode = null;
            }
            base.DeSpawn(mode);
        }

        //保存方向之外的食品和线路编号，职责是支持运输中存读档。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref contents, "sushiContents", this);
            Scribe_Deep.Look(ref plate, "sushiPlate");
            Scribe_Values.Look(ref lineId, "sushiLineId");
            Scribe_Values.Look(ref progress, "sushiProgress", 0.5f);
            Scribe_References.Look(ref reservedBy, "sushiReservedBy");
            Scribe_Values.Look(ref reservedRuleId, "sushiReservedRuleId");
            Scribe_Values.Look(ref reservationKey, "sushiReservationKey");
        }

        //绘制当前连接形状、传送动画和真实餐品。
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            ConveyorRenderer.Draw(this, drawLoc);
            Comps_DrawAt(drawLoc, flip);
            Comps_PostDraw();
        }

        //提供线路配置按钮，职责是让任意一段编辑整条线路。
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos()) yield return gizmo;
            yield return new Command_Action { defaultLabel = SimTranslation.T("RSR.Building.ConfigureConveyor"),
                defaultDesc = SimTranslation.T("RSR.Building.ConfigureConveyorDesc"),
                icon = def.uiIcon, action = () => Find.WindowStack.Add(new Dialog_ConveyorStock(this)) };
        }

        //显示线路运行情况，职责是提供断电、跨店和暂停原因。
        public override string GetInspectString()
        {
            var line = Line;
            return base.GetInspectString() + (line == null ? "" : SimTranslation.T("RSR.Building.LineStock", (line.id).Named("id"), (line.Occupied).Named("occupied"), (line.segments.Count).Named("capacity")) + (!line.SameShop ? SimTranslation.T("RSR.Building.LineOutsideShop") : !line.Powered ? SimTranslation.T("RSR.Building.LineUnpowered") : line.paused ? SimTranslation.T("RSR.Building.LinePaused") : SimTranslation.T("RSR.Building.LineRunning"))
                + (line.notice.NullOrEmpty() ? "" : "\n" + line.notice));
        }
    }
}
