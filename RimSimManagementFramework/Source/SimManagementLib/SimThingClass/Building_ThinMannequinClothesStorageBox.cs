using RimWorld;
using UnityEngine;

namespace SimManagementLib.SimThingClass
{
    //瘦体型服装假人，职责是使用原版 Thin 体型校验并展示衣服。
    public sealed class Building_ThinMannequinClothesStorageBox : Building_MannequinClothesStorageBox
    {
        protected override BodyTypeDef MannequinBodyType => BodyTypeDefOf.Thin;
        protected override float BodyDisplayScale => 1.4f;
        protected override Vector2 BodyDisplayOffset => Vector2.zero;
        protected override float HeadgearDisplayScale => 1.4f;
        protected override Vector2 HeadgearDisplayOffset => Vector2.zero;
    }
}
