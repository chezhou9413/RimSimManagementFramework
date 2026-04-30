using Verse;

namespace SimManagementLib.Pojo
{
    public class CustomerCartItem : IExposable
    {
        public ThingDef def;
        public int count;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref count, "count", 0);
        }
    }
}
