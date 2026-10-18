using Verse;

namespace SimManagementLib.SimThingComp
{
    /// <summary>
    /// 定义展台收藏品标记组件的加载参数，职责是让 ThingDef 通过 XML 声明可被收藏品展台扫描。
    /// </summary>
    public class CompProperties_DisplayStandCollectible : CompProperties
    {
        /// <summary>
        /// 初始化标记组件类型，供 RimWorld 根据 XML 创建组件实例。
        /// </summary>
        public CompProperties_DisplayStandCollectible()
        {
            compClass = typeof(ThingComp_DisplayStandCollectible);
        }
    }

    /// <summary>
    /// 标记物品可以被收藏品展台选择和搬运，组件本身不保存运行时状态。
    /// </summary>
    public class ThingComp_DisplayStandCollectible : ThingComp
    {
    }
}
