using Verse;
namespace SimManagementLib.Pojo
{
    //保存预留物品原来的禁用状态，职责是终止业务后恢复普通取用。
    public sealed class InventoryProtection : IExposable
    {
        public Thing thing;
        public bool wasForbidden;
        //保存原始状态与直接物品引用。
        public void ExposeData()
        {
            Scribe_References.Look(ref thing, "thing");
            Scribe_Values.Look(ref wasForbidden, "wasForbidden");
        }
    }
}
