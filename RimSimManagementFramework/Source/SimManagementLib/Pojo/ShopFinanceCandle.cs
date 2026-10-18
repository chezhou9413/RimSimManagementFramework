using Verse;

namespace SimManagementLib.Pojo
{
    //记录一个经营日内累计账面利润的真实开、高、低、收。
    public class ShopFinanceCandle : IExposable
    {
        public int day;
        public float open;
        public float high;
        public float low;
        public float close;
        public int bills;
        public bool partial;

        //保存逐笔成交形成的日内极值，不依赖可能被裁剪的账单明细。
        public void ExposeData()
        {
            Scribe_Values.Look(ref day, "day");
            Scribe_Values.Look(ref open, "open");
            Scribe_Values.Look(ref high, "high");
            Scribe_Values.Look(ref low, "low");
            Scribe_Values.Look(ref close, "close");
            Scribe_Values.Look(ref bills, "bills");
            Scribe_Values.Look(ref partial, "partial");
        }
    }
}
