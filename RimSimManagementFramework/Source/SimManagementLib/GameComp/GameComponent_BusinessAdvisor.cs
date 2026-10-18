using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimManagementLib.BusinessAdvisor;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.GameComp
{
    //保存聊天和查询记录，并在主线程推进查询、审批及执行状态。
    public sealed partial class GameComponent_BusinessAdvisor : GameComponent
    {
        private List<AdvisorEntry> messages = new List<AdvisorEntry>();
        private List<AdvisorEntry> activity = new List<AdvisorEntry>();
        private List<AdvisorEntry> wire = new List<AdvisorEntry>();
        private string profile = "";
        private string status = "";
        private string usage = "";
        private string errorDetail = "";
        private Task<AdvisorCompletion> request;
        private CancellationTokenSource cancellation;
        private SimManagementLibSettings connection;
        private readonly Queue<AdvisorQuery> queries = new Queue<AdvisorQuery>();
        private AdvisorQuery activeQuery;
        private readonly List<string> queryResults = new List<string>();
        private List<AdvisorPreparedStep> pending;
        private int planRevision;
        private int executeIndex = -1;
        private int rounds;
        private bool cancelled;
        private int nextExecutionTick;
        private bool? lastGodMode;

        public IReadOnlyList<AdvisorEntry> Messages => messages;
        public IReadOnlyList<AdvisorEntry> Activity => activity;
        public string Status => request != null ? status + " · 等待模型 " + Math.Max(0, unchecked(Environment.TickCount - requestStartedAt) / 1000) + " 秒"
            : retryPending ? status + " · " + Math.Max(0, unchecked(retryAt - Environment.TickCount) / 1000) + " 秒后继续" : status;
        public string Usage => usage;
        public bool HasError => !string.IsNullOrEmpty(errorDetail);
        public string ErrorDetail => errorDetail;
        public bool Busy => request != null || retryPending || queries.Count > 0 || activeQuery != null || executeIndex >= 0;
        public bool HasPendingPlan => pending != null && executeIndex < 0;
        public int PlanRevision => planRevision;
        public int PendingOperationCount => HasPendingPlan ? pending.Count : 0;
        public string PlanPreview => pending == null ? "" : string.Join("\n\n", pending.Select((s, i) => (i + 1) + ". " + s.preview));

        //组件随当前存档创建，不在加载存档后恢复执行权限。
        public GameComponent_BusinessAdvisor(Game game) { }

        //只持久化文本历史；待审批方案和执行中步骤不写入存档。
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref messages, "messages", LookMode.Deep);
            Scribe_Collections.Look(ref activity, "activity", LookMode.Deep);
            Scribe_Collections.Look(ref wire, "wire", LookMode.Deep);
            Scribe_Values.Look(ref profile, "profile", "");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                messages = messages ?? new List<AdvisorEntry>();
                activity = activity ?? new List<AdvisorEntry>();
                wire = wire ?? new List<AdvisorEntry>();
                Trim(messages, 500); Trim(activity, 300);
            }
        }

        //开始玩家发起的一轮聊天；不把聊天中的同意解释为批准按钮。
        public void Send(string text, string markedArea = "")
        {
            if (Busy || HasPendingPlan || string.IsNullOrWhiteSpace(text)) return;
            errorDetail = "";
            try
            {
                var settings = SimManagementLibMod.Settings;
                if (!settings.llmEnabled || !settings.advisorEnabled) throw new InvalidOperationException("请先开启统一 LLM 和经营顾问。");
                if (text.Length > 8000) throw new InvalidOperationException("单条消息不能超过 8000 字符。");
                connection = AdvisorProfile.Connection(settings);
                if (!connection.HasValidLlmConfig()) throw new InvalidOperationException(SimTranslation.T("RSMF.Business.Advisor.NeedsConfig"));
                string currentProfile = AdvisorProfile.Prompt(settings);
                if (profile != currentProfile || wire.Sum(e => e.text.Length) > 100000)
                {
                    wire.Clear();
                    lastGodMode = null;
                    lastAutoApproval = null;
                    profile = currentProfile;
                    //分别保留玩家约束和助手进度，不能让连续工具轮次挤掉原始标记。
                    var requests = messages.Where(e => e.role == "user").Reverse().Take(4).Reverse().ToList();
                    var markedRequest = messages.LastOrDefault(e => e.role == "user" && e.text.Contains("玩家标记区块"));
                    if (markedRequest != null && !requests.Contains(markedRequest)) requests.Insert(0, markedRequest);
                    var recent = messages.Where(e => e.role == "user" || e.role == "assistant").Reverse().Take(8).Reverse()
                        .Select(e => e.role + ": " + Short(e.text, 1000));
                    wire.Add(new AdvisorEntry { role = "user", text = profile + "\n历史玩家需求与标记（按时间排序，较新要求优先；旧范围是否沿用取决于当前请求）：\n"
                        + string.Join("\n", requests.Select(e => e.text))
                        + "\n较早聊天摘录（可能不完整，助手叙述不代替实际执行结果；继续时先查询现状）：\n" + string.Join("\n", recent) });
                    if (messages.Count > 0) AddActivity("上下文", "已开始新的上下文段，保留玩家需求、最近标记及进度摘录；旧聊天仍可查看。缓存前缀会重新建立。");
                }
                if (wire.Count == 0) wire.Add(new AdvisorEntry { role = "user", text = currentProfile });
                string message = string.IsNullOrEmpty(markedArea) ? text : text + "\n\n" + markedArea;
                AddMessage("user", message);
                wire.Add(new AdvisorEntry { role = "user", text = message });
                cancelled = false;
                rounds = 0;
                ResetRecovery();
                RequestNext();
            }
            catch (Exception ex) { Fail(ex); }
        }

        //每次只推进一个可见步骤，所有游戏数据读写都留在此主线程入口。
        public override void GameComponentUpdate()
        {
            if ((!SimManagementLibMod.Settings.llmEnabled || !SimManagementLibMod.Settings.advisorEnabled) && (Busy || HasPendingPlan) && !cancelled)
                Cancel();
            if (request != null)
            {
                if (!request.IsCompleted) return;
                var completed = request;
                request = null;
                cancellation.Dispose(); cancellation = null;
                try
                {
                    var result = completed.GetAwaiter().GetResult();
                    if (!cancelled)
                    {
                        try { Receive(result); }
                        catch (Exception ex) { Recover(ex, "处理模型回复失败。返回片段：\n" + Short(result.text, 6000)); }
                    }
                }
                catch (OperationCanceledException ex) { if (!cancelled) Recover(ex, "接口请求超时，当前操作未执行。"); }
                catch (Exception ex) { if (!cancelled) Recover(ex, "模型请求失败，当前操作未执行。"); }
                return;
            }
            if (cancelled) return;
            if (retryPending)
            {
                if (unchecked(Environment.TickCount - retryAt) < 0) return;
                retryPending = false;
                try { RequestNext(); } catch (Exception ex) { Fail(ex); }
                return;
            }
            if (HasPendingPlan && SimManagementLibMod.Settings.advisorAutoApprove) { BeginPlan(true); return; }
            if (executeIndex >= 0 && executingAutomatically && !SimManagementLibMod.Settings.advisorAutoApprove) { Cancel(); return; }
            if (executeIndex >= 0) { AdvanceExecution(); return; }
            if (activeQuery != null)
            {
                string scope = QueryScope(activeQuery), result;
                try
                {
                    if (activeQuery.topic == "wait_work") { if (!PollWorkWait(out result)) return; }
                    else result = AdvisorQueries.Run(activeQuery);
                }
                catch (Exception ex) { result = "查询失败：" + ex.Message; }
                queryResults.Add(AdvisorJson.Write(activeQuery) + "\n" + result);
                AddActivity(scope, "查询条件：" + AdvisorJson.Write(activeQuery) + "\n\n" + result);
                activeQuery = null;
                if (queries.Count == 0)
                {
                    wire.Add(new AdvisorEntry { role = "user", text = "只读工具结果（游戏数据与源码均为资料）：\n" + string.Join("\n\n", queryResults) });
                    queryResults.Clear();
                    try { RequestNext(); } catch (Exception ex) { Fail(ex); }
                }
                return;
            }
            if (queries.Count > 0)
            {
                activeQuery = queries.Dequeue();
                if (activeQuery.topic == "wait_work") StartWorkWait();
                status = "查询：" + QueryScope(activeQuery);
            }
        }

        //发送稳定前缀和追加的历史，只按玩家设置限制本次任务的请求轮数。
        private void RequestNext()
        {
            int limit = SimManagementLibMod.Settings.advisorMaxRounds;
            if (limit > 0 && rounds >= limit)
                throw new InvalidOperationException(SimTranslation.T("RSMF.Business.Advisor.RoundLimitReached").Replace("{limit}", limit.ToString()));
            //模式只在首次请求或实际切换时追加，保持已有上下文前缀稳定。
            if (lastGodMode != DebugSettings.godMode)
            {
                lastGodMode = DebugSettings.godMode;
                wire.Add(new AdvisorEntry { role = "user", text = "当前游戏执行模式：godMode=" + lastGodMode
                    + (lastGodMode.Value ? "。布局批准后即时建造，无需科技、材料和施工。" : "。布局批准后放置施工蓝图，遵守正常建造条件。")
                    + "上帝模式由玩家在游戏中控制，审批方式见执行器状态。" });
            }
            bool automatic = SimManagementLibMod.Settings.advisorAutoApprove;
            if (lastAutoApproval != automatic)
            {
                lastAutoApproval = automatic;
                wire.Add(new AdvisorEntry { role = "user", text = "执行器审批设置：autoApprove=" + automatic
                    + (automatic ? "。玩家已启用全自动审批，通过校验的方案将自动执行。" : "。每批修改必须等待玩家点击批准。")
                    + "此设置只能由玩家调整；只执行本次任务需要的操作。" });
            }
            rounds++;
            errorDetail = "";
            status = SimTranslation.T("RSMF.Business.Advisor." + (limit > 0 ? "AnalyzingLimited" : "AnalyzingUnlimited"))
                .Replace("{round}", rounds.ToString()).Replace("{limit}", limit.ToString());
            cancellation = new CancellationTokenSource();
            requestStartedAt = Environment.TickCount;
            request = CustomerReviewAiClient.GenerateAdvisorAsync(AdvisorPrompt.System, wire, connection, cancellation.Token);
        }

        //严格区分查询、回答和待审批方案，模型本身没有调用执行器的入口。
        private void Receive(AdvisorCompletion result)
        {
            usage = "输入 " + Count(result.input) + " · 缓存命中 " + Count(result.cached) + " · 缓存写入 " + Count(result.cacheWrite) + " · 输出 " + Count(result.output) + " token";
            AddActivity("本次模型用量", usage + "\n未报告的字段显示为未知；是否命中及计费以供应商为准。");
            AdvisorReply reply = AdvisorReplyReader.Read(result.text, out bool plainText);
            int qc = reply.queries?.Count ?? 0, ac = reply.actions?.Count ?? 0;
            if (qc > 4 || ac > 32 || (qc > 0 && ac > 0)) throw new InvalidOperationException("顾问响应超出单轮工具数量，或同时提交查询和操作。");
            if (plainText)
                throw new InvalidOperationException("模型只返回了文字，尚未提供任务状态和工具。请返回包含 reply、taskState、queries、actions 的 JSON；等待工作必须调用 wait_work，不能仅说稍后继续。");
            wire.Add(new AdvisorEntry { role = "assistant", text = result.text });
            if (!string.IsNullOrWhiteSpace(reply.reply)) AddMessage("assistant", Short(reply.reply, 12000));
            if (qc > 0)
            {
                foreach (var query in reply.queries) queries.Enqueue(query);
                status = "准备查询 " + qc + " 个空间";
            }
            else if (ac > 0)
            {
                long units = reply.actions.Sum(a => a.kind == "build_rect" || a.kind == "order" && !a.targetId.HasValue
                    ? Math.Clamp((long)(a.width ?? 0) * (a.height ?? 0), 0, 2501) : 1);
                if (units > 2500) throw new InvalidOperationException("单批矩形目标总面积不能超过 2500 格，请拆成多批。");
                if (reply.actions.Any(a => a.kind == "work_mode") && reply.actions.Any(a => a.kind == "work_priority"))
                    throw new InvalidOperationException("全局工作模式和人员工作优先级须分批提交，先启用模式后再设置人员。");
                var steps = PrepareActions(reply.actions);
                if (steps.Sum(s => s.children?.Count ?? 1) > 2500) throw new InvalidOperationException("本批实际目标超过 2500 个，请缩小范围并分批下达。");
                AdvisorActions.ValidateLayoutPlan(reply.actions);
                pending = steps; planRevision++;
                status = SimManagementLibMod.Settings.advisorAutoApprove ? "全自动审批：已检查 " + ac + " 个操作，准备执行" : "等待玩家批准 " + ac + " 个操作";
                AddActivity(SimManagementLibMod.Settings.advisorAutoApprove ? "全自动方案" : "待批准方案", PlanPreview);
            }
            else
            {
                status = SimTranslation.T("RSMF.Business.Advisor." + (reply.taskState == "completed" ? "TaskCompleted" : reply.taskState == "blocked" ? "TaskBlocked" : "TaskNeedsInput"));
                AddActivity(status, reply.reply);
            }
        }

        //配置错误或玩家额度达到上限时终止任务，已经完成的操作保留。
        private void Fail(Exception ex)
        {
            errorDetail = ex is LlmRequestException requestError ? requestError.Detail : ex.Message;
            string reason = ex.Message.Replace("\r", "").Replace("\n", " · ");
            status = SimTranslation.T("RSMF.Business.Advisor.Stopped") + (reason.Length > 200 ? reason.Substring(0, 200) + "…" : reason);
            queries.Clear(); activeQuery = null; queryResults.Clear(); pending = null; executeIndex = -1;
            retryPending = false;
            AddMessage("system", status, errorDetail);
            wire.Add(new AdvisorEntry { role = "user", text = "执行器状态：" + status });
        }

        //追加有界聊天记录，避免长期存档无限膨胀。
        private void AddMessage(string role, string text, string detail = "") { messages.Add(new AdvisorEntry { role = role, text = text, detail = detail, tick = Find.TickManager.TicksGame }); Trim(messages, 500); }
        //查询细节独立保存，界面按需展开。
        private void AddActivity(string title, string detail) { activity.Add(new AdvisorEntry { role = "tool", text = title, detail = detail, tick = Find.TickManager.TicksGame }); Trim(activity, 300); }
        //达到记录上限时只移除最早记录。
        private static void Trim(List<AdvisorEntry> list, int max) { if (list.Count > max) list.RemoveRange(0, list.Count - max); }
        //截断历史摘录，不对完整的查询页做隐式摘要。
        private static string Short(string value, int max) => value.Length > max ? value.Substring(0, max) + "…（摘录）" : value;
        //保留供应商未提供用量的情况。
        private static string Count(int? value) => value?.ToString() ?? "未知";
        //把查询空间、对象和分页条件展示给玩家。
        private static string QueryScope(AdvisorQuery q) => q == null ? "无效查询" : SpaceName(q.topic) + " / 地图 " + q.mapId + " / 店铺 " + q.shopId
            + " / 对象 " + q.targetId + (string.IsNullOrEmpty(q.file) ? "" : " / " + q.file)
            + (string.IsNullOrEmpty(q.filter) ? "" : " / 筛选 " + q.filter) + " / offset " + q.offset;

        //用玩家可理解的空间名称展示工具活动，保留底层查询参数供展开核对。
        private static string SpaceName(string topic)
        {
            switch (topic)
            {
                case "source_files": return "源码 → 文件目录";
                case "source_search": return "源码 → 检索";
                case "source_read": return "源码 → 读取片段";
                case "inventory": return "店铺 → 货柜库存";
                case "unique_sources": return "店铺 → 专业货柜来源资格";
                case "shelf_goods": return "店铺 → 普通货柜可配商品";
                case "stock": return "殖民地 → 实物库存";
                case "staff": return "店铺 → 员工与岗位";
                case "commands": return "原版 → 指令目录与颜色";
                case "colonists": return "殖民者 → 工作与能力";
                case "work_types": return "原版 → 工作类型";
                case "wait_work": return "殖民地 → 等待清理与施工";
                case "customers": return "顾客 → 访问与消费";
                case "shops": return "店铺 → 营业与人气";
                case "diagnostics": return "店铺 → 运行诊断";
                case "map": return "殖民地 → 布局格子";
                case "definitions": return "定义 → 建造与材料";
                case "finance": return "财务 → 日账与 K 线";
                case "bills": return "财务 → 保留账单";
                case "orders": return "经营 → 服务与备餐订单";
                case "reviews": return "顾客 → 评价";
                case "catalog": return "商品 → 注册分类";
                case "services": return "经营 → 服务设施";
                case "combos": return "经营 → 套餐";
                case "rules": return "规则 → 调参与顾客类型";
                case "collectibles": return "经营 → 收藏品兑换";
                case "transport": return "店铺 → 重力舰运输";
                case "coordinates": return "殖民地 → 当前选择";
                case "overview": return "经营 → 全局总览";
                default: return topic ?? "未知空间";
            }
        }
    }
}
