using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDef
{
    public class CustomerExpressionTagExtension : DefModExtension
    {
        public List<string> expressionTags = new List<string>();
    }

    public class CustomerExpressionSetDef : Def
    {
        public int priority = 0;
        public List<ThingDef> targetRaceDefs = new List<ThingDef>();
        public List<PawnKindDef> targetPawnKinds = new List<PawnKindDef>();
        public List<string> requiredTags = new List<string>();
        public List<string> excludedTags = new List<string>();
        public List<CustomerExpressionEntry> expressions = new List<CustomerExpressionEntry>();

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

        public bool MatchesEvent(string incomingEventId, HashSet<string> tags)
        {
            if (string.IsNullOrEmpty(eventId) || !string.Equals(eventId, incomingEventId)) return false;
            if (!requiredContextTags.NullOrEmpty() && !requiredContextTags.All(tag => tags.Contains(tag))) return false;
            if (!excludedContextTags.NullOrEmpty() && excludedContextTags.Any(tag => tags.Contains(tag))) return false;
            return true;
        }

        public Texture2D ResolveTexture()
        {
            if (textureResolved) return cachedTexture;
            textureResolved = true;
            cachedTexture = TryLoadTextureFromContent(iconTexPath) ?? (iconTexPath.NullOrEmpty() ? null : ContentFinder<Texture2D>.Get(iconTexPath, false));
            return cachedTexture;
        }

        private static Texture2D TryLoadTextureFromContent(string relativePath)
        {
            if (relativePath.NullOrEmpty()) return null;

            string rootDir = SimManagementLib.SimManagementLibMod.ActiveContentPack?.RootDir;
            if (rootDir.NullOrEmpty()) return null;

            string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(rootDir, "Content", "Textures", normalized + ".png");
            if (!File.Exists(fullPath)) return null;

            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                if (bytes == null || bytes.Length == 0) return null;

                Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    Object.Destroy(texture);
                    return null;
                }

                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            catch
            {
                return null;
            }
        }
    }
}
