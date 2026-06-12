using SimManagementLib.Pojo;
using SimManagementLib.SimDialog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 管理公告联网检查的本局状态，负责成功冷却、失败退避、已读过滤和弹窗触发。
    /// </summary>
    public static class AnnouncementClientState
    {
        private const float SuccessCooldownSeconds = 10f * 60f;
        private const int MaxHistoryRecords = 100;
        private static readonly float[] FailureBackoffSeconds = { 5f * 60f, 15f * 60f, 30f * 60f, 60f * 60f };
        private static Task<List<AnnouncementNetworkItemData>> runningTask;
        private static CancellationTokenSource runningCts;
        private static float nextAllowedRealtime;
        private static int failureCount;
        private static string lastStatusKey = "RSMF.Announcement.Status.Idle";

        /// <summary>
        /// 读取当前公告页状态文本，负责给手动检查按钮旁边提供非错误提示。
        /// </summary>
        public static string StatusText => SimTranslation.TOrFallback(lastStatusKey, "Ready.");

        /// <summary>
        /// 玩家打开经商管理时尝试自动检查公告，负责遵守成功冷却和失败退避。
        /// </summary>
        public static void TryCheckOnBusinessManagerOpen()
        {
            StartCheck(false);
        }

        /// <summary>
        /// 玩家手动点击检查公告时尝试发起请求，负责避免并发请求并遵守退避结束时间。
        /// </summary>
        public static void TryManualCheck()
        {
            StartCheck(true);
        }

        /// <summary>
        /// 轮询已完成的联网任务，负责在主线程写设置并弹出未读公告窗口。
        /// </summary>
        public static void Tick()
        {
            Task<List<AnnouncementNetworkItemData>> task = runningTask;
            if (task == null || !task.IsCompleted)
                return;

            runningTask = null;
            runningCts?.Dispose();
            runningCts = null;

            if (task.IsFaulted || task.IsCanceled)
            {
                RegisterFailure();
                return;
            }

            RegisterSuccess();
            List<AnnouncementNetworkItemData> unread = FilterUnread(task.Result);
            if (unread.Count <= 0)
            {
                lastStatusKey = "RSMF.Announcement.Status.NoNew";
                return;
            }

            MarkAsRead(unread);
            Find.WindowStack.Add(new Dialog_Announcements(unread));
            lastStatusKey = "RSMF.Announcement.Status.NewRead";
        }

        /// <summary>
        /// 判断当前是否正在请求公告，负责让 UI 禁用重复点击。
        /// </summary>
        public static bool IsChecking()
        {
            return runningTask != null && !runningTask.IsCompleted;
        }

        /// <summary>
        /// 返回本机已读公告历史，负责按读取时间倒序供 UI 展示。
        /// </summary>
        public static List<AnnouncementReadRecord> GetReadHistory()
        {
            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            NormalizeHistory(settings);
            return (settings?.announcementReadRecords ?? new List<AnnouncementReadRecord>())
                .OrderByDescending(record => ParseTimeTicks(record?.readAt))
                .ToList();
        }

        /// <summary>
        /// 启动公告检查任务，负责根据自动或手动入口判断是否允许发起请求。
        /// </summary>
        private static void StartCheck(bool manual)
        {
            Tick();
            if (IsChecking())
            {
                lastStatusKey = "RSMF.Announcement.Status.Checking";
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < nextAllowedRealtime)
            {
                lastStatusKey = "RSMF.Announcement.Status.Waiting";
                return;
            }

            runningCts?.Dispose();
            runningCts = new CancellationTokenSource();
            runningTask = AnnouncementNetworkApiClient.GetLatestAsync(runningCts.Token);
            lastStatusKey = "RSMF.Announcement.Status.Checking";
        }

        /// <summary>
        /// 记录请求成功，负责重置失败次数并设置十分钟成功冷却。
        /// </summary>
        private static void RegisterSuccess()
        {
            failureCount = 0;
            nextAllowedRealtime = Time.realtimeSinceStartup + SuccessCooldownSeconds;
        }

        /// <summary>
        /// 记录请求失败，负责按 5/15/30/60 分钟在本局内退避。
        /// </summary>
        private static void RegisterFailure()
        {
            int index = Mathf.Clamp(failureCount, 0, FailureBackoffSeconds.Length - 1);
            nextAllowedRealtime = Time.realtimeSinceStartup + FailureBackoffSeconds[index];
            failureCount++;
            lastStatusKey = "RSMF.Announcement.Status.Waiting";
        }

        /// <summary>
        /// 过滤尚未读过的公告，负责用后端 readKey 识别编辑后的新版本。
        /// </summary>
        private static List<AnnouncementNetworkItemData> FilterUnread(List<AnnouncementNetworkItemData> items)
        {
            List<AnnouncementNetworkItemData> result = new List<AnnouncementNetworkItemData>();
            if (items == null)
                return result;

            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            NormalizeHistory(settings);
            HashSet<string> readKeys = new HashSet<string>((settings?.announcementReadRecords ?? new List<AnnouncementReadRecord>())
                .Where(record => record != null && !string.IsNullOrWhiteSpace(record.readKey))
                .Select(record => record.readKey));

            for (int i = 0; i < items.Count && result.Count < 5; i++)
            {
                AnnouncementNetworkItemData item = items[i];
                item?.Sanitize();
                if (item == null || string.IsNullOrWhiteSpace(item.readKey) || readKeys.Contains(item.readKey))
                    continue;

                result.Add(item);
            }

            return result;
        }

        /// <summary>
        /// 写入已读公告历史，负责弹窗出现后不再重复弹同一 readKey。
        /// </summary>
        private static void MarkAsRead(List<AnnouncementNetworkItemData> unread)
        {
            if (unread.NullOrEmpty())
                return;

            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            if (settings == null)
                return;

            NormalizeHistory(settings);
            HashSet<string> existing = new HashSet<string>(settings.announcementReadRecords
                .Where(record => record != null && !string.IsNullOrWhiteSpace(record.readKey))
                .Select(record => record.readKey));

            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < unread.Count; i++)
            {
                AnnouncementNetworkItemData item = unread[i];
                if (item == null || string.IsNullOrWhiteSpace(item.readKey) || existing.Contains(item.readKey))
                    continue;

                settings.announcementReadRecords.Add(new AnnouncementReadRecord(item, now));
                existing.Add(item.readKey);
            }

            TrimHistory(settings.announcementReadRecords);
            settings.Write();
        }

        /// <summary>
        /// 规范化公告历史列表，负责补齐空列表、移除无效项并限制数量。
        /// </summary>
        public static void NormalizeHistory(SimManagementLibSettings settings)
        {
            if (settings == null)
                return;

            if (settings.announcementReadRecords == null)
                settings.announcementReadRecords = new List<AnnouncementReadRecord>();

            for (int i = settings.announcementReadRecords.Count - 1; i >= 0; i--)
            {
                AnnouncementReadRecord record = settings.announcementReadRecords[i];
                if (record == null || string.IsNullOrWhiteSpace(record.readKey))
                {
                    settings.announcementReadRecords.RemoveAt(i);
                    continue;
                }

                record.Sanitize();
            }

            TrimHistory(settings.announcementReadRecords);
        }

        /// <summary>
        /// 裁剪历史公告数量，负责防止 ModSettings 无限增长。
        /// </summary>
        private static void TrimHistory(List<AnnouncementReadRecord> records)
        {
            if (records == null || records.Count <= MaxHistoryRecords)
                return;

            List<AnnouncementReadRecord> ordered = records
                .OrderByDescending(record => ParseTimeTicks(record?.readAt))
                .Take(MaxHistoryRecords)
                .ToList();
            records.Clear();
            records.AddRange(ordered);
        }

        /// <summary>
        /// 解析 ISO 时间为 ticks，负责给历史列表提供稳定排序值。
        /// </summary>
        private static long ParseTimeTicks(string value)
        {
            if (DateTimeOffset.TryParse(value, out DateTimeOffset parsed))
                return parsed.UtcTicks;

            return 0L;
        }
    }
}
