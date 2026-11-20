using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Tool
{
    //提供岗位 24 小时一键倒班分配、覆盖与空窗统计、原版作息冲突检测与同步工具。
    public static class ShopStaffShiftUtility
    {
        //将指定岗位当前可用员工按人数均分 24 小时，形成连续无空窗的倒班表。
        public static int AutoDistributeShifts(Zone_Shop zone, string roleDefName)
        {
            if (zone?.Map == null || string.IsNullOrEmpty(roleDefName)) return 0;
            List<ShopRoleAssignment> usable = zone.GetAssignedPawnRecords(roleDefName, false);
            int count = usable.Count;
            if (count <= 0) return 0;

            if (count == 1)
            {
                usable[0].ClearShiftSchedule();
                return 1;
            }
            if (count == 2)
            {
                usable[0].SetShiftRange(8, 20);
                usable[1].SetShiftRange(20, 8);
                return 2;
            }
            if (count == 3)
            {
                usable[0].SetShiftRange(6, 14);
                usable[1].SetShiftRange(14, 22);
                usable[2].SetShiftRange(22, 6);
                return 3;
            }

            int startHour = 6;
            for (int i = 0; i < count; i++)
            {
                int from = (startHour + i * 24 / count) % 24;
                int to = (startHour + (i + 1) * 24 / count) % 24;
                if (from == to)
                    usable[i].SetShiftRange(from, (from + 1) % 24);
                else
                    usable[i].SetShiftRange(from, to);
            }
            return count;
        }

        //按店铺营业时间将可用员工均分排班；全天营业时退化为 24 小时均分倒班。
        public static int AutoDistributeOpenHoursShifts(Zone_Shop zone, string roleDefName)
        {
            if (zone?.Map == null || string.IsNullOrEmpty(roleDefName)) return 0;
            ShopScheduleData schedule = zone.GetSchedule();
            if (schedule == null || !schedule.useSchedule)
                return AutoDistributeShifts(zone, roleDefName);

            List<int> openHours = new List<int>(24);
            for (int h = 0; h < 24; h++)
            {
                if (IsShopScheduledOpenAtHour(schedule, h))
                    openHours.Add(h);
            }
            if (openHours.Count == 0 || openHours.Count == 24)
                return AutoDistributeShifts(zone, roleDefName);

            List<ShopRoleAssignment> usable = zone.GetAssignedPawnRecords(roleDefName, false);
            int count = usable.Count;
            if (count <= 0) return 0;

            for (int i = 0; i < count; i++)
            {
                ShopRoleAssignment record = usable[i];
                record.EnsureShiftHours();
                record.useShiftSchedule = true;
                for (int h = 0; h < 24; h++) record.shiftHours[h] = false;

                int fromIdx = i * openHours.Count / count;
                int toIdx = (i + 1) * openHours.Count / count;
                if (toIdx <= fromIdx) toIdx = System.Math.Min(openHours.Count, fromIdx + 1);
                for (int k = fromIdx; k < toIdx; k++)
                    record.shiftHours[openHours[k]] = true;
            }
            return count;
        }

        //将指定岗位全部员工重置为全天模式。
        public static void ClearRoleShifts(Zone_Shop zone, string roleDefName)
        {
            if (zone == null || string.IsNullOrEmpty(roleDefName)) return;
            List<ShopRoleAssignment> records = zone.GetAssignedPawnRecords(roleDefName, true);
            for (int i = 0; i < records.Count; i++)
                records[i].ClearShiftSchedule();
        }

        //统计指定岗位每小时（0~23）已排班且当前地图可用的员工人数。
        public static void CountHourlyCoverage(Zone_Shop zone, string roleDefName, int[] hourlyCounts)
        {
            if (hourlyCounts == null || hourlyCounts.Length < 24) return;
            for (int h = 0; h < 24; h++) hourlyCounts[h] = 0;
            if (zone?.Map == null || string.IsNullOrEmpty(roleDefName)) return;

            List<ShopRoleAssignment> usable = zone.GetAssignedPawnRecords(roleDefName, false);
            for (int i = 0; i < usable.Count; i++)
            {
                ShopRoleAssignment record = usable[i];
                for (int h = 0; h < 24; h++)
                {
                    if (record.IsOnShiftAtHour(h))
                        hourlyCounts[h]++;
                }
            }
        }

        //统计岗位在店铺计划营业时间内的覆盖小时与空窗小时数。
        public static int CountRoleOpenGapHours(Zone_Shop zone, string roleDefName, int[] hourlyCounts, out int coveredHours, out int openHours)
        {
            coveredHours = 0;
            openHours = 0;
            int gapHours = 0;
            CountHourlyCoverage(zone, roleDefName, hourlyCounts);
            ShopScheduleData schedule = zone?.GetSchedule();
            for (int h = 0; h < 24; h++)
            {
                bool isOpen = IsShopScheduledOpenAtHour(schedule, h);
                if (isOpen) openHours++;
                if (hourlyCounts[h] > 0) coveredHours++;
                else if (isOpen) gapHours++;
            }
            return gapHours;
        }

        //判断店铺日程在指定小时是否计划营业。
        public static bool IsShopScheduledOpenAtHour(ShopScheduleData schedule, int hour)
        {
            if (schedule == null) return true;
            if (!schedule.manualOpen) return false;
            if (!schedule.useSchedule) return true;
            return schedule.openHours != null && hour >= 0 && hour < schedule.openHours.Count && schedule.openHours[hour];
        }

        //返回殖民者指定小时的原版日程安排，用于排班表上叠加作息色条与冲突检测。
        public static TimeAssignmentDef GetColonistTimetableAtHour(Pawn pawn, int hour)
        {
            if (pawn?.timetable == null || !pawn.IsColonist || pawn.IsPrisonerOfColony || hour < 0 || hour >= 24)
                return null;
            return pawn.timetable.GetAssignment(hour);
        }

        //判断殖民者在指定小时的排班是否与原版睡眠、娱乐或冥想作息冲突。
        public static bool IsTimetableOffDuty(TimeAssignmentDef assignment)
        {
            return assignment == TimeAssignmentDefOf.Sleep
                || assignment == TimeAssignmentDefOf.Joy
                || assignment == TimeAssignmentDefOf.Meditate;
        }

        //统计指定员工排班中与原版休息/娱乐/冥想冲突的小时数。
        public static int CountTimetableConflicts(ShopRoleAssignment record)
        {
            if (record?.pawn == null || record.pawn.Destroyed || !record.useShiftSchedule)
                return 0;
            if (record.pawn.timetable == null || !record.pawn.IsColonist || record.pawn.IsPrisonerOfColony)
                return 0;

            int conflicts = 0;
            for (int h = 0; h < 24; h++)
            {
                if (record.IsOnShiftAtHour(h) && IsTimetableOffDuty(record.pawn.timetable.GetAssignment(h)))
                    conflicts++;
            }
            return conflicts;
        }

        //返回指定小时在岗的员工姓名列表，供排班覆盖条悬停提示使用。
        public static string FormatHourlyStaffNames(Zone_Shop zone, string roleDefName, int hour)
        {
            if (zone?.Map == null || string.IsNullOrEmpty(roleDefName)) return "";
            List<ShopRoleAssignment> usable = zone.GetAssignedPawnRecords(roleDefName, false);
            List<string> names = null;
            for (int i = 0; i < usable.Count; i++)
            {
                if (!usable[i].IsOnShiftAtHour(hour)) continue;
                names ??= new List<string>();
                names.Add(usable[i].DisplayLabel());
            }
            return names == null ? "" : string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), names);
        }

        //批量把指定岗位所有启用自定义排班的殖民者作息表同步为错峰工作、睡眠和娱乐。
        public static int SyncRoleTimetables(Zone_Shop zone, string roleDefName)
        {
            if (zone?.Map == null || string.IsNullOrEmpty(roleDefName)) return 0;
            List<ShopRoleAssignment> usable = zone.GetAssignedPawnRecords(roleDefName, false);
            int synced = 0;
            for (int i = 0; i < usable.Count; i++)
            {
                if (SyncColonistTimetableToShifts(usable[i].pawn, zone.Map))
                    synced++;
            }
            return synced;
        }

        //汇总殖民者在本地图所有商店岗位的排班小时并集，自动将其原版作息表对齐为上班工作、最长空档睡眠加娱乐。
        public static bool SyncColonistTimetableToShifts(Pawn pawn, Map map)
        {
            if (pawn?.timetable == null || !pawn.IsColonist || pawn.IsPrisonerOfColony || map?.zoneManager == null)
                return false;

            bool[] dutyHours = new bool[24];
            bool hasScheduledShift = false;
            List<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (!(zones[i] is Zone_Shop shop)) continue;
                shop.CollectScheduledShiftHoursForPawn(pawn, dutyHours, ref hasScheduledShift);
            }
            if (!hasScheduledShift)
                return false;

            for (int h = 0; h < 24; h++)
                pawn.timetable.SetAssignment(h, dutyHours[h] ? TimeAssignmentDefOf.Work : TimeAssignmentDefOf.Anything);

            FindLongestOffSegment(dutyHours, out int segStart, out int segLen);
            if (segLen <= 0)
                return true;

            int sleepHours = segLen >= 11 ? 8 : (segLen >= 9 ? 7 : (segLen >= 6 ? segLen - 2 : segLen));
            int postShiftBuffer = segLen >= sleepHours + 3 ? 1 : 0;
            int sleepStart = (segStart + postShiftBuffer) % 24;
            for (int i = 0; i < sleepHours; i++)
                pawn.timetable.SetAssignment((sleepStart + i) % 24, TimeAssignmentDefOf.Sleep);

            int remainingAfterSleep = segLen - postShiftBuffer - sleepHours;
            int joyHours = remainingAfterSleep >= 2 ? 2 : (remainingAfterSleep == 1 ? 1 : 0);
            int joyStart = (sleepStart + sleepHours) % 24;
            for (int i = 0; i < joyHours; i++)
                pawn.timetable.SetAssignment((joyStart + i) % 24, TimeAssignmentDefOf.Joy);

            return true;
        }

        //在环形 24 小时数组中查找最长的连续非值班时段起点与长度。
        private static void FindLongestOffSegment(bool[] dutyHours, out int bestStart, out int bestLen)
        {
            bestStart = 0;
            bestLen = 0;
            int curStart = -1;
            int curLen = 0;
            for (int i = 0; i < 48; i++)
            {
                int h = i % 24;
                if (!dutyHours[h])
                {
                    if (curStart < 0) curStart = h;
                    curLen++;
                    if (curLen > bestLen)
                    {
                        bestLen = curLen > 24 ? 24 : curLen;
                        bestStart = curStart;
                    }
                }
                else
                {
                    curStart = -1;
                    curLen = 0;
                }
            }
        }
    }
}
