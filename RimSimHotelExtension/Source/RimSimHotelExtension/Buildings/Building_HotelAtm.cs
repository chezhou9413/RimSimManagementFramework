using RimWorld;
using Verse;

namespace RimSimHotelExtension.Buildings
{
    //只在供电时提供旅行账户取款，不占用收银员岗位。
    public sealed class Building_HotelAtm : Building
    {
        public bool Available => Spawned && GetComp<CompPowerTrader>()?.PowerOn == true
            && GetComp<CompFlickable>()?.SwitchIsOn != false;
    }
}
