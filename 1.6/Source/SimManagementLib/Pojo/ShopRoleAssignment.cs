using Verse;

namespace SimManagementLib.Pojo
{
    public class ShopRoleAssignment : IExposable
    {
        public string roleDefName;
        public Pawn pawn;

        public void ExposeData()
        {
            Scribe_Values.Look(ref roleDefName, "roleDefName", "");
            Scribe_References.Look(ref pawn, "pawn");
        }
    }
}
