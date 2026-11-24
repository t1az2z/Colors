using System;
using ColorsAction.Level;
using UnityEngine;

namespace ColorsAction.Unity.Editor.LevelEditor
{
    /// <summary>Луч по вокселям (DDA): первый блок на пути и грань, через которую луч в него вошёл.</summary>
    public static class VoxelPicking
    {
        public struct Hit
        {
            public Int3 Cell;
            public Vector3Int Normal;
            public Vector3 Point;
        }

        /// <param name="visible">Учитывать ли клетку (например, скрытые этажи пропускаются).</param>
        public static bool Raycast(VoxelLevel level, Ray ray, float maxDistance, Func<Int3, bool> visible, out Hit hit)
        {
            hit = default;
            var pos = ray.origin;
            var dir = ray.direction.normalized;
            int x = Mathf.FloorToInt(pos.x), y = Mathf.FloorToInt(pos.y), z = Mathf.FloorToInt(pos.z);
            int stepX = dir.x > 0 ? 1 : -1, stepY = dir.y > 0 ? 1 : -1, stepZ = dir.z > 0 ? 1 : -1;
            float tDeltaX = dir.x != 0 ? Mathf.Abs(1f / dir.x) : float.PositiveInfinity;
            float tDeltaY = dir.y != 0 ? Mathf.Abs(1f / dir.y) : float.PositiveInfinity;
            float tDeltaZ = dir.z != 0 ? Mathf.Abs(1f / dir.z) : float.PositiveInfinity;
            float tMaxX = dir.x != 0 ? ((stepX > 0 ? x + 1 - pos.x : pos.x - x) * tDeltaX) : float.PositiveInfinity;
            float tMaxY = dir.y != 0 ? ((stepY > 0 ? y + 1 - pos.y : pos.y - y) * tDeltaY) : float.PositiveInfinity;
            float tMaxZ = dir.z != 0 ? ((stepZ > 0 ? z + 1 - pos.z : pos.z - z) * tDeltaZ) : float.PositiveInfinity;
            var normal = Vector3Int.zero;
            float t = 0;

            while (t <= maxDistance)
            {
                var cell = new Int3(x, y, z);
                if (level.Has(cell) && (visible == null || visible(cell)))
                {
                    hit = new Hit { Cell = cell, Normal = normal, Point = pos + dir * t };
                    return true;
                }

                if (tMaxX < tMaxY && tMaxX < tMaxZ)
                {
                    x += stepX;
                    t = tMaxX;
                    tMaxX += tDeltaX;
                    normal = new Vector3Int(-stepX, 0, 0);
                }
                else if (tMaxY < tMaxZ)
                {
                    y += stepY;
                    t = tMaxY;
                    tMaxY += tDeltaY;
                    normal = new Vector3Int(0, -stepY, 0);
                }
                else
                {
                    z += stepZ;
                    t = tMaxZ;
                    tMaxZ += tDeltaZ;
                    normal = new Vector3Int(0, 0, -stepZ);
                }
            }

            return false;
        }

        /// <summary>Клетка, в которую ставится объект при щелчке по грани блока.</summary>
        public static Int3 PlacementCell(VoxelLevel level, Hit hit)
        {
            if (hit.Normal == Vector3Int.up && level.TryGet(hit.Cell, out var b) && !BlockShapes.FillsCell(b.Shape))
                return hit.Cell;
            return hit.Cell + new Int3(hit.Normal.x, hit.Normal.y, hit.Normal.z);
        }

        /// <summary>Пересечение луча с горизонтальной плоскостью y.</summary>
        public static bool PlaneCell(Ray ray, float y, out int x, out int z, out Vector3 point)
        {
            x = z = 0;
            point = default;
            var plane = new Plane(Vector3.up, new Vector3(0, y, 0));
            if (!plane.Raycast(ray, out var d))
                return false;
            point = ray.GetPoint(d);
            x = Mathf.FloorToInt(point.x);
            z = Mathf.FloorToInt(point.z);
            return true;
        }
    }
}
