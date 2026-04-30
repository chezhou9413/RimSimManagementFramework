using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace SimManagementLib.Pojo
{
    public class ComboData : IExposable
    {
        public string comboName;
        public List<ComboItem> items = new List<ComboItem>();
        public float totalPrice;

        public void ExposeData()
        {
            Scribe_Values.Look(ref comboName, "comboName", "未命名套餐");
            Scribe_Values.Look(ref totalPrice, "totalPrice", 0f);
            Scribe_Collections.Look(ref items, "items", LookMode.Deep);
        }
    }
}
