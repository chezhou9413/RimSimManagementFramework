using SimManagementLib.SimThingClass;
using Verse;
namespace SimManagementLib.Api
{
    //记录已经交给顾客但尚未付款的实物，职责是保留其来源和子业务归属。
    public sealed class ActionDeliveredThing : IExposable
    {
        public string key;
        public Thing thing;
        public Building_SimContainer source;
        //保存实物引用，职责是不复制或重新生成已交付商品。
        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key");
            Scribe_References.Look(ref thing, "thing");
            Scribe_References.Look(ref source, "source");
        }
    }
}
