using System.Collections.Generic;
using RimSimRestaurantExtension.GameComp;
using UnityEngine;

namespace RimSimRestaurantExtension.UI
{
    //保存单个窗口的餐厅草稿，职责是隔离共享 Worker 的编辑、选中、滚动和短期查询状态。
    internal sealed class RestaurantPageState
    {
        public RestaurantShopSettings draft;
        public string selectedId = "";
        public Vector2 scroll;
        public readonly Dictionary<string, (int tick, string issue)> menuStatus = new Dictionary<string, (int, string)>();
    }
}
