using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    public partial class LordJob_CustomerVisit
    {
        public Pojo.RuntimeCustomerKind RuntimeCustomerKind => CustomerCatalog.GetKind(customerKindId);

        public void AddCartItem(int pawnId, ThingDef def, int count)
        {
            if (pawnId <= 0 || def == null || count <= 0) return;

            if (!cartItems.TryGetValue(pawnId, out List<CustomerCartItem> list))
            {
                list = new List<CustomerCartItem>();
                cartItems[pawnId] = list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                CustomerCartItem item = list[i];
                if (item == null || item.def != def) continue;
                item.count += count;
                return;
            }

            list.Add(new CustomerCartItem { def = def, count = count });
        }

        public void AddCartItemsFromCombo(int pawnId, List<ComboItem> comboItems)
        {
            if (comboItems == null) return;
            for (int i = 0; i < comboItems.Count; i++)
            {
                ComboItem comboItem = comboItems[i];
                if (comboItem == null || comboItem.def == null || comboItem.count <= 0) continue;
                AddCartItem(pawnId, comboItem.def, comboItem.count);
            }
        }

        public List<CustomerCartItem> GetCartItems(int pawnId)
        {
            return cartItems.TryGetValue(pawnId, out List<CustomerCartItem> list) ? list : null;
        }

        public void ClearCustomerCart(int pawnId)
        {
            cartItems.Remove(pawnId);
            cartValues[pawnId] = 0f;
            checkoutOrder.Remove(pawnId);
            browseWaitStartTick.Remove(pawnId);
        }

        public void MarkPawnReadyForCheckout(int pawnId)
        {
            if (pawnId <= 0) return;
            if (!readyForCheckout.Contains(pawnId))
                readyForCheckout.Add(pawnId);

            if (AreAllActivePawnsReadyForCheckout())
                lord?.ReceiveMemo("Customer_ReadyToCheckout");
        }

        public void SetPawnSettings(int pawnId, CustomerRuntimeSettings settings)
        {
            if (pawnId <= 0 || settings == null) return;
            pawnSettings[pawnId] = settings;
        }

        public CustomerRuntimeSettings GetPawnSettings(int pawnId)
        {
            return pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) ? settings : null;
        }

        public int GetBudgetForPawn(int pawnId)
        {
            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null && settings.budget > 0)
                return settings.budget;
            return totalBudget > 0 ? totalBudget : 1;
        }

        public int GetQueuePatienceForPawn(int pawnId)
        {
            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null && settings.queuePatienceTicks > 0)
                return settings.queuePatienceTicks;
            if (RuntimeCustomerKind?.shoppingBehavior != null && RuntimeCustomerKind.shoppingBehavior.queuePatience > 0)
                return RuntimeCustomerKind.shoppingBehavior.queuePatience;
            return 2500;
        }

        public float GetPreferenceMultiplier(int pawnId, ThingDef def)
        {
            float multiplier = 1f;

            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null)
                multiplier *= settings.GetPreferenceMultiplier(def);

            if (RuntimeCustomerKind != null)
                multiplier *= RuntimeCustomerKind.GetPreferenceMultiplier(def);

            return multiplier;
        }

        public int EnsureCheckoutOrder(int pawnId)
        {
            if (checkoutOrder.TryGetValue(pawnId, out int order)) return order;
            int next = nextCheckoutOrder++;
            checkoutOrder[pawnId] = next;
            return next;
        }

        public int GetCheckoutOrder(int pawnId)
        {
            return checkoutOrder.TryGetValue(pawnId, out int order) ? order : int.MaxValue;
        }

        public void QueuePostCheckoutJobs(int pawnId, IEnumerable<Job> jobs)
        {
            if (pawnId <= 0 || jobs == null) return;

            List<Job> list = jobs.Where(j => j != null).ToList();
            if (list.NullOrEmpty()) return;

            if (!postCheckoutJobs.TryGetValue(pawnId, out List<Job> existing))
            {
                existing = new List<Job>();
                postCheckoutJobs[pawnId] = existing;
            }

            // Jobs are queued here rather than assigned immediately so checkout can finish cleanly first.
            existing.AddRange(list);
            postCheckoutRequired.Add(pawnId);
        }

        public bool TryTakeNextPostCheckoutJob(int pawnId, out Job job)
        {
            job = null;
            if (pawnId <= 0) return false;
            if (!postCheckoutJobs.TryGetValue(pawnId, out List<Job> list) || list.NullOrEmpty()) return false;

            job = list[0];
            list.RemoveAt(0);
            if (list.Count <= 0)
                postCheckoutJobs.Remove(pawnId);

            return job != null;
        }

        public bool NeedsPostCheckoutCompletion(int pawnId)
        {
            return pawnId > 0 && postCheckoutRequired.Contains(pawnId);
        }

        public void MarkPostCheckoutCompleted(int pawnId)
        {
            if (pawnId <= 0) return;
            postCheckoutRequired.Remove(pawnId);
            postCheckoutJobs.Remove(pawnId);
        }

        public int GetOrInitBrowseWaitStartTick(int pawnId, int nowTick)
        {
            if (pawnId <= 0) return nowTick;
            if (!browseWaitStartTick.TryGetValue(pawnId, out int start) || start <= 0 || start > nowTick)
            {
                browseWaitStartTick[pawnId] = nowTick;
                return nowTick;
            }

            return start;
        }

        public void ClearBrowseWaitStartTick(int pawnId)
        {
            if (pawnId <= 0) return;
            browseWaitStartTick.Remove(pawnId);
        }

        private bool AreAllActivePawnsReadyForCheckout()
        {
            if (lord?.ownedPawns == null || lord.ownedPawns.Count == 0) return true;

            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                if (!readyForCheckout.Contains(pawn.thingIDNumber))
                    return false;
            }

            return true;
        }

        public void CheckAllCheckoutsDone()
        {
            bool allDone = true;
            foreach (Pawn pawn in lord.ownedPawns)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;

                int pawnId = pawn.thingIDNumber;
                if (cartValues.TryGetValue(pawnId, out float value) && value > 0f)
                {
                    allDone = false;
                    break;
                }

                // A pawn is not considered complete until every queued follow-up job has been consumed.
                if (postCheckoutRequired.Contains(pawnId))
                {
                    allDone = false;
                    break;
                }
            }

            if (allDone)
            {
                lord.ReceiveMemo("Customer_CheckoutCompleted");
            }
        }
    }
}
