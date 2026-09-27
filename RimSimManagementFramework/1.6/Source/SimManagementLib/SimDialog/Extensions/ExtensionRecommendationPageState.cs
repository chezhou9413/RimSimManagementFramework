using UnityEngine;

namespace SimManagementLib.SimDialog
{
    //保存推荐页的窗口私有状态，职责是默认展示官方扩展并隔离窗口的选择与滚动。
    internal sealed class ExtensionRecommendationPageState
    {
        public bool official = true;
        public Vector2 scroll;
    }
}
