using System;
using System.Collections.Generic;
using ColorsAction.Level;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColorsAction.Unity.Voxel
{
    /// <summary>
    /// Собирает меш куска воксельного уровня: грани, закрытые соседним полным кубом того же этажа, отбрасываются.
    /// Каждому цвету — свой сабмеш (порядок в <see cref="Result.Colors"/>). UV в мировых метрах, как у LevelBuilder.
    /// </summary>
    public static class VoxelMesher
    {
        public sealed class Result
        {
            public Mesh Mesh;

            /// <summary>Id цвета сабмеша; null — цвет мира.</summary>
            public readonly List<string> Colors = new List<string>();
        }

        private sealed class Buffer
        {
            public readonly List<int> Triangles = new List<int>();
        }

        private static readonly List<Vector3> Verts = new List<Vector3>();
        private static readonly List<Vector3> Normals = new List<Vector3>();
        private static readonly List<Vector2> Uvs = new List<Vector2>();

        /// <param name="cells">Клетки куска.</param>
        /// <param name="sameBand">Лежит ли соседняя клетка в том же этаже (грани между этажами не отсекаются).</param>
        public static Result Build(VoxelLevel level, IEnumerable<Int3> cells, Func<Int3, bool> sameBand, string name)
        {
            Verts.Clear();
            Normals.Clear();
            Uvs.Clear();
            var buffers = new Dictionary<string, Buffer>();
            var order = new List<string>();

            foreach (var cell in cells)
            {
                if (!level.TryGet(cell, out var block))
                    continue;
                var key = block.Color ?? string.Empty;
                if (!buffers.TryGetValue(key, out var buffer))
                {
                    buffers[key] = buffer = new Buffer();
                    order.Add(key);
                }

                AddBlock(level, cell, block, sameBand, buffer.Triangles);
            }

            if (Verts.Count == 0)
                return null;

            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(Verts);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.subMeshCount = order.Count;
            var result = new Result { Mesh = mesh };
            for (int i = 0; i < order.Count; i++)
            {
                mesh.SetTriangles(buffers[order[i]].Triangles, i);
                result.Colors.Add(order[i].Length == 0 ? null : order[i]);
            }

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return result;
        }

        // ------------------------------------------------------------------ формы

        private static bool Hides(VoxelLevel level, Int3 neighbour, Func<Int3, bool> sameBand, float height)
        {
            if (!level.TryGet(neighbour, out var b) || !sameBand(neighbour))
                return false;
            if (BlockShapes.FillsCell(b.Shape))
                return true;
            // Соседний «плоский» блок не ниже этой грани тоже её закрывает (два полублока рядом).
            return (b.Shape == BlockShape.Half || b.Shape == BlockShape.Step) && BlockShapes.CenterTop(b.Shape) >= height - 1e-4f;
        }

        private static void AddBlock(VoxelLevel level, Int3 c, Block block, Func<Int3, bool> sameBand, List<int> tris)
        {
            var o = new Vector3(c.X, c.Y, c.Z);
            switch (block.Shape)
            {
                case BlockShape.Decor:
                    AddBox(o + new Vector3(0.2f, 0f, 0.2f), new Vector3(0.6f, 0.6f, 0.6f), null, tris);
                    return;
                case BlockShape.Full:
                case BlockShape.Half:
                case BlockShape.Step:
                {
                    var h = BlockShapes.CenterTop(block.Shape);
                    AddBox(o, new Vector3(1, h, 1), dir =>
                    {
                        if (dir == Vector3Int.up)
                            return h >= 1f && Hides(level, c.Above, sameBand, 1f);
                        if (dir == Vector3Int.down)
                            return Hides(level, c.Below, sameBand, 1f) && level.TryGet(c.Below, out var below) &&
                                   BlockShapes.FillsCell(below.Shape);
                        return Hides(level, c + new Int3(dir.x, 0, dir.z), sameBand, h);
                    }, tris);
                    return;
                }
                default:
                    AddSlope(level, c, block, sameBand, tris);
                    return;
            }
        }

        private static readonly Vector3Int[] Dirs =
            { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right, new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1) };

        private static void AddBox(Vector3 o, Vector3 size, Func<Vector3Int, bool> hidden, List<int> tris)
        {
            float x0 = o.x, y0 = o.y, z0 = o.z, x1 = o.x + size.x, y1 = o.y + size.y, z1 = o.z + size.z;
            foreach (var d in Dirs)
            {
                if (hidden != null && hidden(d))
                    continue;
                Vector3 a, b, cc, e;
                if (d == Vector3Int.up) { a = new Vector3(x0, y1, z0); b = new Vector3(x0, y1, z1); cc = new Vector3(x1, y1, z1); e = new Vector3(x1, y1, z0); }
                else if (d == Vector3Int.down) { a = new Vector3(x0, y0, z0); b = new Vector3(x1, y0, z0); cc = new Vector3(x1, y0, z1); e = new Vector3(x0, y0, z1); }
                else if (d == Vector3Int.left) { a = new Vector3(x0, y0, z1); b = new Vector3(x0, y1, z1); cc = new Vector3(x0, y1, z0); e = new Vector3(x0, y0, z0); }
                else if (d == Vector3Int.right) { a = new Vector3(x1, y0, z0); b = new Vector3(x1, y1, z0); cc = new Vector3(x1, y1, z1); e = new Vector3(x1, y0, z1); }
                else if (d.z < 0) { a = new Vector3(x0, y0, z0); b = new Vector3(x0, y1, z0); cc = new Vector3(x1, y1, z0); e = new Vector3(x1, y0, z0); }
                else { a = new Vector3(x1, y0, z1); b = new Vector3(x1, y1, z1); cc = new Vector3(x0, y1, z1); e = new Vector3(x0, y0, z1); }
                Quad(a, b, cc, e, d, tris);
            }
        }

        /// <summary>
        /// Слоп строится в локальной рамке «подъём к +Z», затем поворачивается вокруг центра клетки.
        /// Высоты: low у z = 0, high у z = 1.
        /// </summary>
        private static void AddSlope(VoxelLevel level, Int3 c, Block block, Func<Int3, bool> sameBand, List<int> tris)
        {
            var low = BlockShapes.EdgeTop(block.Shape, 0, 0, -1);
            var high = BlockShapes.EdgeTop(block.Shape, 0, 0, 1);
            var rot = block.Rotation;
            var o = new Vector3(c.X, c.Y, c.Z);

            Vector3 P(float x, float y, float z)
            {
                float rx = x, rz = z;
                for (int i = 0; i < rot; i++)
                {
                    var t = rx;
                    rx = rz;
                    rz = 1 - t;
                }

                return o + new Vector3(rx, y, rz);
            }

            Vector3Int D(int x, int y, int z)
            {
                int rx = x, rz = z;
                for (int i = 0; i < rot; i++)
                {
                    var t = rx;
                    rx = rz;
                    rz = -t;
                }

                return new Vector3Int(rx, y, rz);
            }

            bool SideHidden(Vector3Int d, float h) => Hides(level, c + new Int3(d.x, 0, d.z), sameBand, h);

            // Наклонный верх.
            Quad(P(0, low, 0), P(0, high, 1), P(1, high, 1), P(1, low, 0), Vector3.zero, tris);
            // Низ.
            if (!(level.TryGet(c.Below, out var below) && BlockShapes.FillsCell(below.Shape) && sameBand(c.Below)))
                Quad(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), Vector3Int.down, tris);
            // Задняя (высокая) грань.
            var back = D(0, 0, 1);
            if (!SideHidden(back, high))
                Quad(P(1, 0, 1), P(1, high, 1), P(0, high, 1), P(0, 0, 1), back, tris);
            // Передняя (низкая) грань, если низ слопа выше пола клетки.
            var front = D(0, 0, -1);
            if (low > 0 && !SideHidden(front, low))
                Quad(P(0, 0, 0), P(0, low, 0), P(1, low, 0), P(1, 0, 0), front, tris);
            // Боковые трапеции.
            var left = D(-1, 0, 0);
            if (!SideHidden(left, high))
                Quad(P(0, 0, 1), P(0, high, 1), P(0, low, 0), P(0, 0, 0), left, tris);
            var right = D(1, 0, 0);
            if (!SideHidden(right, high))
                Quad(P(1, 0, 0), P(1, low, 0), P(1, high, 1), P(1, 0, 1), right, tris);
        }

        /// <summary>Четырёхугольник; нормаль считается из вершин, порядок обхода выправляется под лицевую сторону.</summary>
        private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 expected, List<int> tris)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            if (expected != Vector3.zero && Vector3.Dot(normal, expected) < 0)
            {
                (b, d) = (d, b);
                normal = -normal;
            }

            var i = Verts.Count;
            Verts.Add(a);
            Verts.Add(b);
            Verts.Add(c);
            Verts.Add(d);
            for (int k = 0; k < 4; k++)
            {
                Normals.Add(normal);
                var p = k == 0 ? a : k == 1 ? b : k == 2 ? c : d;
                var an = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
                Uvs.Add(an.y >= an.x && an.y >= an.z ? new Vector2(p.x, p.z) : an.x >= an.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
            }

            tris.Add(i);
            tris.Add(i + 1);
            tris.Add(i + 2);
            tris.Add(i);
            tris.Add(i + 2);
            tris.Add(i + 3);
        }
    }
}
