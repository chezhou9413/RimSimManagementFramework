using UnityEngine;

namespace RimSimRestaurantExtension.UI
{
    //保存总览窗口自身导航状态，职责是隔离共享 Def Worker 的页签、分页与滚动。
    internal sealed class RestaurantOverviewState
    {
        public bool showOrders = true;
        public System.Collections.Generic.HashSet<int> expanded = new System.Collections.Generic.HashSet<int>();
        public int page;
        public Vector2 scroll;
    }
}
