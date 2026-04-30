using SimManagementLib.SimDef;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    public static class CustomerExpressionEvents
    {
        public const string Arrival = "arrival";
        public const string BrowseStart = "browse_start";
        public const string BrowseWait = "browse_wait";
        public const string BrowseNoMatch = "browse_no_match";
        public const string PurchaseItem = "purchase_item";
        public const string PurchasePreferredItem = "purchase_preferred_item";
        public const string PurchaseCombo = "purchase_combo";
        public const string CheckoutQueueStart = "checkout_queue_start";
        public const string CheckoutServiceStart = "checkout_service_start";
        public const string CheckoutPaid = "checkout_paid";
        public const string CheckoutTimeout = "checkout_timeout";
        public const string DineStart = "dine_start";
        public const string DineFinish = "dine_finish";
        public const string DrugUseStart = "drug_use_start";
        public const string DrugUseFinish = "drug_use_finish";
    }

    public class CustomerExpressionRequest
    {
        public HashSet<string> contextTags = new HashSet<string>();

        public CustomerExpressionRequest AddTag(string tag)
        {
            if (!tag.NullOrEmpty())
            {
                contextTags.Add(tag);
            }

            return this;
        }
    }

    public static class CustomerExpressionUtility
    {
        private static readonly Dictionary<string, int> LastShownTicks = new Dictionary<string, int>();

        public static bool TryShowExpression(Pawn pawn, string eventId, CustomerExpressionRequest request = null)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null || eventId.NullOrEmpty()) return false;

            if (request == null)
            {
                request = new CustomerExpressionRequest();
            }
            HashSet<string> tags = CollectTags(pawn, request.contextTags);

            List<CustomerExpressionSetDef> matchedSets = DefDatabase<CustomerExpressionSetDef>.AllDefsListForReading
                .Where(def => def != null && def.MatchesPawn(pawn, tags))
                .ToList();
            if (matchedSets.NullOrEmpty()) return false;

            int highestPriority = matchedSets.Max(def => def.priority);
            List<CustomerExpressionEntry> candidates = matchedSets
                .Where(def => def.priority == highestPriority)
                .SelectMany(def => def.expressions ?? new List<CustomerExpressionEntry>())
                .Where(entry => entry != null && entry.MatchesEvent(eventId, tags))
                .ToList();
            if (candidates.NullOrEmpty()) return false;

            CustomerExpressionEntry picked = candidates.RandomElementByWeight(entry => Mathf.Max(0.01f, entry.weight));
            if (picked == null) return false;
            if (!Rand.Chance(Mathf.Clamp01(picked.chance))) return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            string cooldownKey = pawn.thingIDNumber + ":" + eventId;
            int cooldownTicks = Mathf.Max(0, picked.cooldownTicks);
            if (cooldownTicks > 0 && LastShownTicks.TryGetValue(cooldownKey, out int lastTick) && now - lastTick < cooldownTicks)
            {
                return false;
            }

            Texture2D texture = picked.ResolveTexture();
            if (texture == null) return false;

            ShopBubbleUtility.ShowCustomBubble(
                pawn,
                texture,
                null,
                picked.iconColor,
                null,
                picked.popupScale,
                false);
            LastShownTicks[cooldownKey] = now;
            return true;
        }

        public static HashSet<string> CollectTags(Pawn pawn, IEnumerable<string> extraTags = null)
        {
            HashSet<string> tags = new HashSet<string>();
            if (pawn == null) return tags;

            AddTag(tags, pawn.def?.defName);
            AddTag(tags, pawn.kindDef?.defName);
            AddTag(tags, "race:" + pawn.def?.defName);
            AddTag(tags, "pawnkind:" + pawn.kindDef?.defName);

            if (pawn.RaceProps != null)
            {
                if (pawn.RaceProps.Humanlike) AddTag(tags, "humanlike");
                if (pawn.RaceProps.Animal) AddTag(tags, "animal");
                if (pawn.RaceProps.IsMechanoid) AddTag(tags, "mechanoid");
                if (pawn.RaceProps.IsFlesh) AddTag(tags, "flesh");
            }

            AddTagsFromExtension(tags, pawn.def);
            AddTagsFromExtension(tags, pawn.kindDef);

            if (extraTags != null)
            {
                foreach (string tag in extraTags)
                {
                    AddTag(tags, tag);
                }
            }

            return tags;
        }

        private static void AddTagsFromExtension(HashSet<string> tags, Def def)
        {
            CustomerExpressionTagExtension extension = def?.GetModExtension<CustomerExpressionTagExtension>();
            if (extension == null || extension.expressionTags.NullOrEmpty()) return;

            for (int i = 0; i < extension.expressionTags.Count; i++)
            {
                AddTag(tags, extension.expressionTags[i]);
            }
        }

        private static void AddTag(HashSet<string> tags, string tag)
        {
            if (tags == null || tag.NullOrEmpty()) return;
            tags.Add(tag.Trim());
        }
    }
}
