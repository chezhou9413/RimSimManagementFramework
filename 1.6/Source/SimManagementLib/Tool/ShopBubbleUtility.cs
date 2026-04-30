using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    public static class ShopBubbleUtility
    {
        private const string DefaultPopupMoteDefName = "Sim_ShopPopupMote";

        public static void ShowThingBubble(Pawn pawn, ThingDef thingDef, Color? iconColor = null, ThingDef moteDef = null)
        {
            ShowThingBubble(pawn, thingDef, null, iconColor, null, moteDef);
        }

        public static void ShowThingBubble(
            Pawn pawn,
            ThingDef thingDef,
            string label,
            Color? iconColor = null,
            Color? labelColor = null,
            ThingDef moteDef = null)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null || thingDef == null) return;

            Texture2D icon = ResolveThingIcon(thingDef);
            if (icon == null) return;

            ShowIconBubble(pawn, icon, label, iconColor, labelColor, moteDef);
        }

        public static void ShowIconBubble(
            Pawn pawn,
            Texture2D icon,
            string label = null,
            Color? iconColor = null,
            Color? labelColor = null,
            ThingDef moteDef = null)
        {
            ShowCustomBubble(pawn, icon, label, iconColor, labelColor, 1f, false, moteDef);
        }

        public static void ShowCustomBubble(
            Pawn pawn,
            Texture2D icon,
            string label = null,
            Color? iconColor = null,
            Color? labelColor = null,
            float popupScale = 1f,
            bool useMediumText = false,
            ThingDef moteDef = null)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null) return;
            if (icon == null && label.NullOrEmpty()) return;

            DestroyExistingBubble(pawn);

            ThingDef popupDef = moteDef ?? DefDatabase<ThingDef>.GetNamedSilentFail(DefaultPopupMoteDefName);
            if (popupDef != null && typeof(Mote_ShopPopup).IsAssignableFrom(popupDef.thingClass))
            {
                Mote_ShopPopup popup = (Mote_ShopPopup)ThingMaker.MakeThing(popupDef);
                popup.Setup(icon, label, iconColor, labelColor, popupScale, useMediumText);
                popup.Attach(pawn);
                popup.exactPosition = pawn.DrawPos;
                GenSpawn.Spawn(popup, pawn.Position, pawn.Map);
                return;
            }

            MoteBubble bubble = (MoteBubble)ThingMaker.MakeThing(moteDef ?? ThingDefOf.Mote_Speech);
            bubble.SetupMoteBubble(icon, null, iconColor);
            bubble.Attach(pawn);
            GenSpawn.Spawn(bubble, pawn.Position, pawn.Map);
        }

        public static void ShowTextBubble(Pawn pawn, string label, Color? labelColor = null, ThingDef moteDef = null)
        {
            ShowTextBubble(pawn, label, labelColor, 1f, false, moteDef);
        }

        public static void ShowTextBubble(
            Pawn pawn,
            string label,
            Color? labelColor,
            float popupScale,
            bool useMediumText,
            ThingDef moteDef = null)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null || label.NullOrEmpty()) return;

            DestroyExistingBubble(pawn);

            ThingDef popupDef = moteDef ?? DefDatabase<ThingDef>.GetNamedSilentFail(DefaultPopupMoteDefName);
            if (popupDef != null && typeof(Mote_ShopPopup).IsAssignableFrom(popupDef.thingClass))
            {
                Mote_ShopPopup popup = (Mote_ShopPopup)ThingMaker.MakeThing(popupDef);
                popup.Setup(null, label, null, labelColor, popupScale, useMediumText);
                popup.Attach(pawn);
                popup.exactPosition = pawn.DrawPos;
                GenSpawn.Spawn(popup, pawn.Position, pawn.Map);
                return;
            }

            MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, label, labelColor ?? Color.white, 3.2f);
        }

        public static void ShowSilverPayment(Pawn pawn, int silverAmount)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null || silverAmount <= 0) return;

            ShowThingBubble(
                pawn,
                ThingDefOf.Silver,
                $"结账 {silverAmount}",
                new Color(1f, 0.95f, 0.55f),
                Color.white);
        }

        private static Texture2D ResolveThingIcon(ThingDef thingDef)
        {
            if (thingDef == null) return null;

            if (thingDef.uiIcon != null)
            {
                return thingDef.uiIcon;
            }

            if (thingDef.graphicData?.Graphic is Graphic graphic && graphic.MatSingle?.mainTexture is Texture2D texture)
            {
                return texture;
            }

            return null;
        }

        private static void DestroyExistingBubble(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null) return;

            IntVec3[] pattern =
            {
                IntVec3.Zero,
                new IntVec3(1, 0, 0),
                new IntVec3(0, 0, 1),
                new IntVec3(1, 0, 1)
            };

            for (int i = 0; i < pattern.Length; i++)
            {
                IntVec3 cell = pawn.Position + pattern[i];
                if (!cell.InBounds(pawn.Map)) continue;

                List<Thing> things = cell.GetThingList(pawn.Map);
                for (int j = things.Count - 1; j >= 0; j--)
                {
                    if (things[j] is Mote mote
                        && mote.link1.Linked
                        && mote.link1.Target.HasThing
                        && mote.link1.Target.Thing == pawn
                        && (mote is MoteBubble || mote is Mote_ShopPopup))
                    {
                        mote.Destroy();
                    }
                }
            }
        }
    }
}
