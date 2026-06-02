using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SimManagementLib.GameComp
{
    /// <summary>
    /// 管理顾客 AI 点评记录和后台生成队列，负责限速、失败统计、存档和店铺评分汇总。
    /// </summary>
    public class GameComponent_CustomerReviewManager : GameComponent
    {
        private const float ForumReplyStrongBonus = 0.18f;
        private const int AvatarCleanupIntervalTicks = 60000;
        private const int AvatarCleanupInitialDelayTicks = 2500;
        private List<CustomerReviewRecord> records = new List<CustomerReviewRecord>();
        private List<CustomerReviewSnapshot> pendingSnapshots = new List<CustomerReviewSnapshot>();
        private int failedCount;
        private int nextAllowedRequestTick;
        private int nextAvatarCleanupTick;
        private bool requestInFlight;
        private Task<CustomerReviewRecord> runningTask;
        private string runningAvatarImageId = "";

        public IReadOnlyList<CustomerReviewRecord> Records => records;
        public int PendingCount => pendingSnapshots?.Count ?? 0;
        public int FailedCount => failedCount;

        /// <summary>
        /// 初始化顾客点评管理组件。
        /// </summary>
        public GameComponent_CustomerReviewManager(Game game)
        {
        }

        /// <summary>
        /// 保存点评历史和待处理队列。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref records, "customerReviewRecords", LookMode.Deep);
            Scribe_Collections.Look(ref pendingSnapshots, "customerReviewPendingSnapshots", LookMode.Deep);
            Scribe_Values.Look(ref failedCount, "customerReviewFailedCount", 0);
            Scribe_Values.Look(ref nextAvatarCleanupTick, "customerReviewNextAvatarCleanupTick", 0);
            if (records == null) records = new List<CustomerReviewRecord>();
            if (pendingSnapshots == null) pendingSnapshots = new List<CustomerReviewSnapshot>();
            if (nextAvatarCleanupTick <= 0)
                nextAvatarCleanupTick = AvatarCleanupInitialDelayTicks;
        }

        /// <summary>
        /// 在游戏主线程推动后台请求队列和已完成结果合并。
        /// </summary>
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            MergeCompletedRecords();
            TrimRecords();
            TryCleanupUnusedAvatars();
            if (!CanStartRequest()) return;

            CustomerReviewSnapshot snapshot = pendingSnapshots[0];
            pendingSnapshots.RemoveAt(0);
            StartRequest(snapshot);
        }

        /// <summary>
        /// 将一条离店顾客快照加入后台生成队列。
        /// </summary>
        public void EnqueueSnapshot(CustomerReviewSnapshot snapshot)
        {
            if (snapshot == null) return;
            snapshot.recentReviewContextSummary = BuildRecentReviewContext(snapshot.zoneId, snapshot.tickAbs);
            pendingSnapshots.Add(snapshot);
        }

        /// <summary>
        /// 返回指定店铺的平均星级和评价数量。
        /// </summary>
        public void GetShopReviewStats(int zoneId, out float averageStars, out int count)
        {
            averageStars = 0f;
            count = 0;
            if (records.NullOrEmpty()) return;

            float total = 0f;
            for (int i = 0; i < records.Count; i++)
            {
                CustomerReviewRecord record = records[i];
                if (record == null || record.zoneId != zoneId || record.stars <= 0 || IsReplyRecord(record)) continue;
                total += record.stars;
                count++;
            }

            if (count > 0)
                averageStars = total / count;
        }

        /// <summary>
        /// 返回全部评价的平均星级。
        /// </summary>
        public float GetOverallAverageStars()
        {
            if (records.NullOrEmpty()) return 0f;
            int count = 0;
            float total = 0f;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] == null || records[i].stars <= 0 || IsReplyRecord(records[i])) continue;
                total += records[i].stars;
                count++;
            }
            return count > 0 ? total / count : 0f;
        }

        /// <summary>
        /// 判断记录是否为论坛回复，负责让店铺均分只统计主帖评价。
        /// </summary>
        private static bool IsReplyRecord(CustomerReviewRecord record)
        {
            return record != null && !string.IsNullOrWhiteSpace(record.replyToReviewId);
        }

        /// <summary>
        /// 判断当前是否允许启动新的点评生成请求。
        /// </summary>
        private bool CanStartRequest()
        {
            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            if (settings == null || !settings.HasValidReviewAiConfig()) return false;
            if (requestInFlight) return false;
            if (pendingSnapshots.NullOrEmpty()) return false;
            return Find.TickManager.TicksGame >= nextAllowedRequestTick;
        }

        /// <summary>
        /// 启动一次后台点评生成请求，并记录正在使用的头像避免清理误删。
        /// </summary>
        private void StartRequest(CustomerReviewSnapshot snapshot)
        {
            SimManagementLibSettings settings = CopySettings(SimManagementLibMod.Settings);
            requestInFlight = true;
            runningAvatarImageId = snapshot?.avatarImageId ?? "";
            int intervalTicks = MathfRoundTicksPerRequest(settings.reviewRequestsPerMinute);
            nextAllowedRequestTick = Find.TickManager.TicksGame + intervalTicks;

            runningTask = StartRequestAsync(snapshot, settings);
        }

        /// <summary>
        /// 在游戏主线程启动异步点评请求，负责兼容 UnityWebRequest 的运行时线程要求。
        /// </summary>
        private static async Task<CustomerReviewRecord> StartRequestAsync(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            try
            {
                CustomerReviewAiResult result = await CustomerReviewAiClient.GenerateReviewAsync(snapshot, settings, CancellationToken.None);
                if (result == null) return null;
                return BuildRecord(snapshot, result, settings.reviewProvider);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 合并后台点评生成结果，并在请求结束后释放正在生成头像的保护引用。
        /// </summary>
        private void MergeCompletedRecords()
        {
            if (!requestInFlight || runningTask == null || !runningTask.IsCompleted) return;

            CustomerReviewRecord record = null;
            if (runningTask.Status == TaskStatus.RanToCompletion)
                record = runningTask.Result;

            if (record == null)
            {
                failedCount++;
            }
            else
            {
                ApplyForumInteractions(record);
                records.Add(record);
            }

            TrimRecords();
            requestInFlight = false;
            runningTask = null;
            runningAvatarImageId = "";
        }

        /// <summary>
        /// 裁剪超出上限的点评记录，头像文件由定期清理统一回收。
        /// </summary>
        private void TrimRecords()
        {
            int max = Math.Max(1, SimManagementLibMod.Settings?.maxReviewRecords ?? 1000);
            while (records.Count > max)
            {
                records.RemoveAt(0);
            }
        }

        /// <summary>
        /// 按固定间隔清理没有被任何点评记录或待处理快照引用的头像文件。
        /// </summary>
        private void TryCleanupUnusedAvatars()
        {
            int ticksGame = Find.TickManager?.TicksGame ?? 0;
            if (ticksGame < nextAvatarCleanupTick)
                return;

            nextAvatarCleanupTick = ticksGame + AvatarCleanupIntervalTicks;
            CustomerReviewAvatarCache.CleanupUnusedAvatars(CollectReferencedAvatarIds());
        }

        /// <summary>
        /// 收集当前仍需要保留的头像编号，覆盖已完成点评、待生成快照和正在生成中的请求。
        /// </summary>
        private IEnumerable<string> CollectReferencedAvatarIds()
        {
            if (records != null)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    string avatarImageId = records[i]?.avatarImageId;
                    if (!string.IsNullOrWhiteSpace(avatarImageId))
                        yield return avatarImageId;
                }
            }

            if (pendingSnapshots != null)
            {
                for (int i = 0; i < pendingSnapshots.Count; i++)
                {
                    string avatarImageId = pendingSnapshots[i]?.avatarImageId;
                    if (!string.IsNullOrWhiteSpace(avatarImageId))
                        yield return avatarImageId;
                }
            }

            if (!string.IsNullOrWhiteSpace(runningAvatarImageId))
                yield return runningAvatarImageId;
        }

        private static CustomerReviewRecord BuildRecord(CustomerReviewSnapshot snapshot, CustomerReviewAiResult result, CustomerReviewProvider provider)
        {
            return new CustomerReviewRecord
            {
                reviewId = snapshot.reviewId,
                tickAbs = snapshot.tickAbs,
                gameDay = snapshot.gameDay,
                zoneId = snapshot.zoneId,
                zoneLabel = snapshot.zoneLabel,
                customerDisplayName = snapshot.customerDisplayName,
                aiNickname = result.nickname,
                stars = NormalizeStars(snapshot, result),
                reviewText = result.reviewText,
                upvoteReviewId = result.upvoteReviewId,
                downvoteReviewId = result.downvoteReviewId,
                replyToReviewId = result.replyToReviewId,
                replyText = result.replyText,
                replyStance = result.replyStance,
                spentSilver = snapshot.spentSilver,
                kindId = snapshot.kindId,
                kindDescription = snapshot.kindDescription,
                raceLabel = snapshot.raceLabel,
                raceDescription = snapshot.raceDescription,
                ageSummary = snapshot.ageSummary,
                backstorySummary = snapshot.backstorySummary,
                backstoryDetailSummary = snapshot.backstoryDetailSummary,
                traitSummary = snapshot.traitSummary,
                xenotypeSummary = snapshot.xenotypeSummary,
                geneSummary = snapshot.geneSummary,
                personalityBiasSummary = snapshot.personalityBiasSummary,
                moodSummary = snapshot.moodSummary,
                healthSummary = snapshot.healthSummary,
                purchasedSummary = snapshot.purchasedSummary,
                serviceSummary = snapshot.serviceSummary,
                cashierSummary = snapshot.cashierSummary,
                checkoutJobSummary = snapshot.checkoutJobSummary,
                postPurchaseSummary = snapshot.postPurchaseSummary,
                roomSummary = snapshot.roomSummary,
                relationSummary = snapshot.relationSummary,
                weatherSummary = snapshot.weatherSummary,
                gameConditionSummary = snapshot.gameConditionSummary,
                colonyWealthSummary = snapshot.colonyWealthSummary,
                colonyShopSummary = snapshot.colonyShopSummary,
                colonyLeaderSummary = snapshot.colonyLeaderSummary,
                colonyCultureSummary = snapshot.colonyCultureSummary,
                recentReviewContextSummary = snapshot.recentReviewContextSummary,
                featuredItems = CloneItems(snapshot.featuredItems),
                avatarImageId = snapshot.avatarImageId,
                provider = provider,
                generationStatus = CustomerReviewGenerationStatus.Completed
            };
        }

        /// <summary>
        /// 校准模型星级，负责只在模型给出中庸 3 星时按明显体验信号轻量打散。
        /// </summary>
        private static int NormalizeStars(CustomerReviewSnapshot snapshot, CustomerReviewAiResult result)
        {
            int stars = Math.Max(1, Math.Min(5, result?.stars ?? 0));
            if (stars != 3 || snapshot == null || result == null)
                return stars;

            int score = 0;
            if (snapshot.spentSilver > 0f) score++;
            if (ContainsAnyReviewSignal(snapshot.purchasedSummary, "没有购买", "没有付款", "没有付款成功", "未付款")) score -= 2;
            if (ContainsAnyReviewSignal(snapshot.serviceSummary, "取消", "失败", "等待过久")) score -= 2;
            if (ContainsAnyReviewSignal(snapshot.budgetSummary, "没有付款", "未付款购物车")) score -= 2;
            if (ContainsAnyReviewSignal(snapshot.personalityBiasSummary, "心情很差", "故意给低分", "差评", "迁怒")) score -= 1;
            if (ContainsAnyReviewSignal(snapshot.healthSummary, "疼痛", "严重度", "伤")) score -= 1;
            if (ContainsAnyReviewSignal(result.reviewText, "满意", "值", "不错", "舒服", "顺", "还会", "救急")) score += 1;
            if (ContainsAnyReviewSignal(result.reviewText, "烦", "坑", "贵", "慢", "烂", "差", "白跑", "不值", "恶心")) score -= 1;

            if (score >= 2) return 4;
            if (score <= -3) return 1;
            if (score <= -1) return 2;
            return 3;
        }

        /// <summary>
        /// 检查星级校准关键词，负责避免散落的字符串判断重复出现。
        /// </summary>
        private static bool ContainsAnyReviewSignal(string text, params string[] words)
        {
            if (string.IsNullOrEmpty(text) || words == null)
                return false;

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (!string.IsNullOrEmpty(word) && text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 应用模型生成的论坛互动，负责把新顾客的点赞或点踩计入被互动的历史评论。
        /// </summary>
        private void ApplyForumInteractions(CustomerReviewRecord record)
        {
            if (record == null || records.NullOrEmpty())
                return;

            NormalizeForumInteractions(record);

            if (!string.IsNullOrWhiteSpace(record.upvoteReviewId))
            {
                CustomerReviewRecord target = records.FirstOrDefault(r => r != null && r.reviewId == record.upvoteReviewId);
                if (target != null) target.upvotes++;
                else record.upvoteReviewId = "";
            }

            if (!string.IsNullOrWhiteSpace(record.downvoteReviewId))
            {
                CustomerReviewRecord target = records.FirstOrDefault(r => r != null && r.reviewId == record.downvoteReviewId);
                if (target != null) target.downvotes++;
                else record.downvoteReviewId = "";
            }

            if (!string.IsNullOrWhiteSpace(record.replyToReviewId))
            {
                CustomerReviewRecord target = records.FirstOrDefault(r => r != null && r.reviewId == record.replyToReviewId);
                if (target != null)
                    record.replyToNickname = target.aiNickname;
                else
                {
                    record.replyToReviewId = "";
                    record.replyToNickname = "";
                    record.replyText = "";
                    record.replyStance = "";
                }
            }
        }

        /// <summary>
        /// 校准论坛互动字段，负责让点赞点踩和回复的出现频率更接近真实论坛。
        /// </summary>
        private void NormalizeForumInteractions(CustomerReviewRecord record)
        {
            if (record == null || records.NullOrEmpty())
                return;

            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            bool hasReply = HasValidReply(record);
            if (!hasReply)
                TryAssignReplyTarget(record, settings);
            hasReply = HasValidReply(record);
            bool keepReply = hasReply && Rand.Value <= GetReplyChance(record, settings);
            if (!keepReply)
                ClearReply(record);

            NormalizeReactionChoice(record);
            bool hasReaction = HasValidReaction(record);
            if (!hasReaction)
                TryAssignReactionTarget(record, settings, keepReply);
            hasReaction = HasValidReaction(record);
            if (!hasReaction) return;

            float reactionChance = GetReactionChance(settings, keepReply);
            if (Rand.Value > reactionChance)
                ClearReaction(record);
        }

        /// <summary>
        /// 尝试为模型写出的回复补充目标，负责避免有回复正文但没有挂到帖子上。
        /// </summary>
        private void TryAssignReplyTarget(CustomerReviewRecord record, SimManagementLibSettings settings)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.replyText) || records.NullOrEmpty())
                return;

            CustomerReviewRecord target = PickInteractionTarget(record, preferOpposite: IsNegativeStance(record), allowReplies: false);
            if (target == null)
                return;

            record.replyToReviewId = target.reviewId;
            if (string.IsNullOrWhiteSpace(record.replyStance))
                record.replyStance = GuessReplyStance(record, target);
        }

        /// <summary>
        /// 尝试补充点赞或点踩目标，负责让论坛互动概率不完全依赖模型填写 ID。
        /// </summary>
        private void TryAssignReactionTarget(CustomerReviewRecord record, SimManagementLibSettings settings, bool keptReply)
        {
            if (record == null || records.NullOrEmpty())
                return;

            bool downvote = record.stars <= 2 || IsNegativeStance(record);
            CustomerReviewRecord target = PickInteractionTarget(record, downvote, allowReplies: true);
            if (target == null)
                return;

            if (downvote)
                record.downvoteReviewId = target.reviewId;
            else
                record.upvoteReviewId = target.reviewId;
        }

        /// <summary>
        /// 选择论坛互动目标，负责优先挑选同店近期主帖并允许多条回复集中到同一帖子。
        /// </summary>
        private CustomerReviewRecord PickInteractionTarget(CustomerReviewRecord record, bool preferOpposite, bool allowReplies)
        {
            List<CustomerReviewRecord> candidates = records
                .Where(r => r != null
                    && !string.IsNullOrWhiteSpace(r.reviewId)
                    && r.reviewId != record.reviewId
                    && (record.zoneId < 0 || r.zoneId == record.zoneId)
                    && (allowReplies || string.IsNullOrWhiteSpace(r.replyToReviewId)))
                .OrderByDescending(r => r.tickAbs)
                .Take(8)
                .ToList();
            if (candidates.Count == 0)
                return null;

            List<CustomerReviewRecord> preferred = preferOpposite
                ? candidates.Where(r => r.stars >= 4).ToList()
                : candidates.Where(r => record.stars >= 4 ? r.stars >= 3 : r.stars <= 3).ToList();
            List<CustomerReviewRecord> pool = preferred.Count > 0 ? preferred : candidates;
            return PickWeightedInteractionTarget(pool);
        }

        /// <summary>
        /// 按论坛活跃度加权选择互动目标，负责让已有讨论的帖子更容易继续收到回复。
        /// </summary>
        private CustomerReviewRecord PickWeightedInteractionTarget(List<CustomerReviewRecord> candidates)
        {
            if (candidates.NullOrEmpty())
                return null;

            List<CustomerReviewRecord> weighted = new List<CustomerReviewRecord>();
            for (int i = 0; i < candidates.Count; i++)
            {
                CustomerReviewRecord candidate = candidates[i];
                if (candidate == null) continue;
                weighted.Add(candidate);
                int replyCount = records.Count(r => r != null && r.replyToReviewId == candidate.reviewId);
                int reactionCount = Math.Min(3, Math.Max(0, candidate.upvotes + candidate.downvotes));
                for (int j = 0; j < Math.Min(3, replyCount) + reactionCount; j++)
                    weighted.Add(candidate);
            }

            return weighted.Count > 0 ? weighted[Rand.Range(0, weighted.Count)] : candidates[Rand.Range(0, candidates.Count)];
        }

        /// <summary>
        /// 判断回复是否偏反对，负责为自动挂载目标和点踩选择提供倾向。
        /// </summary>
        private static bool IsNegativeStance(CustomerReviewRecord record)
        {
            if (record == null)
                return false;

            string stance = record.replyStance ?? "";
            string text = (record.replyText ?? "") + " " + (record.reviewText ?? "");
            return record.stars <= 2 || stance.Contains("反驳") || stance.Contains("吐槽") || text.Contains("不") || text.Contains("烦") || text.Contains("坑");
        }

        /// <summary>
        /// 推断回复立场，负责在模型遗漏立场字段时给 UI 一个自然分类。
        /// </summary>
        private static string GuessReplyStance(CustomerReviewRecord record, CustomerReviewRecord target)
        {
            if (record == null || target == null)
                return "回复";

            if (record.stars <= 2 && target.stars >= 4)
                return "反驳";
            if (record.stars >= 4 && target.stars >= 4)
                return "支持";
            if (record.stars <= 2 && target.stars <= 2)
                return "补充";
            return "吐槽";
        }

        /// <summary>
        /// 判断回复字段是否可用，负责过滤没有目标或没有正文的回复。
        /// </summary>
        private bool HasValidReply(CustomerReviewRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.replyToReviewId) || string.IsNullOrWhiteSpace(record.replyText))
                return false;

            return records.Any(r => r != null && r.reviewId == record.replyToReviewId);
        }

        /// <summary>
        /// 计算回复保留概率，负责让强烈立场比普通评价更容易参与讨论。
        /// </summary>
        private static float GetReplyChance(CustomerReviewRecord record, SimManagementLibSettings settings)
        {
            float baseChance = settings?.reviewForumReplyChance ?? 0.35f;
            if (record == null)
                return baseChance;

            bool strongStars = record.stars <= 2 || record.stars >= 5;
            bool strongStance = !string.IsNullOrWhiteSpace(record.replyStance) && (record.replyStance.Contains("反驳") || record.replyStance.Contains("吐槽"));
            return Mathf.Clamp01(strongStars || strongStance ? baseChance + ForumReplyStrongBonus : baseChance);
        }

        /// <summary>
        /// 计算点赞点踩保留概率，负责让玩家设置能直接控制论坛活跃度。
        /// </summary>
        private static float GetReactionChance(SimManagementLibSettings settings, bool keptReply)
        {
            float baseChance = settings?.reviewForumReactionChance ?? 0.60f;
            return Mathf.Clamp01(baseChance);
        }

        /// <summary>
        /// 清理回复字段，负责在本次顾客不参与回复时保持记录干净。
        /// </summary>
        private static void ClearReply(CustomerReviewRecord record)
        {
            if (record == null)
                return;

            record.replyToReviewId = "";
            record.replyToNickname = "";
            record.replyText = "";
            record.replyStance = "";
        }

        /// <summary>
        /// 校准点赞点踩目标，负责避免同一条评价同时点赞和点踩。
        /// </summary>
        private void NormalizeReactionChoice(CustomerReviewRecord record)
        {
            if (record == null)
                return;

            bool validUpvote = !string.IsNullOrWhiteSpace(record.upvoteReviewId) && records.Any(r => r != null && r.reviewId == record.upvoteReviewId);
            bool validDownvote = !string.IsNullOrWhiteSpace(record.downvoteReviewId) && records.Any(r => r != null && r.reviewId == record.downvoteReviewId);
            if (!validUpvote)
                record.upvoteReviewId = "";
            if (!validDownvote)
                record.downvoteReviewId = "";

            if (!validUpvote || !validDownvote)
                return;

            bool preferDownvote = record.stars <= 2;
            bool preferUpvote = record.stars >= 4;
            if (preferDownvote || (!preferUpvote && Rand.Value < 0.5f))
                record.upvoteReviewId = "";
            else
                record.downvoteReviewId = "";
        }

        /// <summary>
        /// 判断是否存在有效点赞或点踩，负责给概率校准提供统一入口。
        /// </summary>
        private static bool HasValidReaction(CustomerReviewRecord record)
        {
            return record != null && (!string.IsNullOrWhiteSpace(record.upvoteReviewId) || !string.IsNullOrWhiteSpace(record.downvoteReviewId));
        }

        /// <summary>
        /// 清理点赞点踩字段，负责在本次顾客只发帖不互动时保持记录干净。
        /// </summary>
        private static void ClearReaction(CustomerReviewRecord record)
        {
            if (record == null)
                return;

            record.upvoteReviewId = "";
            record.downvoteReviewId = "";
        }

        private static List<ReviewFeaturedItem> CloneItems(List<ReviewFeaturedItem> items)
        {
            List<ReviewFeaturedItem> result = new List<ReviewFeaturedItem>();
            if (items == null) return result;
            for (int i = 0; i < items.Count; i++)
            {
                ReviewFeaturedItem item = items[i];
                if (item == null) continue;
                result.Add(new ReviewFeaturedItem
                {
                    label = item.label,
                    defName = item.defName,
                    lineType = item.lineType,
                    count = item.count,
                    amount = item.amount,
                    comboItemDefNames = item.comboItemDefNames?.ToList() ?? new List<string>()
                });
            }
            return result;
        }

        private static SimManagementLibSettings CopySettings(SimManagementLibSettings source)
        {
            SimManagementLibSettings copy = new SimManagementLibSettings();
            if (source == null) return copy;
            copy.reviewAiEnabled = source.reviewAiEnabled;
            copy.reviewProvider = source.reviewProvider;
            copy.openAiBaseUrl = source.openAiBaseUrl;
            copy.openAiApiKey = source.openAiApiKey;
            copy.openAiModel = source.openAiModel;
            copy.anthropicApiKey = source.anthropicApiKey;
            copy.anthropicModel = source.anthropicModel;
            copy.reviewRequestsPerMinute = source.reviewRequestsPerMinute;
            copy.reviewRequestTimeoutSeconds = source.reviewRequestTimeoutSeconds;
            copy.reviewTemperature = source.reviewTemperature;
            copy.reviewForumReactionChance = source.reviewForumReactionChance;
            copy.reviewForumReplyChance = source.reviewForumReplyChance;
            copy.reviewAbsurdNitpickEnabled = source.reviewAbsurdNitpickEnabled;
            copy.reviewAbsurdNitpickChance = source.reviewAbsurdNitpickChance;
            copy.reviewSystemPrompt = source.reviewSystemPrompt;
            copy.reviewUserPrompt = source.reviewUserPrompt;
            copy.reviewNicknamePrefixes = source.reviewNicknamePrefixes;
            copy.reviewNicknameSuffixes = source.reviewNicknameSuffixes;
            copy.reviewToneWords = source.reviewToneWords;
            copy.reviewPositiveWords = source.reviewPositiveWords;
            copy.reviewNegativeWords = source.reviewNegativeWords;
            copy.reviewBannedWords = source.reviewBannedWords;
            copy.reviewPromptInputFormat = source.reviewPromptInputFormat;
            copy.reviewPromptEnabledNodeIds = source.reviewPromptEnabledNodeIds;
            copy.reviewPromptNodeOrder = source.reviewPromptNodeOrder;
            copy.reviewPromptCustomNodes = source.reviewPromptCustomNodes;
            copy.SanitizeReviewSettingsText();
            return copy;
        }

        /// <summary>
        /// 构造短期评价上下文，负责让新评价知道近期口碑但仍以本次体验为准。
        /// </summary>
        private string BuildRecentReviewContext(int zoneId, int tickAbs)
        {
            if (records.NullOrEmpty()) return "暂无近期评价。";

            List<CustomerReviewRecord> recent = records
                .Where(r => r != null && r.stars > 0 && (zoneId < 0 || r.zoneId == zoneId))
                .OrderByDescending(r => r.tickAbs)
                .Take(6)
                .ToList();

            if (recent.Count == 0) return "暂无同店近期评价。";

            float average = recent.Sum(r => r.stars) / (float)Math.Max(1, recent.Count);
            int low = recent.Count(r => r.stars <= 2);
            int high = recent.Count(r => r.stars >= 4);
            string examples = string.Join("；", recent.Take(4).Select(r => $"reviewId={r.reviewId}, {r.aiNickname}, {r.stars}星, 赞{r.upvotes}/踩{r.downvotes}: {TrimForContext(r.reviewText, 46)}"));
            return $"同店近期 {recent.Count} 条，均分 {average:F1} 星，高分 {high} 条，低分 {low} 条；可互动评论: {examples}。这些只能作为短期口碑背景，本次顾客仍按自己的实际经历客观评价。";
        }

        /// <summary>
        /// 裁剪近期评价文本，负责控制提示上下文长度。
        /// </summary>
        private static string TrimForContext(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return "无文本";
            string cleaned = string.Join(" ", text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return cleaned.Length > maxLength ? cleaned.Substring(0, maxLength) : cleaned;
        }

        private static int MathfRoundTicksPerRequest(int requestsPerMinute)
        {
            int safe = Math.Max(1, requestsPerMinute);
            return Math.Max(60, (int)Math.Round(3600f / safe));
        }
    }
}
