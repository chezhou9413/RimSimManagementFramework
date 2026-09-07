using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace SimManagementLib.Pojo
{
    public class ComboItem : IExposable
    {
        public ThingDef def;
        public int count;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref count, "count", 1);
        }
    }
}
