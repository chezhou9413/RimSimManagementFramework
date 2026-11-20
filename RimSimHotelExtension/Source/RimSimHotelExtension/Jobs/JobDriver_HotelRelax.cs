using System.Collections.Generic;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Services;
using RimSimHotelExtension.Tool;
using RimWorld;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Jobs
{
    //承载住客入房安顿、大堂/客房就座休息、欣赏旅馆陈设和室内漫步，不强制依赖殖民者娱乐需求。
    public sealed class JobDriver_HotelRelax : JobDriver
    {
        public const int ModeWalk = 0;
        public const int ModeSettleInRoom = 1;
        public const int ModeRoomSeat = 2;
        public const int ModeLobbySeat = 3;
        public const int ModeAdmireDecor = 4;

        //按休闲模式显示对应的住店活动描述。
        public override string GetReport() => job.count switch
        {
            ModeSettleInRoom => HotelText.T("RSH.Text.SettlingIntoRoom"),
            ModeRoomSeat => HotelText.T("RSH.Text.RelaxingInOwnRoom"),
            ModeLobbySeat => HotelText.T("RSH.Text.RelaxingInLobby"),
            ModeAdmireDecor => HotelText.T("RSH.Text.AdmiringHotelDecor"),
            _ => HotelText.T(job.targetB.IsValid ? "RSH.Text.RelaxingAtTheHotel" : "RSH.Text.WalkingAroundTheHotel")
        };

        //提前保留目的地，避免多名住客同时选择同一把椅子或空格。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed)) return false;
            pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, job.targetA.Cell);
            return true;
        }

        //抵达目标后短暂歇息，入房安顿完成时给出对客房的第一印象反馈。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            int duration = job.count == ModeWalk ? Rand.RangeInclusive(90, 180)
                : job.count == ModeSettleInRoom ? Rand.RangeInclusive(180, 320)
                : Rand.RangeInclusive(320, 620);
            Toil relax = Toils_General.Wait(duration, job.targetB.IsValid ? TargetIndex.B : TargetIndex.None);
            relax.socialMode = RandomSocialMode.SuperActive;
            relax.AddPreTickAction(() =>
            {
                pawn.GainComfortFromCellIfPossible(1);
                float rate = job.count == ModeAdmireDecor ? 0.50f : 0.40f;
                pawn.needs.joy?.GainJoy(job.def.joyGainRate * rate / 2500f, job.def.joyKind);
            });
            relax.AddFinishAction(() =>
            {
                if (job.count != ModeSettleInRoom || pawn?.Map == null) return;
                var manager = HotelMapComponent.For(pawn.Map);
                var stay = manager?.FindStay(pawn);
                var guest = manager?.GuestFor(pawn);
                if (stay == null || guest == null || guest.roomSettled) return;
                guest.roomSettled = true;
                guest.status = "RSH.Text.Staying";
                if (stay.bed == null || !stay.bed.Spawned) return;
                var preview = HotelQualityUtility.EvaluateRoomPreview(stay.bed);
                string key = preview.estimatedDailyDelta >= 3f ? "RSH.Bubble.RoomGreat"
                    : preview.estimatedDailyDelta >= 0f ? "RSH.Bubble.RoomOkay"
                    : "RSH.Bubble.RoomPoor";
                Color color = preview.estimatedDailyDelta >= 3f ? new Color(0.65f, 1f, 0.72f)
                    : preview.estimatedDailyDelta >= 0f ? Color.white
                    : new Color(1f, 0.72f, 0.56f);
                ShopBubbleUtility.ShowTextBubble(pawn, HotelText.T(key), color);
            });
            yield return relax;
        }
    }
}
