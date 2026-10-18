using RimWorld;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //用审批确定的材质与朝向调用原版即时建造，保留地板分层、建筑样式和放置回调。
    internal sealed class AdvisorBuildDesignator : Designator_Build
    {
        //仅准备建造参数；创建控件本身不放置建筑。
        internal AdvisorBuildDesignator(BuildableDef definition, ThingDef stuff, Rot4 rotation) : base(definition)
        {
            SetStuffDef(stuff);
            placingRot = rotation;
        }
    }
}
