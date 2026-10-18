using RimWorld;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存单个商店区域的营业开关和二十四小时日程配置。
    /// </summary>
    public class ShopScheduleData : IExposable
    {
        public bool manualOpen = true;
        public bool useSchedule = false;
        public List<bool> openHours = CreateDefaultHours();

        /// <summary>
        /// 读写商店营业日程存档数据，并修正旧存档缺失或长度异常的小时列表。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref manualOpen, "manualOpen", true);
            Scribe_Values.Look(ref useSchedule, "useSchedule", false);
            Scribe_Collections.Look(ref openHours, "openHours", LookMode.Value);
            EnsureValidHours();
        }

        /// <summary>
        /// 判断指定地图当前本地小时是否在营业时间内。
        /// </summary>
        public bool IsOpenNow(Map map)
        {
            if (!manualOpen) return false;
            if (!useSchedule) return true;
            EnsureValidHours();
            int hour = map != null ? GenLocalDate.HourInteger(map) : 0;
            hour = Mathf.Clamp(hour, 0, 23);
            return openHours[hour];
        }

        /// <summary>
        /// 返回当前营业设置的简短状态文本。
        /// </summary>
        public string GetStatusText(Map map)
        {
            if (!manualOpen) return SimTranslation.T("RSMF.ShopSchedule.ManuallyClosed");
            if (!useSchedule) return SimTranslation.T("RSMF.ShopSchedule.OpenAllDay");
            int hour = map != null ? GenLocalDate.HourInteger(map) : 0;
            return IsOpenNow(map)
                ? SimTranslation.T("RSMF.ShopSchedule.OpenAtHour", hour.ToString("00").Named("hour"))
                : SimTranslation.T("RSMF.ShopSchedule.ClosedAtHour", hour.ToString("00").Named("hour"));
        }

        /// <summary>
        /// 返回二十四小时日程的紧凑说明文本。
        /// </summary>
        public string GetScheduleSummary()
        {
            EnsureValidHours();
            if (!useSchedule) return SimTranslation.T("RSMF.ShopSchedule.OpenAllDay");

            StringBuilder sb = new StringBuilder();
            int start = -1;
            for (int i = 0; i <= 24; i++)
            {
                bool open = i < 24 && openHours[i];
                if (open && start < 0)
                {
                    start = i;
                }
                else if (!open && start >= 0)
                {
                    if (sb.Length > 0) sb.Append(SimTranslation.T("RSMF.Common.ListSeparator"));
                    sb.Append(start.ToString("00")).Append(":00-").Append(i.ToString("00")).Append(":00");
                    start = -1;
                }
            }

            return sb.Length > 0 ? sb.ToString() : SimTranslation.T("RSMF.ShopSchedule.NoBusinessHours");
        }

        /// <summary>
        /// 设置指定小时是否营业，并确保小时索引处于有效范围。
        /// </summary>
        public void SetHourOpen(int hour, bool open)
        {
            EnsureValidHours();
            if (hour < 0 || hour >= 24) return;
            openHours[hour] = open;
        }

        /// <summary>
        /// 将全部小时批量设置为营业或停业。
        /// </summary>
        public void SetAllHours(bool open)
        {
            EnsureValidHours();
            for (int i = 0; i < 24; i++)
                openHours[i] = open;
        }

        /// <summary>
        /// 将日程重置为默认的九点到二十一点营业。
        /// </summary>
        public void SetDefaultBusinessHours()
        {
            openHours = CreateDefaultHours();
        }

        /// <summary>
        /// 创建一份独立副本，供 UI 草稿编辑后再写回商店区域。
        /// </summary>
        public ShopScheduleData Clone()
        {
            EnsureValidHours();
            return new ShopScheduleData
            {
                manualOpen = manualOpen,
                useSchedule = useSchedule,
                openHours = new List<bool>(openHours)
            };
        }

        /// <summary>
        /// 用另一份日程数据覆盖当前配置。
        /// </summary>
        public void CopyFrom(ShopScheduleData source)
        {
            if (source == null) return;
            source.EnsureValidHours();
            manualOpen = source.manualOpen;
            useSchedule = source.useSchedule;
            openHours = new List<bool>(source.openHours);
        }

        /// <summary>
        /// 修正小时列表，保证它始终包含二十四个布尔值。
        /// </summary>
        private void EnsureValidHours()
        {
            if (openHours == null)
                openHours = CreateDefaultHours();

            while (openHours.Count < 24)
                openHours.Add(true);

            if (openHours.Count > 24)
                openHours.RemoveRange(24, openHours.Count - 24);
        }

        /// <summary>
        /// 创建默认营业时段，上午九点到晚上九点为营业。
        /// </summary>
        private static List<bool> CreateDefaultHours()
        {
            List<bool> hours = new List<bool>(24);
            for (int i = 0; i < 24; i++)
                hours.Add(i >= 9 && i < 21);
            return hours;
        }
    }
}
