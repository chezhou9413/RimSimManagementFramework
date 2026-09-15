using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Rendering
{
    //保存主线程加载的共享像素源，职责是让所有连接形状复用一次贴图读取。
    [StaticConstructorOnStartup]
    internal static class ConveyorTextureSources
    {
        private const string Root = "Things/Building/Restaurant/SushiConveyor/";
        internal static readonly ConveyorPixelBuffer Bottom, Region, Dye;
        internal static readonly ConveyorPixelBuffer[] Animation = new ConveyorPixelBuffer[3];

        //在启动加载阶段采集源图，职责是避免首次放置时执行显卡纹理回读。
        static ConveyorTextureSources()
        {
            Bottom = Read("RSR_SushiConveyor_BaseAtlas");
            Region = Read("Masks/RSR_SushiConveyor_BeltRegion");
            Dye = Read("RSR_SushiConveyor_Atlas_m");
            for (int i = 0; i < Animation.Length; i++)
                Animation[i] = Read("Animation/RSR_SushiConveyor_Belt_Frame0" + (i + 1));
        }

        //读取一张已加载的源图，职责是统一素材路径和缺失错误。
        private static ConveyorPixelBuffer Read(string name)
        {
            return new ConveyorPixelBuffer(ContentFinder<Texture2D>.Get(Root + name));
        }
    }
}
