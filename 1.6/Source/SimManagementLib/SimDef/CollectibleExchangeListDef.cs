using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDef
{
    /// <summary>
    /// 定义收藏品兑换首页和二级商店，负责通过 XML 提供入口、立绘、进度文本和可购买商品。
    /// </summary>
    public class CollectibleExchangeListDef : Def
    {
        // 商店名显示在列表主标题前，用于区分不同收藏品兑换来源。
        [MustTranslate]
        public string shopName = "";

        // 首页入口标题；为空时使用 Def 的 label。
        [MustTranslate]
        public string title = "";

        // 首页入口介绍；为空时使用 Def 的 description。
        [MustTranslate]
        public string intro = "";

        // 优先级越高越靠前；相同优先级按标题和 defName 排序。
        public int priority;

        // 是否在经商管理首页显示该入口。
        public bool visible = true;

        // 商店整体刷新周期，单位为游戏 tick；小于等于 0 时不会自动刷新商品库存。
        public int refreshIntervalTicks;

        // 二级商店左侧立绘贴图路径，位于 Textures 目录下且不包含扩展名。
        public string portraitTexPath = "";

        // 二级商店立绘缩放倍率，用于微调立绘在展示框内的大小。
        public float portraitScale = 1f;

        // 二级商店立绘水平偏移，按展示框宽度归一化，正数向右。
        public float portraitOffsetX;

        // 二级商店立绘垂直偏移，按展示框高度归一化，正数向下。
        public float portraitOffsetY;

        // 二级商店显示的期数或标题标签。
        [MustTranslate]
        public string periodLabel = "";

        // 二级商店显示的物品切换进度说明。
        [MustTranslate]
        public string progressLabel = "";

        // 进入二级商店时随机播放的欢迎文本列表。
        [MustTranslate]
        public List<string> welcomeTexts = new List<string>();

        // 鼠标浏览商品时随机播放的商品介绍或闲聊文本列表。
        [MustTranslate]
        public List<string> browseTexts = new List<string>();

        // 玩家在商店停留一段时间后随机播放的闲聊文本列表。
        [MustTranslate]
        public List<string> idleTexts = new List<string>();

        // 玩家成功购买商品后随机播放的感谢或反馈文本列表。
        [MustTranslate]
        public List<string> purchaseTexts = new List<string>();

        // 玩家货币不足时随机播放的提示文本列表。
        [MustTranslate]
        public List<string> notEnoughCurrencyTexts = new List<string>();

        // 停留文本触发间隔秒数；小于等于 0 时使用默认间隔。
        public float idleTextIntervalSeconds = 12f;

        // 二级商店商品列表；所有商品、价格、货币和数量都由 XML 配置。
        public List<CollectibleExchangeItemEntry> items = new List<CollectibleExchangeItemEntry>();
    }

    /// <summary>
    /// 定义收藏品兑换二级商店中的一个商品，负责提供商品、货币、价格、限购和展示覆盖文本。
    /// </summary>
    public class CollectibleExchangeItemEntry
    {
        // 商品在当前兑换商店内的稳定 ID，用于保存购买次数。
        public string id = "";

        // 购买后发放的物品。
        public ThingDef thingDef;

        // 单次购买需要扣除的货币数量。
        public int price;

        // 单次购买使用的货币物品。
        public ThingDef currencyDef;

        // 存档内最多可购买次数；小于等于 0 时视为不可购买。
        public int maxPurchases;

        // 单次购买发放的物品数量；小于等于 0 时按 1 处理。
        public int count = 1;

        // 是否允许该商品在商店周期刷新时重置已购买次数。
        public bool canRefresh;

        // 商品在周期刷新时恢复库存的概率，运行时会限制到 0~1。
        public float refreshChance = 1f;

        // 自定义商品图标贴图路径；为空时使用 thingDef 默认图标。
        public string iconTexPath = "";

        // 商品名称覆盖文本；为空时使用 thingDef 标签。
        [MustTranslate]
        public string labelOverride = "";

        // 商品说明覆盖文本；为空时使用 thingDef 说明。
        [MustTranslate]
        public string descriptionOverride = "";

        // 自定义图标缓存，避免 IMGUI 每帧重复查找贴图。
        [Unsaved] private Texture2D cachedIcon;

        // 自定义图标是否已经尝试加载。
        [Unsaved] private bool iconResolved;

        /// <summary>
        /// 读取商品稳定 ID，负责在 XML 未配置 id 时回退到物品 defName。
        /// </summary>
        public string StableId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
                return thingDef?.defName ?? "";
            }
        }

        /// <summary>
        /// 读取商品显示名称，负责优先使用 XML 覆盖文本。
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(labelOverride))
                    return labelOverride;
                return thingDef?.LabelCap.RawText ?? "";
            }
        }

        /// <summary>
        /// 读取商品显示说明，负责优先使用 XML 覆盖文本。
        /// </summary>
        public string DisplayDescription
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(descriptionOverride))
                    return descriptionOverride;
                return thingDef?.description ?? "";
            }
        }

        /// <summary>
        /// 读取单次购买发放数量，负责给未配置或配置异常的 XML 提供安全默认值。
        /// </summary>
        public int PurchaseCount
        {
            get
            {
                return count > 0 ? count : 1;
            }
        }

        /// <summary>
        /// 读取商品刷新概率，负责把 XML 中的异常值限制到合法概率范围。
        /// </summary>
        public float RefreshChance
        {
            get
            {
                return Mathf.Clamp01(refreshChance);
            }
        }

        /// <summary>
        /// 读取自定义图标贴图，负责在未配置时返回空让 UI 使用物品默认图标。
        /// </summary>
        public Texture2D ResolveIconTexture()
        {
            if (iconResolved)
                return cachedIcon;

            iconResolved = true;
            cachedIcon = string.IsNullOrWhiteSpace(iconTexPath) ? null : ContentFinder<Texture2D>.Get(iconTexPath, false);
            return cachedIcon;
        }
    }
}
