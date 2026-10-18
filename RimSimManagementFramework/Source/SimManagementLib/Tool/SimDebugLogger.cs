using SimManagementLib.SimZone;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Verse;

namespace SimManagementLib.Tool
{
    //类职责：在主线程格式化顾客日志，并把纯字符串批量交给后台文件写入器。
    public static class SimDebugLogger
    {
        private const string LogFolderName = "RimSimManagementFramework";
        private const string LogSubFolderName = "Logs";
        private const string JourneyLogFileName = "journey-debug.log";
        private const int RingCapacity = 256;
        private const int BatchCapacity = 128;
        private static readonly object FileLock = new object();
        private static readonly object RingLock = new object();
        private static readonly ConcurrentQueue<LogWriteRequest> PendingWrites = new ConcurrentQueue<LogWriteRequest>();
        private static readonly Queue<string> RecentLines = new Queue<string>(RingCapacity);
        private static readonly AutoResetEvent WriteSignal = new AutoResetEvent(false);
        private static int workerStarted;
        private static int hasReportedWriteFailure;
        private static string pendingFailureMessage;

        public static bool Enabled => SimManagementLibMod.Settings?.enableJourneyDebugLog ?? false;
        public static string JourneyLogPath => Path.Combine(GetLogDirectory(), JourneyLogFileName);
        public static int PendingCount => PendingWrites.Count;

        //记录带商店对象的行程事件，职责是在主线程读取 RimWorld 上下文并生成不可变字符串。
        public static void Journey(string source, string message, Pawn pawn = null, Zone_Shop shop = null, int orderId = -1)
        {
            if (!Enabled) return;
            ReportPendingFailure();
            EnqueueLine(BuildLine(source, message, pawn, shop, orderId));
        }

        //记录只有商店编号的行程事件，职责是在主线程完成全部游戏对象读取。
        public static void Journey(string source, string message, Pawn pawn, int shopId, int orderId = -1)
        {
            if (!Enabled) return;
            ReportPendingFailure();
            EnqueueLine(BuildLine(source, message, pawn, null, orderId, shopId));
        }

        //返回固定容量内存日志快照，职责是让诊断界面不读取磁盘文件。
        public static List<string> GetRecentLines()
        {
            lock (RingLock)
                return new List<string>(RecentLines);
        }

        //清空行程日志，职责是丢弃尚未写入的旧批次并以 UTF-8 重置文件。
        public static void ClearJourneyLog()
        {
            while (PendingWrites.TryDequeue(out _)) { }
            try
            {
                lock (FileLock)
                {
                    Directory.CreateDirectory(GetLogDirectory());
                    File.WriteAllText(JourneyLogPath, "", new UTF8Encoding(false));
                }
                lock (RingLock) RecentLines.Clear();
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 调试日志清空失败：" + ex.Message);
            }
        }

        //把格式化日志放入内存环形缓冲和后台队列，职责是让游戏线程不执行磁盘 I/O。
        private static void EnqueueLine(string line)
        {
            lock (RingLock)
            {
                while (RecentLines.Count >= RingCapacity) RecentLines.Dequeue();
                RecentLines.Enqueue(line);
            }

            int maxBytes = Math.Max(262144, SimManagementLibMod.Settings?.journeyDebugLogMaxBytes ?? 4194304);
            PendingWrites.Enqueue(new LogWriteRequest(JourneyLogPath, line, maxBytes));
            EnsureWorker();
            WriteSignal.Set();
            if (SimManagementLibMod.Settings?.mirrorJourneyDebugLogToGameLog ?? false)
                Log.Message("[RSMF Journey] " + line);
        }

        //启动唯一后台写入线程，职责是保证线程只接触字符串、并发队列和文件系统。
        private static void EnsureWorker()
        {
            if (Interlocked.CompareExchange(ref workerStarted, 1, 0) != 0) return;
            Thread worker = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "RSMF-JourneyLogWriter"
            };
            worker.Start();
        }

        //持续批量写入日志，职责是完全隔离后台线程与任何 RimWorld 对象。
        private static void WriterLoop()
        {
            while (true)
            {
                WriteSignal.WaitOne(250);
                try
                {
                    FlushBatch();
                }
                catch (Exception ex)
                {
                    pendingFailureMessage = ex.Message;
                }
            }
        }

        //写入一批纯字符串请求，职责是减少文件打开和锁竞争次数。
        private static void FlushBatch()
        {
            if (!PendingWrites.TryDequeue(out LogWriteRequest first)) return;
            StringBuilder batch = new StringBuilder();
            batch.AppendLine(first.Line);
            int count = 1;
            while (count < BatchCapacity && PendingWrites.TryPeek(out LogWriteRequest next) && next.Path == first.Path)
            {
                if (!PendingWrites.TryDequeue(out next)) break;
                batch.AppendLine(next.Line);
                count++;
            }

            lock (FileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(first.Path));
                TrimIfNeeded(first.Path, first.MaxBytes);
                using (FileStream stream = new FileStream(first.Path, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.Write(batch.ToString());
            }
        }

        //在文件超过上限时截断旧内容，职责是避免后台日志无限增长。
        private static void TrimIfNeeded(string path, int maxBytes)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= maxBytes) return;
            File.WriteAllText(path, "日志超过体积上限，后台写入器已重新开始。" + Environment.NewLine, new UTF8Encoding(false));
        }

        //构造日志行，职责是让后台收到的内容不再依赖 Pawn、Map 或区划引用。
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

        //返回日志目录，职责是集中管理框架调试文件位置。
        private static string GetLogDirectory()
        {
            return Path.Combine(GenFilePaths.SaveDataFolderPath, LogFolderName, LogSubFolderName);
        }

        //清理来源文本，职责是避免结构字段包含换行。
        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value.Replace("\r", " ").Replace("\n", " ");
        }

        //在主线程报告后台写入失败，职责是避免后台线程调用游戏日志并限制重复输出。
        private static void ReportPendingFailure()
        {
            string message = pendingFailureMessage;
            if (string.IsNullOrEmpty(message) || Interlocked.CompareExchange(ref hasReportedWriteFailure, 1, 0) != 0) return;
            pendingFailureMessage = null;
            Log.Warning("[SimManagementLib] 调试日志写入失败：" + message);
        }

        //结构职责：携带后台写入所需的纯文件系统数据。
        private readonly struct LogWriteRequest
        {
            public readonly string Path;
            public readonly string Line;
            public readonly int MaxBytes;

            //创建日志写入请求，职责是冻结路径、文本和体积限制。
            public LogWriteRequest(string path, string line, int maxBytes)
            {
                Path = path;
                Line = line;
                MaxBytes = maxBytes;
            }
        }
    }
}
