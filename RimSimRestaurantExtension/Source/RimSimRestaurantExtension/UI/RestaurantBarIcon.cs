using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //管理餐厅独立图标材质，负责将图标染色蒙版接入原版建筑图标绘制流程。
    public static class RestaurantBarIcon
    {
        //在定义和贴图加载完成后绑定图标材质，供建造菜单和定义图标使用。
        public static void Initialize()
        {
            Bind(DefDatabase<ThingDef>.GetNamed("RSR_BarCounter"));
            Bind(DefDatabase<ThingDef>.GetNamed("RSR_SushiConveyor"));
        }

        //绑定单个建筑的独立图标和染色蒙版。
        private static void Bind(ThingDef bar)
        {
            if (!ShaderDatabase.TryGetUIShader(bar.graphicData.shaderType.Shader, out Shader uiShader))
            {
                Log.Error("[RimSimRestaurantExtension] 建筑图标无法加载支持染色蒙版的 UI Shader：" + bar.defName);
                return;
            }

            //独立图标路径不会自动解析蒙版，因此显式绑定同名的 _m 贴图。
            MaterialRequest request = new MaterialRequest(bar.uiIcon, uiShader, bar.graphicData.color)
            {
                maskTex = ContentFinder<Texture2D>.Get(bar.uiIconPath + "_m"),
                colorTwo = bar.graphicData.colorTwo == Color.white ? bar.uiIconColorTwo : bar.graphicData.colorTwo
            };
            bar.uiIconMaterial = MaterialPool.MatFrom(request);
        }
    }
}
