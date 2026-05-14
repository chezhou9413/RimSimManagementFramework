using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 定义顾客点评生成供应商类型，负责在设置和存档中区分不同大模型接口。
    /// </summary>
    public enum CustomerReviewProvider
    {
        OpenAICompatible,
        Anthropic
    }

    /// <summary>
    /// 定义顾客点评生成状态，负责记录评价是否由模型成功产出。
    /// </summary>
    public enum CustomerReviewGenerationStatus
    {
        Pending,
        Completed,
        Failed
    }

    /// <summary>
    /// 保存点评中展示的商品或套餐摘要，负责在 UI 中还原商品图标和套餐卡片。
    /// </summary>
    public class ReviewFeaturedItem : IExposable
    {
        public string label = "";
        public string defName = "";
        public string lineType = "";
        public int count;
        public float amount;
        public List<string> comboItemDefNames = new List<string>();

        /// <summary>
        /// 将展示商品摘要读写到存档。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label", "");
            Scribe_Values.Look(ref defName, "defName", "");
            Scribe_Values.Look(ref lineType, "lineType", "");
            Scribe_Values.Look(ref count, "count", 0);
            Scribe_Values.Look(ref amount, "amount", 0f);
            Scribe_Collections.Look(ref comboItemDefNames, "comboItemDefNames", LookMode.Value);
            if (comboItemDefNames == null) comboItemDefNames = new List<string>();
        }
    }

    /// <summary>
    /// 保存模型生成后的顾客点评摘要，负责支撑历史展示、店铺均分和存档恢复。
    /// </summary>
    public class CustomerReviewRecord : IExposable
    {
        public string reviewId = "";
        public int tickAbs;
        public int gameDay;
        public int zoneId = -1;
        public string zoneLabel = "";
        public string customerDisplayName = "";
        public string aiNickname = "";
        public int stars;
        public string reviewText = "";
        public int upvotes;
        public int downvotes;
        public string upvoteReviewId = "";
        public string downvoteReviewId = "";
        public string replyToReviewId = "";
        public string replyToNickname = "";
        public string replyStance = "";
        public string replyText = "";
        public float spentSilver;
        public string kindId = "";
        public string kindDescription = "";
        public string raceLabel = "";
        public string raceDescription = "";
        public string ageSummary = "";
        public string backstorySummary = "";
        public string backstoryDetailSummary = "";
        public string traitSummary = "";
        public string xenotypeSummary = "";
        public string geneSummary = "";
        public string personalityBiasSummary = "";
        public string moodSummary = "";
        public string healthSummary = "";
        public string purchasedSummary = "";
        public string serviceSummary = "";
        public string cashierSummary = "";
        public string checkoutJobSummary = "";
        public string postPurchaseSummary = "";
        public string recentReviewContextSummary = "";
        public List<ReviewFeaturedItem> featuredItems = new List<ReviewFeaturedItem>();
        public string avatarImageId = "";
        public CustomerReviewProvider provider = CustomerReviewProvider.OpenAICompatible;
        public CustomerReviewGenerationStatus generationStatus = CustomerReviewGenerationStatus.Completed;

        /// <summary>
        /// 将顾客点评摘要读写到存档。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref reviewId, "reviewId", "");
            Scribe_Values.Look(ref tickAbs, "tickAbs", 0);
            Scribe_Values.Look(ref gameDay, "gameDay", 0);
            Scribe_Values.Look(ref zoneId, "zoneId", -1);
            Scribe_Values.Look(ref zoneLabel, "zoneLabel", "");
            Scribe_Values.Look(ref customerDisplayName, "customerDisplayName", "");
            Scribe_Values.Look(ref aiNickname, "aiNickname", "");
            Scribe_Values.Look(ref stars, "stars", 0);
            Scribe_Values.Look(ref reviewText, "reviewText", "");
            Scribe_Values.Look(ref upvotes, "upvotes", 0);
            Scribe_Values.Look(ref downvotes, "downvotes", 0);
            Scribe_Values.Look(ref upvoteReviewId, "upvoteReviewId", "");
            Scribe_Values.Look(ref downvoteReviewId, "downvoteReviewId", "");
            Scribe_Values.Look(ref replyToReviewId, "replyToReviewId", "");
            Scribe_Values.Look(ref replyToNickname, "replyToNickname", "");
            Scribe_Values.Look(ref replyStance, "replyStance", "");
            Scribe_Values.Look(ref replyText, "replyText", "");
            Scribe_Values.Look(ref spentSilver, "spentSilver", 0f);
            Scribe_Values.Look(ref kindId, "kindId", "");
            Scribe_Values.Look(ref kindDescription, "kindDescription", "");
            Scribe_Values.Look(ref raceLabel, "raceLabel", "");
            Scribe_Values.Look(ref raceDescription, "raceDescription", "");
            Scribe_Values.Look(ref ageSummary, "ageSummary", "");
            Scribe_Values.Look(ref backstorySummary, "backstorySummary", "");
            Scribe_Values.Look(ref backstoryDetailSummary, "backstoryDetailSummary", "");
            Scribe_Values.Look(ref traitSummary, "traitSummary", "");
            Scribe_Values.Look(ref xenotypeSummary, "xenotypeSummary", "");
            Scribe_Values.Look(ref geneSummary, "geneSummary", "");
            Scribe_Values.Look(ref personalityBiasSummary, "personalityBiasSummary", "");
            Scribe_Values.Look(ref moodSummary, "moodSummary", "");
            Scribe_Values.Look(ref healthSummary, "healthSummary", "");
            Scribe_Values.Look(ref purchasedSummary, "purchasedSummary", "");
            Scribe_Values.Look(ref serviceSummary, "serviceSummary", "");
            Scribe_Values.Look(ref cashierSummary, "cashierSummary", "");
            Scribe_Values.Look(ref checkoutJobSummary, "checkoutJobSummary", "");
            Scribe_Values.Look(ref postPurchaseSummary, "postPurchaseSummary", "");
            Scribe_Values.Look(ref recentReviewContextSummary, "recentReviewContextSummary", "");
            Scribe_Collections.Look(ref featuredItems, "featuredItems", LookMode.Deep);
            Scribe_Values.Look(ref avatarImageId, "avatarImageId", "");
            Scribe_Values.Look(ref provider, "provider", CustomerReviewProvider.OpenAICompatible);
            Scribe_Values.Look(ref generationStatus, "generationStatus", CustomerReviewGenerationStatus.Completed);
            if (featuredItems == null) featuredItems = new List<ReviewFeaturedItem>();
        }
    }

    /// <summary>
    /// 保存顾客离店时采集的纯数据快照，负责在后台请求大模型时避免读取游戏对象。
    /// </summary>
    public class CustomerReviewSnapshot : IExposable
    {
        public string reviewId = "";
        public int tickAbs;
        public int gameDay;
        public int zoneId = -1;
        public string zoneLabel = "";
        public string customerDisplayName = "";
        public float spentSilver;
        public string kindId = "";
        public string kindLabel = "";
        public string kindDescription = "";
        public string raceLabel = "";
        public string raceDescription = "";
        public string ageSummary = "";
        public string backstorySummary = "";
        public string backstoryDetailSummary = "";
        public string traitSummary = "";
        public string xenotypeSummary = "";
        public string geneSummary = "";
        public string personalityBiasSummary = "";
        public string moodSummary = "";
        public string healthSummary = "";
        public string budgetSummary = "";
        public string purchasedSummary = "";
        public string serviceSummary = "";
        public string shopEnvironmentSummary = "";
        public string cashierSummary = "";
        public string checkoutJobSummary = "";
        public string postPurchaseSummary = "";
        public string recentReviewContextSummary = "";
        public List<ReviewFeaturedItem> featuredItems = new List<ReviewFeaturedItem>();
        public string avatarImageId = "";

        /// <summary>
        /// 将待生成点评快照读写到存档，确保保存读档后仍能继续后台生成。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref reviewId, "reviewId", "");
            Scribe_Values.Look(ref tickAbs, "tickAbs", 0);
            Scribe_Values.Look(ref gameDay, "gameDay", 0);
            Scribe_Values.Look(ref zoneId, "zoneId", -1);
            Scribe_Values.Look(ref zoneLabel, "zoneLabel", "");
            Scribe_Values.Look(ref customerDisplayName, "customerDisplayName", "");
            Scribe_Values.Look(ref spentSilver, "spentSilver", 0f);
            Scribe_Values.Look(ref kindId, "kindId", "");
            Scribe_Values.Look(ref kindLabel, "kindLabel", "");
            Scribe_Values.Look(ref kindDescription, "kindDescription", "");
            Scribe_Values.Look(ref raceLabel, "raceLabel", "");
            Scribe_Values.Look(ref raceDescription, "raceDescription", "");
            Scribe_Values.Look(ref ageSummary, "ageSummary", "");
            Scribe_Values.Look(ref backstorySummary, "backstorySummary", "");
            Scribe_Values.Look(ref backstoryDetailSummary, "backstoryDetailSummary", "");
            Scribe_Values.Look(ref traitSummary, "traitSummary", "");
            Scribe_Values.Look(ref xenotypeSummary, "xenotypeSummary", "");
            Scribe_Values.Look(ref geneSummary, "geneSummary", "");
            Scribe_Values.Look(ref personalityBiasSummary, "personalityBiasSummary", "");
            Scribe_Values.Look(ref moodSummary, "moodSummary", "");
            Scribe_Values.Look(ref healthSummary, "healthSummary", "");
            Scribe_Values.Look(ref budgetSummary, "budgetSummary", "");
            Scribe_Values.Look(ref purchasedSummary, "purchasedSummary", "");
            Scribe_Values.Look(ref serviceSummary, "serviceSummary", "");
            Scribe_Values.Look(ref shopEnvironmentSummary, "shopEnvironmentSummary", "");
            Scribe_Values.Look(ref cashierSummary, "cashierSummary", "");
            Scribe_Values.Look(ref checkoutJobSummary, "checkoutJobSummary", "");
            Scribe_Values.Look(ref postPurchaseSummary, "postPurchaseSummary", "");
            Scribe_Values.Look(ref recentReviewContextSummary, "recentReviewContextSummary", "");
            Scribe_Collections.Look(ref featuredItems, "featuredItems", LookMode.Deep);
            Scribe_Values.Look(ref avatarImageId, "avatarImageId", "");
            if (featuredItems == null) featuredItems = new List<ReviewFeaturedItem>();
        }
    }

    /// <summary>
    /// 保存模型返回的结构化点评结果，负责在解析后转成可持久化的点评记录。
    /// </summary>
    public class CustomerReviewAiResult
    {
        public string nickname = "";
        public int stars;
        public string reviewText = "";
        public string upvoteReviewId = "";
        public string downvoteReviewId = "";
        public string replyToReviewId = "";
        public string replyText = "";
        public string replyStance = "";
        public List<string> tags = new List<string>();
    }
}
