using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    public class Mote_ShopPopup : MoteAttached
    {
        private const float BaseIconSize = 1.15f;
        private const float TravelWorldZ = 0.22f;
        private const float HeadOffsetWorldZ = 0.58f;
        private const float MoveDuration = 0.22f;

        private Texture2D icon;
        private Color iconColor = Color.white;
        private Material iconMaterial;

        public void Setup(
            Texture2D popupIcon,
            string popupLabel,
            Color? popupIconColor = null,
            Color? popupLabelColor = null,
            float scale = 1f,
            bool mediumText = false)
        {
            icon = popupIcon;
            iconColor = popupIconColor ?? Color.white;
            iconMaterial = icon != null ? MaterialPool.MatFrom(icon, ShaderDatabase.TransparentPostLight, Color.white) : null;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (Find.UIRoot.HideMotes)
            {
                return;
            }

            float alpha = ComputeDisplayAlpha();
            if (alpha <= 0.001f || icon == null || iconMaterial == null)
            {
                return;
            }

            float rise = Mathf.Lerp(0f, TravelWorldZ, EaseOutCubic(Mathf.Clamp01(AgeSecs / MoveDuration)));
            float scale = Mathf.Lerp(0.92f, 1f, EaseOutCubic(Mathf.Clamp01(AgeSecs / MoveDuration)));

            Vector3 iconDrawPos = exactPosition;
            iconDrawPos.y = def.altitudeLayer.AltitudeFor() + 0.03f;
            iconDrawPos.z += HeadOffsetWorldZ + rise;

            float finalSize = BaseIconSize * scale;
            Vector3 size = new Vector3(finalSize, 1f, finalSize);

            Color drawColor = new Color(iconColor.r, iconColor.g, iconColor.b, alpha);
            Material material = iconMaterial;
            if (material.color != drawColor)
            {
                material = MaterialPool.MatFrom(icon, ShaderDatabase.TransparentPostLight, drawColor);
            }

            Matrix4x4 matrix = default(Matrix4x4);
            matrix.SetTRS(iconDrawPos, Quaternion.identity, size);
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }

        public override void DrawGUIOverlay()
        {
        }

        private static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        private float ComputeDisplayAlpha()
        {
            if (def?.mote == null)
                return Mathf.Clamp01(Alpha);

            float age = AgeSecs;
            float fadeIn = Mathf.Max(0f, def.mote.fadeInTime);
            float solid = Mathf.Max(0f, def.mote.solidTime);
            float fadeOut = Mathf.Max(0f, def.mote.fadeOutTime);
            float total = fadeIn + solid + fadeOut;

            if (total <= 0.001f)
                return 1f;

            if (fadeIn > 0f && age < fadeIn)
                return Mathf.Clamp01(age / fadeIn);

            float fadeOutStart = fadeIn + solid;
            if (fadeOut > 0f && age > fadeOutStart)
                return Mathf.Clamp01(1f - ((age - fadeOutStart) / fadeOut));

            return 1f;
        }
    }
}
