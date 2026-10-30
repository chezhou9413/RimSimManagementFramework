using Verse;

namespace RimSimHotelExtension.Models
{
    //保存旅馆最近的资金变化，取款本金和营业收入分开显示。
    public sealed class HotelMoneyRecord : IExposable
    {
        public int tick;
        public Pawn pawn;
        public int shopZoneId;
        public string kind;
        public string label;
        public int amount;
        public int fee;

        //保存可供经营界面查看的有限交易记录。
        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick");
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref shopZoneId, "shopZoneId");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref amount, "amount");
            Scribe_Values.Look(ref fee, "fee");
        }
    }
}
