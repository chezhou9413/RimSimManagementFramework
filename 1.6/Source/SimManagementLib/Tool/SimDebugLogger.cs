using SimManagementLib.SimZone;
using System;
using System.IO;
using System.Text;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供模拟经营框架调试日志写入能力，负责把顾客行程和扩展事件输出到本地文件。
    /// </summary>
    public static class SimDebugLogger
    {
        private const string LogFolderName = "RimSimManagementFramework";
        private const string LogSubFolderName = "Logs";
        private const string JourneyLogFileName = "journey-debug.log";
        private static readonly object FileLock = new object();
        private static bool hasReportedWriteFailure;

        /// <summary>
        /// 返回行程调试日志是否启用。
        /// </summary>
        public static bool Enabled => SimManagementLibMod.Settings?.enableJourneyDebugLog ?? true;

        /// <summary>
        /// 返回行程调试日志文件路径。
        /// </summary>
        public static string JourneyLogPath => Path.Combine(GetLogDirectory(), JourneyLogFileName);

        /// <summary>
        /// 写入顾客行程日志，负责统一追加时间、tick、地图、顾客、店铺和订单上下文。
        /// </summary>
        public static void Journey(string source, string message, Pawn pawn = null, Zone_Shop shop = null, int orderId = -1)
        {
            if (!Enabled) return;
            string line = BuildLine(source, message, pawn, shop, orderId);
            AppendLine(JourneyLogPath, line);
        }

        /// <summary>
        /// 写入顾客行程日志，负责在只有店铺编号时记录上下文。
        /// </summary>
        public static void Journey(string source, string message, Pawn pawn, int shopId, int orderId = -1)
        {
            if (!Enabled) return;
            string line = BuildLine(source, message, pawn, null, orderId, shopId);
            AppendLine(JourneyLogPath, line);
        }

        /// <summary>
        /// 清空行程调试日志，负责在需要重新抓取问题时重置文件。
        /// </summary>
        public static void ClearJourneyLog()
        {
            try
            {
                lock (FileLock)
                {
                    Directory.CreateDirectory(GetLogDirectory());
                    File.WriteAllText(JourneyLogPath, "", Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                ReportWriteFailure(ex);
            }
        }

        /// <summary>
        /// 返回日志目录，负责集中管理框架调试文件位置。
        /// </summary>
        private static string GetLogDirectory()
        {
            return Path.Combine(GenFilePaths.SaveDataFolderPath, LogFolderName, LogSubFolderName);
        }

        /// <summary>
        /// 构造日志行文本，负责让每条记录都能独立定位场景。
        /// </summary>
        private static string BuildLine(string source, string message, Pawn pawn, Zone_Shop shop, int orderId, int fallbackShopId = -1)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string mapText = pawn?.Map != null ? $"map={pawn.Map.uniqueID}" : "map=-";
            string pawnText = pawn != null ? $"pawn={pawn.LabelShortCap}/{pawn.thingIDNumber}" : "pawn=-";
            int shopId = shop?.ID ?? fallbackShopId;
            string shopText = shopId >= 0 ? $"shop={shopId}" : "shop=-";
            string orderText = orderId >= 0 ? $"order={orderId}" : "order=-";
            return $"{time} tick={tick} source={Safe(source)} {mapText} {pawnText} {shopText} {orderText} {message ?? ""}";
        }

        /// <summary>
        /// 追加写入日志行，负责控制文件体积和 UTF-8 编码。
        /// </summary>
        private static void AppendLine(string path, string line)
        {
            try
            {
                lock (FileLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    TrimIfNeeded(path);
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
                if (SimManagementLibMod.Settings?.mirrorJourneyDebugLogToGameLog ?? false)
                    Log.Message("[RSMF Journey] " + line);
            }
            catch (Exception ex)
            {
                ReportWriteFailure(ex);
            }
        }

        /// <summary>
        /// 在文件过大时截断旧内容，负责避免调试日志无限增长。
        /// </summary>
        private static void TrimIfNeeded(string path)
        {
            int maxBytes = Math.Max(1024 * 256, SimManagementLibMod.Settings?.journeyDebugLogMaxBytes ?? 4 * 1024 * 1024);
            if (!File.Exists(path)) return;
            FileInfo info = new FileInfo(path);
            if (info.Length <= maxBytes) return;
            File.WriteAllText(path, $"日志超过 {maxBytes} 字节，已从 tick {Find.TickManager?.TicksGame ?? 0} 重新开始。{Environment.NewLine}", Encoding.UTF8);
        }

        /// <summary>
        /// 清理来源文本，负责避免日志字段中出现换行。
        /// </summary>
        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            return value.Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>
        /// 报告日志写入失败，负责避免同一异常反复刷屏。
        /// </summary>
        private static void ReportWriteFailure(Exception ex)
        {
            if (hasReportedWriteFailure) return;
            hasReportedWriteFailure = true;
            Log.Warning("[SimManagementLib] 调试日志写入失败：" + ex.Message);
        }
    }
}
