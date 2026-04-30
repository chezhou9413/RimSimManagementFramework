using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Pojo
{
    public class FinanceLineItem : IExposable
    {
        public string label;
        public string defName;
        public bool isCombo;
        public int count;
        public float amount;
        public float cost;

        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label", "");
            Scribe_Values.Look(ref defName, "defName", "");
            Scribe_Values.Look(ref isCombo, "isCombo", false);
            Scribe_Values.Look(ref count, "count", 0);
            Scribe_Values.Look(ref amount, "amount", 0f);
            Scribe_Values.Look(ref cost, "cost", 0f);
        }
    }

    public class PendingFinanceBill : IExposable
    {
        public int zoneId = -1;
        public string zoneLabel = "";
        public List<FinanceLineItem> lines = new List<FinanceLineItem>();

        public float TotalAmount
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < lines.Count; i++)
                    total += lines[i].amount;
                return total;
            }
        }

        public void AddOrMergeLine(FinanceLineItem incoming)
        {
            if (incoming == null) return;

            for (int i = 0; i < lines.Count; i++)
            {
                FinanceLineItem line = lines[i];
                if (line == null) continue;
                if (line.isCombo != incoming.isCombo) continue;

                if (incoming.isCombo)
                {
                    if (line.label != incoming.label) continue;
                }
                else
                {
                    if (line.defName != incoming.defName) continue;
                }

                line.count += incoming.count;
                line.amount += incoming.amount;
                line.cost += incoming.cost;
                return;
            }

            lines.Add(incoming);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref zoneId, "zoneId", -1);
            Scribe_Values.Look(ref zoneLabel, "zoneLabel", "");
            Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);
            if (lines == null) lines = new List<FinanceLineItem>();
        }
    }

    public class FinanceBillRecord : IExposable
    {
        public int tickAbs;
        public int gameDay;
        public int zoneId = -1;
        public string zoneLabel = "";
        public string customerName = "";
        public int paidSilver;
        public List<FinanceLineItem> lines = new List<FinanceLineItem>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref tickAbs, "tickAbs", 0);
            Scribe_Values.Look(ref gameDay, "gameDay", 0);
            Scribe_Values.Look(ref zoneId, "zoneId", -1);
            Scribe_Values.Look(ref zoneLabel, "zoneLabel", "");
            Scribe_Values.Look(ref customerName, "customerName", "");
            Scribe_Values.Look(ref paidSilver, "paidSilver", 0);
            Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);
            if (lines == null) lines = new List<FinanceLineItem>();
        }
    }
}
