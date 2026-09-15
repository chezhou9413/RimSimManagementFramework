using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Buildings.Rendering
{
    //缓存四向连接图集的几何裁剪，职责是让轮廓、透明和蒙版着色器共用相同图块坐标。
    internal static class RestaurantAtlasGeometry
    {
        private static readonly Vector2[][] coordinates = new Vector2[16][];
        private static readonly Mesh[] meshes = new Mesh[16];

        //取得连接图块的四角坐标，职责是裁掉原版四乘四图集每块周围的留白。
        internal static Vector2[] Coordinates(int links)
        {
            if (coordinates[links] != null) return coordinates[links];
            var rect = new Rect(links % 4 * 0.25f + 1f / 32f,
                links / 4 * 0.25f + 1f / 32f, 0.1875f, 0.1875f);
            var uv = new Vector2[4];
            Printer_Plane.GetUVs(rect, out uv[0], out uv[1], out uv[2], out uv[3], false);
            coordinates[links] = uv;
            return uv;
        }

        //取得直接携带裁剪坐标的平面，职责是仅在首次绘制该连接形状时创建网格。
        internal static Mesh MeshFor(int links)
        {
            if (meshes[links] != null) return meshes[links];
            Mesh mesh = MeshMakerPlanes.NewPlaneMesh(1f);
            mesh.name = "餐厅连接图块_" + links;
            mesh.uv = Coordinates(links);
            meshes[links] = mesh;
            return mesh;
        }
    }
}
