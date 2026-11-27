using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDef
{
    //为种族或角色种类补充表情匹配标签。
    public class CustomerExpressionTagExtension : DefModExtension
    {
        public List<string> expressionTags = new List<string>();
    }

    //按种族和标签选择顾客可使用的表情集。
    public class CustomerExpressionSetDef : Def
    {
        public int priority = 0;
        public List<ThingDef> targetRaceDefs = new List<ThingDef>();
        public List<PawnKindDef> targetPawnKinds = new List<PawnKindDef>();
        public List<string> requiredTags = new List<string>();
        public List<string> excludedTags = new List<string>();
        public List<CustomerExpressionEntry> expressions = new List<CustomerExpressionEntry>();

        //检查顾客是否符合这套表情的种族和标签条件。
        public bool MatchesPawn(Pawn pawn, HashSet<string> tags)
        {
            if (pawn == null) return false;
            if (!targetRaceDefs.NullOrEmpty() && !targetRaceDefs.Contains(pawn.def)) return false;
            if (!targetPawnKinds.NullOrEmpty() && !targetPawnKinds.Contains(pawn.kindDef)) return false;
            if (!requiredTags.NullOrEmpty() && !requiredTags.All(tag => tags.Contains(tag))) return false;
            if (!excludedTags.NullOrEmpty() && excludedTags.Any(tag => tags.Contains(tag))) return false;
            return true;
        }
    }

    //保存一次表情触发的条件、贴图和显示参数。
    public class CustomerExpressionEntry
    {
        public string eventId = string.Empty;
        public float chance = 1f;
        public float weight = 1f;
        public int cooldownTicks = 120;
        public string iconTexPath = string.Empty;
        public Color iconColor = Color.white;
        public float popupScale = 1f;
        public List<string> requiredContextTags = new List<string>();
        public List<string> excludedContextTags = new List<string>();

        [Unsaved] private Texture2D cachedTexture;
        [Unsaved] private bool textureResolved;

        //检查事件及本次动作附带的标签。
        public bool MatchesEvent(string incomingEventId, HashSet<string> tags)
        {
            if (string.IsNullOrEmpty(eventId) || !string.Equals(eventId, incomingEventId)) return false;
            if (!requiredContextTags.NullOrEmpty() && !requiredContextTags.All(tag => tags.Contains(tag))) return false;
            if (!excludedContextTags.NullOrEmpty() && excludedContextTags.Any(tag => tags.Contains(tag))) return false;
            return true;
        }

        //按已启用模组的加载顺序查找表情贴图，并缓存查找结果。
        public Texture2D ResolveTexture()
        {
            if (textureResolved) return cachedTexture;
            textureResolved = true;
            if (!iconTexPath.NullOrEmpty())
                cachedTexture = ContentFinder<Texture2D>.Get(iconTexPath.Replace('\\', '/'), false);
            return cachedTexture;
        }
    }
}
