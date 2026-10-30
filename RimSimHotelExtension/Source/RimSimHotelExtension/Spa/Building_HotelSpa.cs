using System.Collections.Generic;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using RimWorld;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.Spa
{
    //提供固定池位和分层绘制，营业参数与旅馆管理窗口使用同一份设置。
    public sealed class Building_HotelSpa : Building
    {
        private Graphic baseGraphic;
        private Graphic waterGraphic;
        private Graphic frameGraphic;
        public SpaProperties Properties => def.GetModExtension<SpaProperties>();
        public Zone_Shop Shop => Spawned ? ShopDataUtility.FindShopZone(Map, Position) : null;
        public bool Stone => Properties.stone;
        public int Capacity => SpaLayout.CapacityFor(def);
        public int DurationTicks => Stone ? HotelTuningDef.Current.stoneSpaTicks : HotelTuningDef.Current.woodSpaTicks;
        public float JoyGain => Stone ? HotelTuningDef.Current.stoneSpaJoyGain : HotelTuningDef.Current.woodSpaJoyGain;
        public float SatisfactionGain => Stone ? HotelTuningDef.Current.stoneSpaSatisfactionGain : HotelTuningDef.Current.woodSpaSatisfactionGain;
        public bool Enabled => Shop != null && HotelMapComponent.For(Map).SettingsFor(Shop).spaEnabled;
        public int TicketPrice => Shop == null ? 0 : Stone
            ? HotelMapComponent.For(Map).SettingsFor(Shop).stoneSpaPrice
            : HotelMapComponent.For(Map).SettingsFor(Shop).woodSpaPrice;
        public bool Operational => Spawned && !Destroyed && Enabled && GetComp<CompPowerTrader>()?.PowerOn == true
            && GetComp<CompBreakdownable>()?.BrokenDown != true && GetComp<ThingComp_CashStorage>() != null;

        //将本地固定池位旋转到建筑实际朝向，使用内部六格避免走到池沿外。
        public IntVec3 SlotCell(int slot)
        {
            return SpaLayout.SlotCell(def, Position, Rotation, slot);
        }

        //选中后显示与施工预览一致的占地和固定池位。
        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            SpaLayout.DrawLayout(def, Position, Rotation, Color.white);
        }

        //水面保留素材的半透明度，池沿位于建筑层，避免从人物头顶盖过去。
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (baseGraphic == null)
            {
                string name = Stone ? "StoneSpa" : "WoodSpa";
                string root = Properties.textureRoot;
                Vector2 size = def.graphicData.drawSize;
                baseGraphic = GraphicDatabase.Get<Graphic_Multi>(root + "/Base/" + name, ShaderDatabase.Cutout, size, Color.white);
                waterGraphic = GraphicDatabase.Get<Graphic_Multi>(root + "/Water/" + name, ShaderDatabase.Transparent, size, Color.white);
                frameGraphic = GraphicDatabase.Get<Graphic_Multi>(root + "/Frame/" + name, ShaderDatabase.Cutout, size, Color.white);
            }
            Vector3 layerLoc = drawLoc;
            layerLoc.y = AltitudeLayer.Building.AltitudeFor();
            baseGraphic.Draw(layerLoc, Rotation, this);
            layerLoc.y = AltitudeLayer.BuildingBelowTop.AltitudeFor();
            waterGraphic.Draw(layerLoc, Rotation, this);
            layerLoc.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            frameGraphic.Draw(layerLoc, Rotation, this);
            Comps_DrawAt(drawLoc, flip);
            Comps_PostDraw();
        }

        //营业时从池内缓缓散出少量原版水汽，画外由原版粒子可见性检查跳过。
        protected override void Tick()
        {
            base.Tick();
            if (!this.IsHashIntervalTick(150) || !Operational) return;
            Vector3 loc = SlotCell(Rand.Range(0, Capacity)).ToVector3Shifted();
            if (!loc.ShouldSpawnMotesAt(Map)) return;
            FleckCreationData steam = FleckMaker.GetDataStatic(loc, Map, FleckDefOf.AirPuff, 0.65f);
            steam.rotationRate = Rand.Range(-12f, 12f);
            steam.velocityAngle = Rand.Range(-15f, 15f);
            steam.velocitySpeed = 0.25f;
            Map.flecks.CreateFleck(steam);
        }

        //拆除时先终止会话和处理票款，再让原版释放建筑。
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            SpaMapComponent.For(Map)?.NotifyBuildingRemoved(this);
            base.DeSpawn(mode);
        }

        //在选中建筑时显示真实容量、排队与营业问题。
        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            if (!text.NullOrEmpty()) text += "\n";
            var manager = SpaMapComponent.For(Map);
            text += HotelText.T("RSH.Text.BathingSlots") + $": {manager?.OccupiedCount(this) ?? 0}/{Capacity}";
            text += "\n" + HotelText.T("RSH.Text.Waiting") + $": {manager?.WaitingCount(this) ?? 0}";
            text += "\n" + HotelText.T("RSH.Text.Admission") + $": {TicketPrice}";
            if (Shop == null) text += "\n" + HotelText.T("RSH.Text.NotInsideAShopZone");
            else if (!Enabled) text += "\n" + HotelText.T("RSH.Text.SpaAdmissionsClosed");
            else if (!Operational) text += "\n" + HotelText.T("RSH.Text.SpaHasNoPowerOrNeedsRepair");
            return text;
        }
    }
}
