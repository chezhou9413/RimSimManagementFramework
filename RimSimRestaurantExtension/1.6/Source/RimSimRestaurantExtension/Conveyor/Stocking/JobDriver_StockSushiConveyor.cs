using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Conveyor.Transport;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.SimMapComp;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Conveyor.Stocking
{
    //执行单盘补餐任务，职责是持久化领取、取料、制作、上架与清理阶段。
    public sealed class JobDriver_StockSushiConveyor : JobDriver
    {
        public ConveyorRestockTask task;
        private MapComponent_InventoryReservations Ledger => pawn.Map.GetComponent<MapComponent_InventoryReservations>();

        //保存独立任务，职责是让读档继续当前工作而不是再次领取。
        public override void ExposeData() { base.ExposeData(); Scribe_Deep.Look(ref task, "sushiRestockTask"); }

        //原子预约线路容量、上架工作点和食材，职责是阻止多人补货超过目标盘数。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!(job.targetA.Thing is Building_SushiConveyor belt)) return false;
            task = ConveyorStockPlanner.Find(pawn, belt, out var stock);
            if (task == null || !pawn.Reserve(belt, job, 1, -1, null, errorOnFailed)) return false;
            task.key = "SushiRestock/" + pawn.Map.uniqueID + "/" + job.loadID;
            if (task.stove != null && (!pawn.Reserve(task.stove, job, 1, -1, null, errorOnFailed)
                || !pawn.ReserveSittableOrSpot(task.stove.InteractionCell, job, errorOnFailed))) return false;
            if (!task.cleaning && !Ledger.Reserve(task.key, stock)) return false;
            belt.reservedBy = pawn;
            belt.reservedRuleId = task.ruleId;
            belt.reservationKey = task.key;
            job.countQueue = stock.Select(t => t.Count).ToList();
            foreach (var item in stock) job.AddQueuedTarget(TargetIndex.B, item.Thing);
            job.placedThings = new List<ThingCountClass>();
            return true;
        }

        //构造补餐或清理流程，职责是让实际物品始终只属于一个容器或地面位置。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(FinishTask);
            this.FailOn(() => task == null || task.destination?.Spawned != true || task.destination.reservedBy != pawn);
            Toil upload = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil cleanup = ToilMaker.MakeToil("SushiClearPlate");
            cleanup.defaultCompleteMode = ToilCompleteMode.Instant;
            cleanup.initAction = TakeForCleaning;
            Toil stock = ToilMaker.MakeToil("SushiUploadPlate");
            stock.defaultCompleteMode = ToilCompleteMode.Never;
            stock.initAction = () => ticksLeftThisToil = RestaurantOrderUtility.Settings.GetOrCreate(task.shopId).maxWaitTicks;
            stock.tickAction = WaitForUpload;
            yield return Toils_Jump.JumpIf(upload, () => task.cleaning || task.produced);
            foreach (var toil in ConveyorIngredientToils.Collect(this, upload)) yield return toil;
            Toil cook = ToilMaker.MakeToil("SushiCook");
            cook.defaultCompleteMode = ToilCompleteMode.Delay;
            cook.defaultDuration = 1;
            cook.initAction = () =>
            {
                //烹饪时让原版朝向跟踪器面向灶台，A 目标仍保留为上架传送带。
                job.SetTarget(TargetIndex.C, task.stove);
                rotateToFace = TargetIndex.C;
                ticksLeftThisToil = RestaurantCookingUtility.GetCookTicks(pawn, task.stove, task);
            };
            //离开烹饪阶段时恢复上架目标，覆盖制作完成和任务中断两种路径。
            cook.AddFinishAction(() => rotateToFace = TargetIndex.A);
            cook.tickAction = () =>
            {
                if (!RestaurantCookingUtility.CanCookOrderAt(task.stove, task))
                { Fail("补餐灶台已不可用"); return; }
                RestaurantCookingUtility.NotifyUsedThisTick(task.stove);
                if (task.recipe.workSkill != null) pawn.skills?.Learn(task.recipe.workSkill, 0.1f * task.recipe.workSkillLearnFactor);
            };
            cook.WithProgressBar(TargetIndex.C, () => 1f - ticksLeftThisToil / (float)RestaurantCookingUtility.GetCookTicks(pawn, task.stove, task));
            yield return cook;
            Toil produce = ToilMaker.MakeToil("SushiProduce");
            produce.defaultCompleteMode = ToilCompleteMode.Instant;
            produce.initAction = Produce;
            yield return produce;
            yield return upload;
            yield return Toils_Jump.JumpIf(cleanup, () => task.cleaning);
            yield return stock;
            Toil finish = ToilMaker.MakeToil("SushiFinish");
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            finish.initAction = () => task.finished = true;
            yield return Toils_Jump.Jump(finish);
            yield return cleanup;
            Toil recover = ToilMaker.MakeToil("SushiRecoverPath");
            recover.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            recover.initAction = () =>
            {
                var shop = RestaurantOrderUtility.FindShopById(pawn.Map, task.shopId);
                Thing food = pawn.carryTracker.CarriedThing;
                var cells = RestaurantKitchenStorage.Cells(shop).Where(c => RestaurantKitchenStorage.Accepts(shop, c, food)
                        && pawn.CanReserveAndReach(c, PathEndMode.OnCell, Danger.Some)).OrderBy(c => c.DistanceToSquared(pawn.Position)).ToList();
                if (cells.Count == 0) { DropCarried(); ReadyForNextToil(); return; }
                job.SetTarget(TargetIndex.C, cells[0]);
                if (!pawn.Reserve(cells[0], job)) { DropCarried(); ReadyForNextToil(); return; }
                pawn.pather.StartPath(cells[0], PathEndMode.OnCell);
            };
            yield return recover;
            Toil drop = ToilMaker.MakeToil("SushiRecoverDrop");
            drop.defaultCompleteMode = ToilCompleteMode.Instant;
            drop.initAction = DropCarried;
            yield return drop;
            yield return finish;
        }

        //完成单盘真实制作，职责是共用食材核验与原版成品生成。
        private void Produce()
        {
            if (!RestaurantProductionUtility.TryProduce(pawn, task.stove, task, job, out Thing meal, out float cost))
            { Fail("补餐制作失败：实物食材或配方已失效"); return; }
            task.cost = cost; task.produced = true;
            job.placedThings = null;
            Ledger.Release(task.key);
            if (!pawn.carryTracker.innerContainer.TryAdd(meal, false))
            { GenPlace.TryPlaceThing(meal, pawn.Position, pawn.Map, ThingPlaceMode.Near); Fail("补餐成品无法携带"); }
        }

        //在工作点等待流动空位，职责是在等待上限内提交餐盘而不阻挡经过的食品。
        private void WaitForUpload()
        {
            if (TryUpload()) { ReadyForNextToil(); return; }
            if (pawn.jobs.curDriver != this) return;
            if (ticksLeftThisToil <= 1) Fail("等待传送带上架空位超时");
        }

        //上架当前实物，职责是再次校验供电、规则、实物间距和目标库存后转移餐盘。
        private bool TryUpload()
        {
            var belt = task.destination;
            var line = belt.Line;
            Thing food = pawn.carryTracker.CarriedThing;
            var rule = line?.rules.FirstOrDefault(r => r.id == task.ruleId && r.enabled);
            if (line?.CanStock != true || line.Shop.ID != task.shopId || rule == null
                || food == null || food.def != task.mealDef || food.stackCount != task.mealCount
                || !food.IngestibleNow || line.Count(rule.id) >= rule.target)
            { Fail("上架时线路、食品或目标库存已变化"); return false; }
            if (!ConveyorMovement.TryInsertionProgress(belt, out float progress)) return false;
            Ledger.Release(task.key);
            if (!pawn.carryTracker.innerContainer.TryTransferToContainer(food, belt.GetDirectlyHeldThings(), false))
            { Fail("传送带容器拒绝餐盘"); return false; }
            belt.plate = new ConveyorPlate { ruleId = task.ruleId, cost = task.cost };
            belt.progress = progress;
            food.SetForbidden(false, false);
            task.finished = true;
            return true;
        }

        //取走需要清理的餐盘，职责是保留可回收实物并只记录一次腐坏成本。
        private void TakeForCleaning()
        {
            var belt = task.destination;
            if (!ConveyorStockPlanner.NeedsCleaning(belt) || belt.Food != task.cleaningFood || pawn.carryTracker.CarriedThing != null)
            { Fail("待清理餐盘已变化"); return; }
            Thing food = belt.Food;
            if (!food.IngestibleNow || food.TryGetComp<CompRottable>()?.Stage > RotStage.Fresh)
            {
                if (belt.plate?.wasteRecorded != true)
                { belt.Line.lostPlates++; belt.Line.wasteCost += task.cost; if (belt.plate != null) belt.plate.wasteRecorded = true; }
            }
            if (!belt.GetDirectlyHeldThings().TryTransferToContainer(food, pawn.carryTracker.innerContainer, false))
            { Fail("厨师无法携带待清理餐盘"); return; }
            belt.plate = null;
        }

        //释放携带实物，职责是中断和清理均不吞掉原料或成品。
        private void DropCarried()
        {
            if (pawn.carryTracker.CarriedThing != null)
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }

        //解除业务与物理预约，职责是在所有结束路径释放空位和原料。
        private void FinishTask(JobCondition condition)
        {
            if (task == null) return;
            Ledger.Release(task.key);
            if (task.destination != null && task.destination.reservedBy == pawn)
            { task.destination.reservedBy = null; task.destination.reservedRuleId = null; task.destination.reservationKey = null; }
            if (job.placedThings != null)
                foreach (var part in job.placedThings)
                    if (part.thing != null) pawn.Map.physicalInteractionReservationManager.TryRelease(pawn, job, part.thing);
            DropCarried();
            if (!task.finished && condition != JobCondition.InterruptForced)
                Log.Warning("[RimSimRestaurantExtension] 传送带补餐结束：" + condition + "，任务 " + task.key);
        }

        //输出具体失败原因并结束任务，职责是保留可诊断信息。
        public void Fail(string reason)
        {
            Log.Warning("[RimSimRestaurantExtension] " + reason);
            EndJobWith(JobCondition.Incompletable);
        }
    }
}
