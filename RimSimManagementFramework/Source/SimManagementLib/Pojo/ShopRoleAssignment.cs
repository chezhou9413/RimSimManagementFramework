using RimWorld;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace SimManagementLib.Pojo
{
    //保存商店岗位的一条员工分配记录与可选的 24 小时排班配置。
    public class ShopRoleAssignment : IExposable
    {
        public string roleDefName;
        public Pawn pawn;
        public int pawnThingId = -1;
        public string pawnLabel = "";
        public string factionLabel = "";
        public bool useShiftSchedule;
        public List<bool> shiftHours;

        //从 Pawn 写入显示快照，让临时员工离开地图后仍能在店员界面被识别。
        public void CapturePawn(Pawn source)
        {
            if (source == null) return;
            pawn = source;
            pawnThingId = source.thingIDNumber;
            pawnLabel = source.LabelShortCap;
            factionLabel = source.Faction?.Name ?? "";
        }

        //返回员工显示名，优先使用当前 Pawn 名称并在引用失效时回退到快照。
        public string DisplayLabel()
        {
            if (pawn != null && !pawn.Destroyed)
                return pawn.LabelShortCap;
            if (!string.IsNullOrEmpty(pawnLabel))
                return pawnLabel;
            if (pawnThingId >= 0)
                return SimTranslation.T("RSMF.Staff.FallbackName", pawnThingId.Named("id"));
            return SimTranslation.T("RSMF.Staff.Unknown");
        }

        //判断记录中的员工是否仍能在指定地图执行店员工作。
        public bool HasUsablePawnOn(Map map)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.Spawned
                && pawn.Map == map;
        }

        //判断记录是否指向指定 Pawn，兼容旧引用和快照编号。
        public bool MatchesPawn(Pawn target)
        {
            if (target == null) return false;
            if (pawn == target) return true;
            return pawnThingId >= 0 && pawnThingId == target.thingIDNumber;
        }

        //确保 24 小时排班数组长度有效。
        public void EnsureShiftHours()
        {
            if (shiftHours == null)
            {
                shiftHours = new List<bool>(24);
                for (int i = 0; i < 24; i++) shiftHours.Add(true);
                return;
            }
            while (shiftHours.Count < 24) shiftHours.Add(true);
            if (shiftHours.Count > 24) shiftHours.RemoveRange(24, shiftHours.Count - 24);
        }

        //判断该员工在指定小时（0~23）是否处于本岗位排班时段内。
        public bool IsOnShiftAtHour(int hour)
        {
            if (!useShiftSchedule) return true;
            if (shiftHours == null || shiftHours.Count < 24) return true;
            int normalized = ((hour % 24) + 24) % 24;
            return shiftHours[normalized];
        }

        //判断该员工当前时刻是否在班，允许附带提前交接窗口。
        public bool IsOnShiftNow(Map map, int leadTicks = 0)
        {
            if (!useShiftSchedule || map == null) return true;
            int dayTick = GenLocalDate.DayTick(map);
            int hour = (dayTick / GenDate.TicksPerHour) % 24;
            if (IsOnShiftAtHour(hour)) return true;
            if (leadTicks <= 0) return false;
            int ticksToNextHour = GenDate.TicksPerHour - (dayTick % GenDate.TicksPerHour);
            return ticksToNextHour <= leadTicks && IsOnShiftAtHour((hour + 1) % 24);
        }

        //判断该员工本班是否即将结束（当前在班、下一小时下班且距离整点不超过提前窗口）。
        public bool IsShiftEndingSoon(Map map, int leadTicks)
        {
            if (!useShiftSchedule || map == null || leadTicks <= 0) return false;
            int dayTick = GenLocalDate.DayTick(map);
            int hour = (dayTick / GenDate.TicksPerHour) % 24;
            if (!IsOnShiftAtHour(hour) || IsOnShiftAtHour((hour + 1) % 24)) return false;
            int ticksToNextHour = GenDate.TicksPerHour - (dayTick % GenDate.TicksPerHour);
            return ticksToNextHour <= leadTicks;
        }

        //切换指定小时的排班状态，首次编辑时自动启用自定义排班。
        public void SetShiftHour(int hour, bool on)
        {
            if (hour < 0 || hour >= 24) return;
            EnsureShiftHours();
            useShiftSchedule = true;
            shiftHours[hour] = on;
        }

        //把班次重置为全天自由（跟随原版日程表）。
        public void ClearShiftSchedule()
        {
            useShiftSchedule = false;
            if (shiftHours != null)
            {
                EnsureShiftHours();
                for (int i = 0; i < 24; i++) shiftHours[i] = true;
            }
        }

        //按起止小时（支持跨午夜，如 22 到 6）设置连续值班段。
        public void SetShiftRange(int startHour, int endHourExclusive)
        {
            EnsureShiftHours();
            useShiftSchedule = true;
            for (int i = 0; i < 24; i++) shiftHours[i] = false;
            int start = ((startHour % 24) + 24) % 24;
            int end = ((endHourExclusive % 24) + 24) % 24;
            if (start == end)
            {
                for (int i = 0; i < 24; i++) shiftHours[i] = true;
                return;
            }
            int cursor = start;
            while (cursor != end)
            {
                shiftHours[cursor] = true;
                cursor = (cursor + 1) % 24;
            }
        }

        //统计当前排班包含的值班小时总数。
        public int CountShiftHours()
        {
            if (!useShiftSchedule) return 24;
            if (shiftHours == null || shiftHours.Count < 24) return 24;
            int count = 0;
            for (int i = 0; i < 24; i++)
                if (shiftHours[i]) count++;
            return count;
        }

        //生成紧凑班次摘要文本，支持跨午夜段合并显示（例如 22:00-06:00）。
        public string GetShiftSummary()
        {
            if (!useShiftSchedule) return SimTranslation.T("RSMF.StaffShift.AllDay");
            EnsureShiftHours();
            int count = CountShiftHours();
            if (count <= 0) return SimTranslation.T("RSMF.StaffShift.OffAllDay");
            if (count >= 24) return SimTranslation.T("RSMF.StaffShift.AllDay");

            bool wrapsMidnight = shiftHours[0] && shiftHours[23];
            int firstOff = 0;
            if (wrapsMidnight)
            {
                while (firstOff < 24 && shiftHours[firstOff]) firstOff++;
            }

            StringBuilder sb = new StringBuilder();
            int start = -1;
            for (int step = 0; step <= 24; step++)
            {
                int hour = (firstOff + step) % 24;
                bool on = step < 24 && shiftHours[hour];
                if (on && start < 0)
                {
                    start = hour;
                }
                else if (!on && start >= 0)
                {
                    if (sb.Length > 0) sb.Append(" / ");
                    sb.Append(start.ToString("00")).Append(":00-").Append(hour.ToString("00")).Append(":00");
                    start = -1;
                }
            }
            return sb.Length > 0 ? sb.ToString() : SimTranslation.T("RSMF.StaffShift.OffAllDay");
        }

        //复制当前分配与排班记录，供商店区域搬迁快照复用。
        public ShopRoleAssignment Clone()
        {
            return new ShopRoleAssignment
            {
                roleDefName = roleDefName,
                pawn = pawn,
                pawnThingId = pawnThingId,
                pawnLabel = pawnLabel,
                factionLabel = factionLabel,
                useShiftSchedule = useShiftSchedule,
                shiftHours = shiftHours != null ? new List<bool>(shiftHours) : null
            };
        }

        //读写岗位分配与排班存档数据。
        public void ExposeData()
        {
            Scribe_Values.Look(ref roleDefName, "roleDefName", "");
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref pawnThingId, "pawnThingId", -1);
            Scribe_Values.Look(ref pawnLabel, "pawnLabel", "");
            Scribe_Values.Look(ref factionLabel, "factionLabel", "");
            Scribe_Values.Look(ref useShiftSchedule, "useShiftSchedule", false);
            Scribe_Collections.Look(ref shiftHours, "shiftHours", LookMode.Value);
            if (useShiftSchedule)
                EnsureShiftHours();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pawn != null)
                CapturePawn(pawn);
        }
    }
}
