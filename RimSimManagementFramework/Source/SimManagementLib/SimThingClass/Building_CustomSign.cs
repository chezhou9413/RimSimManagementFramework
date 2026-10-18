using Verse;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 作为自定义招牌旧 Def 的建筑类型入口，实际编辑、保存和绘制职责由自定义招牌组件承担。
    /// </summary>
    public class Building_CustomSign : Building
    {
    }

    /// <summary>
    /// 标识招牌编辑器中可编辑的三个面。
    /// </summary>
    public enum SignFaceKind
    {
        South,
        East,
        North
    }
}
