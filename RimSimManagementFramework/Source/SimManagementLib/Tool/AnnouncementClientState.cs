using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimDialog;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    //管理本地公告状态，职责是在主菜单和经营界面展示未读公告并持久化版本记录。
    public static class AnnouncementClientState
    {
        private const int MaxHistoryRecords = 100;
        private static bool mainMenuChecked;
        private static string lastStatusKey = "RSMF.Announcement.Status.Idle";

        //返回当前检查状态，职责是为公告页面提供本地检查结果。
        public static string StatusText => SimTranslation.T(lastStatusKey);

        //在主菜单就绪后检查一次，职责是等待加载和已有对话框结束而不打断初始化。
        public static void TryCheckOnMainMenu()
        {
            if (mainMenuChecked || Current.ProgramState != ProgramState.Entry
                || LongEventHandler.AnyEventNowOrWaiting || Current.Game != null
                || SimManagementLibMod.Settings == null)
                return;

            if (Find.WindowStack.Windows.Any(window => window.layer == WindowLayer.Dialog && !window.IsDebug))
                return;

            mainMenuChecked = true;
            ShowUnread(true);
        }

        //响应经营管理窗口打开，职责是让游戏内入口同样显示尚未读取的本地公告。
        public static void TryCheckOnBusinessManagerOpen()
        {
            ShowUnread(false);
        }

        //响应手动检查，职责是立即重新读取已加载 Def 而不使用联网冷却。
        public static void TryManualCheck()
        {
            ShowUnread(false);
        }

        //读取并展示本地公告，职责是过滤未启用和已读版本并避免重复打开公告窗口。
        private static void ShowUnread(bool mainMenu)
        {
            if (Find.WindowStack.IsOpen<Dialog_Announcements>())
                return;

            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            NormalizeHistory(settings);
            HashSet<string> readKeys = new HashSet<string>(settings.announcementReadKeys);
            List<AnnouncementItemData> unread = DefDatabase<AnnouncementDef>.AllDefsListForReading
                .Where(def => def.enabled && (!def.mainMenuOnly || mainMenu) && !readKeys.Contains(def.ReadKey))
                .OrderByDescending(def => def.order)
                .ThenByDescending(def => def.publishedAt, StringComparer.Ordinal)
                .ThenBy(def => def.defName, StringComparer.Ordinal)
                .Select(def => new AnnouncementItemData(def))
                .ToList();

            if (unread.Count == 0)
            {
                lastStatusKey = "RSMF.Announcement.Status.NoNew";
                return;
            }

            Find.WindowStack.Add(new Dialog_Announcements(unread));
        }

        //记录已经成功打开的公告，职责是持久化全部版本标识并保存最近的正文快照。
        public static void MarkAsRead(List<AnnouncementItemData> items)
        {
            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            NormalizeHistory(settings);
            HashSet<string> existing = new HashSet<string>(settings.announcementReadKeys);
            DateTime now = DateTime.UtcNow;
            foreach (AnnouncementItemData item in items)
            {
                if (!existing.Add(item.readKey)) continue;
                settings.announcementReadKeys.Add(item.readKey);
                settings.announcementReadRecords.Add(new AnnouncementReadRecord(item, now));
            }

            //历史正文可裁剪，已读标识独立保留，避免大量本地公告在下次启动时反复弹出。
            TrimHistory(settings.announcementReadRecords);
            settings.Write();
            lastStatusKey = "RSMF.Announcement.Status.NewRead";
        }

        //返回本机已读历史，职责是按读取时间倒序提供持久化的公告快照。
        public static List<AnnouncementReadRecord> GetReadHistory()
        {
            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            NormalizeHistory(settings);
            return settings.announcementReadRecords
                .OrderByDescending(record => ParseTimeTicks(record.readAt))
                .ToList();
        }

        //规范化公告持久化数据，职责是清理空项并限制历史正文数量。
        public static void NormalizeHistory(SimManagementLibSettings settings)
        {
            if (settings == null) return;
            if (settings.announcementReadRecords == null)
                settings.announcementReadRecords = new List<AnnouncementReadRecord>();
            if (settings.announcementReadKeys == null)
                settings.announcementReadKeys = new List<string>();

            settings.announcementReadKeys = settings.announcementReadKeys
                .Where(key => !string.IsNullOrWhiteSpace(key)).Distinct().ToList();
            settings.announcementReadRecords.RemoveAll(record => record == null || string.IsNullOrWhiteSpace(record.readKey));
            foreach (AnnouncementReadRecord record in settings.announcementReadRecords)
                record.Sanitize();
            TrimHistory(settings.announcementReadRecords);
        }

        //裁剪历史正文，职责是保留最近一百条快照而不删除对应已读标识。
        private static void TrimHistory(List<AnnouncementReadRecord> records)
        {
            if (records.Count <= MaxHistoryRecords) return;
            List<AnnouncementReadRecord> newest = records.OrderByDescending(record => ParseTimeTicks(record.readAt))
                .Take(MaxHistoryRecords).ToList();
            records.Clear();
            records.AddRange(newest);
        }

        //解析公告读取时间，职责是为历史快照提供稳定的时间排序。
        private static long ParseTimeTicks(string value)
        {
            return DateTimeOffset.TryParse(value, out DateTimeOffset parsed) ? parsed.UtcTicks : 0L;
        }
    }
}
