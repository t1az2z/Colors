using System.Collections.Generic;
using System.Linq;
using ColorsAction.Level;
using ColorsAction.Level.Navigation;
using ColorsAction.Unity.Voxel;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColorsAction.Unity.Editor.LevelEditor
{
    /// <summary>Работа редактора уровней в Scene View: ввод мышью и клавишами, курсор, разметка, наложение карты пути.</summary>
    public sealed partial class LevelEditorWindow
    {
        private Int3 lastStrokeCell = new Int3(int.MinValue, 0, 0);
        private Int3? hoverCell;
        private VoxelPicking.Hit lastHit;
        private int hoverX, hoverZ;
        private bool hoverPlane;

        private HashSet<int> reachable;
        private (int version, int profile, bool everything) reachableKey;

        /// <summary>Видна ли клетка (блоки скрытых этажей не ловят луч).</summary>
        private bool Visible(Int3 cell) =>
            session.Level.FloorIndexOf(cell.Y) <= activeFloor || upperMode == UpperFloorsMode.Visible;

        private bool OnShownFloor(Int3 cell) =>
            session.Level.FloorIndexOf(cell.Y) <= activeFloor || upperMode != UpperFloorsMode.Hidden;

        private void OnSceneGUI(SceneView view)
        {
            if (session == null)
                return;
            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);
            HandleKeys(e);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(id);

            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            UpdateHover(ray);
            HandleMouse(e, id);

            if (e.type == EventType.Repaint)
            {
                if (showNav)
                    DrawNav();
                DrawCursor();
            }

            DrawMarkup();
            DrawStatus();
            if (e.type == EventType.MouseMove)
                view.Repaint();
        }

        // ------------------------------------------------------------------ ввод

        private void HandleKeys(Event e)
        {
            if (e.type == EventType.ScrollWheel && e.shift && (tool == Tool.Paint || tool == Tool.Erase))
            {
                var d = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
                height += d > 0 ? -1 : 1;
                e.Use();
                Repaint();
                return;
            }

            if (e.type != EventType.KeyDown)
                return;
            switch (e.keyCode)
            {
                case KeyCode.PageUp:
                    SetFloor(activeFloor + 1);
                    e.Use();
                    break;
                case KeyCode.PageDown:
                    SetFloor(activeFloor - 1);
                    e.Use();
                    break;
                case KeyCode.R when !e.control && !e.shift && tool == Tool.Paint:
                    rotation = (rotation + 1) % 4;
                    e.Use();
                    Repaint();
                    break;
                case KeyCode.Escape:
                    if (pickingTarget)
                        pickingTarget = false;
                    else
                        selectedObject = selectedPoint = null;
                    e.Use();
                    Repaint();
                    break;
                case KeyCode.Delete when selectedObject != null || selectedPoint != null:
                    DeleteSelection();
                    e.Use();
                    break;
            }
        }

        private void UpdateHover(Ray ray)
        {
            hoverPlane = VoxelPicking.PlaneCell(ray, LayerY, out hoverX, out hoverZ, out _);
            if (VoxelPicking.Raycast(session.Level, ray, 500f, Visible, out var hit))
            {
                hoverCell = hit.Cell;
                lastHit = hit;
            }
            else
                hoverCell = null;
        }

        private void HandleMouse(Event e, int id)
        {
            if (e.alt || e.button != 0)
                return;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.control && tool == Tool.Paint)
                    {
                        Eyedrop();
                        e.Use();
                        return;
                    }

                    GUIUtility.hotControl = id;
                    lastStrokeCell = new Int3(int.MinValue, 0, 0);
                    Apply(true);
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    Apply(false);
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    session.EndStroke();
                    e.Use();
                    break;
            }
        }

        private void Apply(bool click)
        {
            var brush = session.Brush;
            brush.Size = brushSize;
            brush.Mode = column ? BrushMode.Column : BrushMode.Single;
            switch (tool)
            {
                case Tool.Paint:
                    if (hoverPlane && NewStrokeCell(new Int3(hoverX, LayerY, hoverZ)))
                        brush.Paint(hoverX, LayerY, hoverZ, new Block(shape, rotation, color), FloorBase);
                    break;
                case Tool.Erase:
                    if (hoverPlane && NewStrokeCell(new Int3(hoverX, LayerY, hoverZ)))
                        brush.Erase(hoverX, LayerY, hoverZ);
                    break;
                case Tool.Recolor:
                    if (hoverCell.HasValue && NewStrokeCell(hoverCell.Value))
                        brush.Recolor(hoverCell.Value.X, hoverCell.Value.Y, hoverCell.Value.Z, color);
                    break;
                case Tool.Objects:
                    if (click)
                        ClickObject();
                    break;
                case Tool.Points:
                    if (click)
                        ClickPoint();
                    break;
                case Tool.Select:
                    if (click)
                        ClickSelect();
                    break;
            }
        }

        private bool NewStrokeCell(Int3 cell)
        {
            if (cell == lastStrokeCell)
                return false;
            lastStrokeCell = cell;
            return true;
        }

        private void Eyedrop()
        {
            if (!hoverCell.HasValue || !session.Level.TryGet(hoverCell.Value, out var b))
                return;
            shape = b.Shape;
            rotation = b.Rotation;
            color = b.Color;
            SetFloor(session.Level.FloorIndexOf(hoverCell.Value.Y));
            height = hoverCell.Value.Y - FloorBase;
            Repaint();
        }

        private Int3? PlacementCell() => hoverCell.HasValue ? VoxelPicking.PlacementCell(session.Level, lastHit) : (Int3?)null;

        private LevelObject ObjectAt(Int3 cell) => session.Level.Objects.FirstOrDefault(o => o.Cell == cell || o.Covers(cell));
        private InteractionPoint PointAt(Int3 cell) => session.Level.Points.FirstOrDefault(p => p.Cell == cell);

        private bool TrySelectAt(Int3 cell)
        {
            var point = PointAt(cell);
            var obj = point == null ? ObjectAt(cell) : null;
            if (point == null && obj == null && hoverCell.HasValue)
                obj = ObjectAt(hoverCell.Value);
            if (point == null && obj == null)
                return false;
            selectedPoint = point?.Id;
            selectedObject = obj?.Id;
            Repaint();
            return true;
        }

        private void ClickSelect()
        {
            var cell = PlacementCell();
            if (cell.HasValue && TrySelectAt(cell.Value))
                return;
            selectedObject = selectedPoint = null;
            Repaint();
        }

        private void ClickObject()
        {
            var cell = PlacementCell();
            if (!cell.HasValue || TrySelectAt(cell.Value))
                return;
            var obj = new LevelObject
            {
                Id = objectKind == LevelObjectKind.PlayerSpawn && session.Level.FindObject("spawn") == null
                    ? "spawn"
                    : session.Level.NewObjectId(objectKind),
                Kind = objectKind, Cell = cell.Value, Barrier = barrierKind,
                Height = objectKind == LevelObjectKind.ShortcutDoor ? doorHeight
                    : objectKind == LevelObjectKind.CrateStack ? crateHeight
                    : objectKind == LevelObjectKind.DrawBridge ? DrawBridgeShape.Height : barrierHeight,
                Yaw = objectKind == LevelObjectKind.ShortcutDoor ? doorYaw : objectKind == LevelObjectKind.CrateStack ? crateYaw
                    : objectKind == LevelObjectKind.DrawBridge ? bridgeYaw : 0f,
                Color = HasColor(objectKind) ? objectColor : null,
                Item = string.IsNullOrEmpty(objectItem) ? null : objectItem,
                Count = objectKind == LevelObjectKind.Chest ? chestCount : 1
            };
            session.Execute(MarkupCommand.PutObject(null, obj));
            selectedObject = obj.Id;
            selectedPoint = null;
            Repaint();
        }

        private void ClickPoint()
        {
            var cell = PlacementCell();
            if (!cell.HasValue)
                return;
            var source = pickingTarget && selectedPoint != null ? session.Level.FindPoint(selectedPoint) : null;
            if (source != null)
            {
                var after = source.Clone();
                var targetPoint = PointAt(cell.Value);
                var targetObject = targetPoint == null ? ObjectAt(cell.Value) ?? (hoverCell.HasValue ? ObjectAt(hoverCell.Value) : null) : null;
                if (targetPoint != null && targetPoint.Id != source.Id)
                    after.TargetPoint = targetPoint.Id;
                else if (targetObject != null)
                    after.TargetObject = targetObject.Id;
                else if (InteractionPoint.CreatesLink(source.Action))
                {
                    // Пустая клетка: ставим точку B того же действия без условия и связываем с ней.
                    var b = new InteractionPoint
                    {
                        Id = session.Level.NewPointId(source.Action), Action = source.Action, Cell = cell.Value, TwoWay = source.TwoWay
                    };
                    session.Execute(MarkupCommand.PutPoint(null, b));
                    after.TargetPoint = b.Id;
                }

                session.Execute(MarkupCommand.PutPoint(source, after));
                pickingTarget = false;
                Repaint();
                return;
            }

            if (TrySelectAt(cell.Value))
                return;
            var point = InteractionPoint.Create(session.Level.NewPointId(pointAction), pointAction, cell.Value);
            session.Execute(MarkupCommand.PutPoint(null, point));
            selectedPoint = point.Id;
            selectedObject = null;
            pickingTarget = true;
            Repaint();
        }

        // ------------------------------------------------------------------ рисование

        /// <summary>Высота пола в клетке стояния (с учётом неполного блока в самой клетке).</summary>
        private float Surface(Int3 stand)
        {
            if (session.Level.TryGet(stand, out var b) && BlockShapes.IsSolid(b.Shape))
                return stand.Y + BlockShapes.CenterTop(b.Shape);
            return stand.Y;
        }

        private void DrawCursor()
        {
            Handles.zTest = CompareFunction.Always;
            if (tool == Tool.Paint || tool == Tool.Erase)
            {
                if (!hoverPlane)
                    return;
                var y = LayerY;
                Handles.color = new Color(1f, 1f, 1f, 0.18f);
                const int r = 6;
                for (int i = -r; i <= r + 1; i++)
                {
                    Handles.DrawLine(new Vector3(hoverX + i, y, hoverZ - r), new Vector3(hoverX + i, y, hoverZ + r + 1));
                    Handles.DrawLine(new Vector3(hoverX - r, y, hoverZ + i), new Vector3(hoverX + r + 1, y, hoverZ + i));
                }

                Handles.color = tool == Tool.Paint ? new Color(0.3f, 1f, 0.4f, 0.9f) : new Color(1f, 0.3f, 0.3f, 0.9f);
                session.Brush.Size = brushSize;
                var h = tool == Tool.Paint ? Mathf.Max(0.05f, BlockShapes.MaxTop(shape)) : 1f;
                foreach (var (x, z) in session.Brush.Footprint(hoverX, hoverZ))
                {
                    Handles.DrawWireCube(new Vector3(x + 0.5f, y + h * 0.5f, z + 0.5f), new Vector3(1f, h, 1f));
                    if (tool == Tool.Paint && column && y > FloorBase)
                        Handles.DrawDottedLine(new Vector3(x + 0.5f, FloorBase, z + 0.5f), new Vector3(x + 0.5f, y, z + 0.5f), 3f);
                }

                if (tool == Tool.Paint && BlockShapes.IsSlope(shape))
                {
                    var (dx, dz) = BlockShapes.RiseDirection(rotation);
                    Handles.ArrowHandleCap(0, new Vector3(hoverX + 0.5f, y + 0.5f, hoverZ + 0.5f),
                        Quaternion.LookRotation(new Vector3(dx, 0, dz)), 0.8f, EventType.Repaint);
                }

                return;
            }

            if (!hoverCell.HasValue)
                return;
            if (tool == Tool.Recolor)
            {
                Handles.color = Color.cyan;
                var c = hoverCell.Value;
                session.Brush.Size = brushSize;
                foreach (var (x, z) in session.Brush.Footprint(c.X, c.Z))
                    Handles.DrawWireCube(new Vector3(x + 0.5f, c.Y + 0.5f, z + 0.5f), Vector3.one * 1.02f);
            }
            else
            {
                Handles.color = pickingTarget ? Color.green : Color.yellow;
                var c = PlacementCell() ?? hoverCell.Value;
                Handles.DrawWireDisc(new Vector3(c.X + 0.5f, Surface(c) + 0.02f, c.Z + 0.5f), Vector3.up, 0.45f, 2f);
            }
        }

        private static Color ActionColor(InteractionAction a)
        {
            switch (a)
            {
                case InteractionAction.ShieldSlide: return new Color(0.69f, 0.49f, 1f);
                case InteractionAction.Lift: return new Color(0.9f, 0.9f, 0.9f);
                case InteractionAction.GrappleAnchor: return new Color(0.09f, 0.71f, 0.66f);
                case InteractionAction.FlailRing: return new Color(1f, 0.54f, 0.12f);
                case InteractionAction.Lever: return new Color(1f, 0.85f, 0.2f);
                case InteractionAction.ShortcutDoor: return new Color(0.6f, 0.4f, 0.25f);
                default: return new Color(0.4f, 0.8f, 1f);
            }
        }

        private static Color KindColor(LevelObjectKind k)
        {
            switch (k)
            {
                case LevelObjectKind.PlayerSpawn: return new Color(0.3f, 1f, 0.4f);
                case LevelObjectKind.Bonfire: return new Color(1f, 0.55f, 0.1f);
                case LevelObjectKind.Npc: return new Color(0.4f, 0.8f, 1f);
                case LevelObjectKind.Hint: return Color.white;
                case LevelObjectKind.ShortcutDoor: return new Color(0.6f, 0.4f, 0.25f);
                case LevelObjectKind.CrateStack: return new Color(0.18f, 0.42f, 1f);
                case LevelObjectKind.Chest: return new Color(0.85f, 0.7f, 0.25f);
                case LevelObjectKind.DrawBridge: return new Color(0.9f, 0.2f, 0.15f);
                default: return Color.gray;
            }
        }

        private void DrawMarkup()
        {
            if (Event.current.type != EventType.Repaint)
                return;
            var label = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } };
            Handles.zTest = CompareFunction.LessEqual;
            foreach (var o in session.Level.Objects)
            {
                if (OnShownFloor(o.Cell))
                    DrawObject(o, label);
            }

            foreach (var p in session.Level.Points)
            {
                if (OnShownFloor(p.Cell))
                    DrawPoint(p, label);
            }
        }

        private void DrawObject(LevelObject o, GUIStyle label)
        {
            var pos = new Vector3(o.Cell.X + 0.5f, Surface(o.Cell), o.Cell.Z + 0.5f);
            var c = o.Color != null ? session.ColorOf(o.Color) : KindColor(o.Kind);
            var enemyName = (string)null;
            if (o.Kind == LevelObjectKind.EnemySpawn)
            {
                // Враг окрашен цветом своего типа из Enemies.json.
                foreach (var e in session.Enemies)
                {
                    if (e.id != o.Item)
                        continue;
                    enemyName = e.name;
                    if (e.color != null)
                        c = session.ColorOf(e.color);
                }
            }

            var top = o.Cell.Y + 1f;
            if (o.IsBarrier)
            {
                var h = Mathf.Max(1, o.Height);
                top = o.Cell.Y + h;
                var thick = o.Barrier == BarrierKind.Crate ? 0.9f : o.Barrier == BarrierKind.Membrane ? 0.12f : 0.3f;
                var rot = Quaternion.Euler(0, o.Yaw, 0);
                var footprint = rot * new Vector3(o.Barrier == BarrierKind.Crate ? 0.9f : 1f, 0, thick);
                var size = new Vector3(Mathf.Abs(footprint.x), o.Barrier == BarrierKind.Crate ? 0.9f * h : h, Mathf.Abs(footprint.z));
                var center = new Vector3(pos.x, o.Cell.Y + size.y * 0.5f, pos.z);
                Handles.color = new Color(c.r, c.g, c.b, o.Barrier == BarrierKind.Membrane ? 0.25f : 0.5f);
                DrawSolidBox(center, size);
                Handles.color = c;
                Handles.DrawWireCube(center, size);
                if (o.Barrier == BarrierKind.Grate)
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        var off = rot * new Vector3(i * 0.3f, 0, 0);
                        Handles.DrawLine(center + off + Vector3.down * h * 0.5f, center + off + Vector3.up * h * 0.5f);
                    }
                }
            }
            else if (o.IsCrate)
            {
                // Стопка ящиков: коробка на две клетки по направлению поворота.
                var h = Mathf.Max(1, o.Height);
                top = o.Cell.Y + h;
                var (cx, cz) = CrateStackShape.Center(o.Cell.X, o.Cell.Z, o.Yaw);
                var (dx, dz) = CrateStackShape.Step(o.Yaw);
                var size = new Vector3(dx != 0 ? 1.9f : 0.9f, h - 0.05f, dz != 0 ? 1.9f : 0.9f);
                var center = new Vector3(cx, pos.y + size.y * 0.5f, cz);
                Handles.color = new Color(c.r, c.g, c.b, 0.55f);
                DrawSolidBox(center, size);
                Handles.color = c;
                for (int k = 0; k < h; k++)
                    Handles.DrawWireCube(new Vector3(cx, pos.y + k + 0.5f, cz), new Vector3(size.x, 0.95f, size.z));
            }
            else if (o.IsBridge)
            {
                // Разводной мост: ближняя створка лежит (половина 4×2), дальняя поднята стенкой у дальнего края,
                // от её верха к раме за мостом — цепи.
                top = o.Cell.Y + DrawBridgeShape.Height;
                var rot = Quaternion.Euler(0, DrawBridgeShape.SnapYaw(o.Yaw), 0);
                var (cx, cz) = DrawBridgeShape.Center(o.Cell.X, o.Cell.Z, o.Yaw);
                var center = new Vector3(cx, pos.y + 0.03f, cz);
                var deck = rot * new Vector3(DrawBridgeShape.LeafLength, 0f, DrawBridgeShape.Width);
                Handles.color = new Color(0.85f, 0.85f, 0.85f, 0.45f);
                DrawSolidBox(center - rot * new Vector3(1f, 0f, 0f), new Vector3(Mathf.Abs(deck.x), 0.06f, Mathf.Abs(deck.z)));
                var leafCenter = center + rot * new Vector3(1.7f, DrawBridgeShape.Height * 0.5f, 0f);
                var leaf = rot * new Vector3(0.4f, 0f, DrawBridgeShape.Width);
                var leafSize = new Vector3(Mathf.Abs(leaf.x), DrawBridgeShape.Height, Mathf.Abs(leaf.z));
                Handles.color = new Color(0.85f, 0.85f, 0.85f, 0.55f);
                DrawSolidBox(leafCenter, leafSize);
                Handles.color = c;
                Handles.DrawWireCube(leafCenter, leafSize);
                foreach (var side in new[] { -0.8f, 0.8f })
                    Handles.DrawLine(center + rot * new Vector3(1.5f, DrawBridgeShape.Height - 0.1f, side),
                        center + rot * new Vector3(2.8f, 2.55f, side * 1.5f), 3f);
            }
            else if (o.IsDoor)
            {
                // Плита поперёк клетки и стрелка в лицевую сторону — оттуда дверь открывается.
                var h = Mathf.Max(1, o.Height);
                top = o.Cell.Y + h;
                var rot = Quaternion.Euler(0, o.Yaw, 0);
                var footprint = rot * new Vector3(1f, 0, 0.3f);
                var size = new Vector3(Mathf.Abs(footprint.x), h, Mathf.Abs(footprint.z));
                var center = new Vector3(pos.x, o.Cell.Y + h * 0.5f, pos.z);
                Handles.color = new Color(c.r, c.g, c.b, 0.55f);
                DrawSolidBox(center, size);
                Handles.color = c;
                Handles.DrawWireCube(center, size);
                Handles.color = new Color(1f, 0.85f, 0.3f);
                Handles.ArrowHandleCap(0, pos + Vector3.up * 0.15f + rot * Vector3.forward * 0.15f, rot, 0.8f, EventType.Repaint);
            }
            else
            {
                Handles.color = c;
                switch (o.Kind)
                {
                    case LevelObjectKind.PlayerSpawn:
                        Handles.DrawSolidDisc(pos + Vector3.up * 0.03f, Vector3.up, 0.4f);
                        Handles.color = Color.black;
                        Handles.ArrowHandleCap(0, pos + Vector3.up * 0.1f, Quaternion.Euler(0, o.Yaw, 0), 0.6f, EventType.Repaint);
                        break;
                    case LevelObjectKind.WeaponPedestal:
                        DrawSolidBox(pos + Vector3.up * 0.25f, new Vector3(0.6f, 0.5f, 0.6f));
                        break;
                    case LevelObjectKind.Chest:
                        DrawSolidBox(pos + Vector3.up * 0.3f, new Vector3(0.86f, 0.6f, 0.62f));
                        Handles.Label(pos + Vector3.up * 0.8f, $"{o.Item} ×{o.Count}");
                        break;
                    case LevelObjectKind.Bonfire:
                        Handles.ConeHandleCap(0, pos + Vector3.up * 0.3f, Quaternion.LookRotation(Vector3.up), 0.5f, EventType.Repaint);
                        break;
                    case LevelObjectKind.Hint:
                        Handles.DrawWireDisc(pos + Vector3.up * 0.03f, Vector3.up, 0.4f, 2f);
                        break;
                    default:
                        Handles.SphereHandleCap(0, pos + Vector3.up * 0.4f, Quaternion.identity, 0.6f, EventType.Repaint);
                        break;
                }
            }

            if (o.Id == selectedObject)
            {
                Handles.color = Color.yellow;
                var h = top - o.Cell.Y;
                Handles.DrawWireCube(new Vector3(pos.x, o.Cell.Y + h * 0.5f, pos.z), new Vector3(1.08f, h + 0.08f, 1.08f));
            }

            var text = enemyName != null ? "Враг: " + enemyName
                : o.IsDoor ? "Дверь-срезка ↦ " + DoorSideLabels[((Mathf.RoundToInt(o.Yaw / 90f) % 4) + 4) % 4]
                : o.Item != null ? $"{o.Kind}: {o.Item}" : o.Kind.ToString();
            Handles.Label(new Vector3(pos.x, top + 0.2f, pos.z), text, label);
        }

        private void DrawPoint(InteractionPoint p, GUIStyle label)
        {
            var a = PointPos(p);
            var c = ActionColor(p.Action);
            Handles.color = c;
            Handles.DrawSolidDisc(a, Vector3.up, 0.32f);
            if (p.Id == selectedPoint)
            {
                Handles.color = Color.yellow;
                Handles.DrawWireDisc(a, Vector3.up, 0.45f, 3f);
            }

            Handles.Label(a + Vector3.up * 0.5f, p.Condition.IsEmpty ? p.Id : $"{p.Id}\n{p.Condition}", label);

            var target = p.TargetPoint != null ? session.Level.FindPoint(p.TargetPoint) : null;
            if (target != null)
            {
                var b = PointPos(target);
                var lift = Vector3.up * Mathf.Max(1f, Vector3.Distance(a, b) * 0.25f);
                Handles.DrawBezier(a, b, a + lift, b + lift, c, null, 4f);
                Handles.color = c;
                Handles.ConeHandleCap(0, b + Vector3.up * 0.25f, Quaternion.LookRotation(Vector3.down), 0.3f, EventType.Repaint);
                if (p.TwoWay)
                    Handles.ConeHandleCap(0, a + Vector3.up * 0.25f, Quaternion.LookRotation(Vector3.down), 0.3f, EventType.Repaint);
            }

            var obj = p.TargetObject != null ? session.Level.FindObject(p.TargetObject) : null;
            if (obj != null)
            {
                Handles.color = c;
                Handles.DrawDottedLine(a, new Vector3(obj.Cell.X + 0.5f, obj.Cell.Y + 0.5f, obj.Cell.Z + 0.5f), 4f);
            }
        }

        private Vector3 PointPos(InteractionPoint p) => new Vector3(p.Cell.X + 0.5f, Surface(p.Cell) + 0.04f, p.Cell.Z + 0.5f);

        private static readonly int[][] BoxFaces =
            { new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };

        private static void DrawSolidBox(Vector3 center, Vector3 size)
        {
            var e = size * 0.5f;
            var c = new[]
            {
                center + new Vector3(-e.x, -e.y, -e.z), center + new Vector3(e.x, -e.y, -e.z), center + new Vector3(e.x, -e.y, e.z),
                center + new Vector3(-e.x, -e.y, e.z), center + new Vector3(-e.x, e.y, -e.z), center + new Vector3(e.x, e.y, -e.z),
                center + new Vector3(e.x, e.y, e.z), center + new Vector3(-e.x, e.y, e.z)
            };
            foreach (var f in BoxFaces)
                Handles.DrawAAConvexPolygon(c[f[0]], c[f[1]], c[f[2]], c[f[3]]);
        }

        private static Material overlayMaterial;

        /// <summary>Неосвещённый материал с цветом вершин, прозрачный, с проверкой глубины.</summary>
        private static Material OverlayMaterial
        {
            get
            {
                if (overlayMaterial != null)
                    return overlayMaterial;
                overlayMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                overlayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                overlayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                overlayMaterial.SetInt("_Cull", (int)CullMode.Off);
                overlayMaterial.SetInt("_ZWrite", 0);
                overlayMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
                return overlayMaterial;
            }
        }

        private void DrawNav()
        {
            if (session.NavStale && session.Graph == null)
                session.RebuildNav();
            var graph = session.Graph;
            if (graph == null || session.Profiles.Count == 0)
                return;
            var pi = Mathf.Clamp(profileIndex, 0, session.Profiles.Count - 1);
            var profile = session.Profiles[pi];
            var ctx = new NavContext { Everything = navEverything };
            HashSet<int> only = null;
            if (navReachable)
            {
                var key = (graph.LevelVersion, pi, navEverything);
                if (reachable == null || !reachableKey.Equals(key))
                {
                    var spawn = session.Level.Objects.FirstOrDefault(o => o.Kind == LevelObjectKind.PlayerSpawn);
                    reachable = spawn != null
                        ? new PathFinder(graph).Reachable(graph.NodeAtStand(spawn.Cell), profile, ctx)
                        : new HashSet<int>();
                    reachableKey = key;
                }

                only = reachable;
            }

            var (from, to) = session.Level.FloorRange(activeFloor);
            OverlayMaterial.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0.25f, 0.95f, 0.35f, 0.45f));
            foreach (var n in graph.Nodes)
            {
                if (n.Block.Y < from || n.Block.Y >= to)
                    continue;
                if (only != null ? !only.Contains(n.Index) : !NavGraph.CanStand(n, profile, ctx))
                    continue;
                var y = n.Block.Y + (session.Level.TryGet(n.Block, out var b) ? BlockShapes.MaxTop(b.Shape) : 1f) + 0.03f;
                float x0 = n.Block.X + 0.12f, x1 = n.Block.X + 0.88f, z0 = n.Block.Z + 0.12f, z1 = n.Block.Z + 0.88f;
                if (n.Smoothed)
                {
                    // Сглаженный верх — квадрат лежит на наклоне.
                    var k = n.Corners;
                    GL.Vertex3(x0, SurfaceSmoothing.HeightAt(k, 0.12f, 0.12f) + 0.03f, z0);
                    GL.Vertex3(x0, SurfaceSmoothing.HeightAt(k, 0.12f, 0.88f) + 0.03f, z1);
                    GL.Vertex3(x1, SurfaceSmoothing.HeightAt(k, 0.88f, 0.88f) + 0.03f, z1);
                    GL.Vertex3(x1, SurfaceSmoothing.HeightAt(k, 0.88f, 0.12f) + 0.03f, z0);
                    continue;
                }

                GL.Vertex3(x0, y, z0);
                GL.Vertex3(x0, y, z1);
                GL.Vertex3(x1, y, z1);
                GL.Vertex3(x1, y, z0);
            }

            GL.End();
            GL.Begin(GL.LINES);
            foreach (var n in graph.Nodes)
            {
                if (n.Block.Y < from || n.Block.Y >= to || (only != null && !only.Contains(n.Index)))
                    continue;
                foreach (var link in n.Links)
                {
                    var move = graph.Classify(link, profile, ctx);
                    if (move == NavMove.None || move == NavMove.Walk)
                        continue;
                    var m = graph.Nodes[link.To];
                    var a = new Vector3(n.Block.X + 0.5f, n.Surface + 0.08f, n.Block.Z + 0.5f);
                    var b = new Vector3(m.Block.X + 0.5f, m.Surface + 0.08f, m.Block.Z + 0.5f);
                    GL.Color(move == NavMove.Jump ? new Color(0.25f, 0.5f, 1f)
                        : move == NavMove.Drop ? new Color(1f, 0.6f, 0.2f, 0.8f)
                        : new Color(0.69f, 0.49f, 1f));
                    var arc = move == NavMove.Special ? 1.2f : 0.4f;
                    var peak = Vector3.Lerp(a, b, 0.5f) + Vector3.up * (arc + Mathf.Max(0, b.y - a.y) * 0.5f);
                    GL.Vertex(a);
                    GL.Vertex(peak);
                    GL.Vertex(peak);
                    GL.Vertex(b);
                    var dir = (b - peak).normalized;
                    var side = Vector3.Cross(dir, Vector3.up).normalized * 0.12f;
                    GL.Vertex(b);
                    GL.Vertex(b - dir * 0.25f + side);
                    GL.Vertex(b);
                    GL.Vertex(b - dir * 0.25f - side);
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        private void DrawStatus()
        {
            Handles.BeginGUI();
            var floor = session.Level.Floors[activeFloor];
            var text = $"{ToolNames[(int)tool]}  |  этаж «{floor.Name}» ({activeFloor + 1}/{session.Level.Floors.Count})";
            if (tool == Tool.Paint || tool == Tool.Erase)
                text += $"  |  H = {height} (Y {LayerY})";
            if (tool == Tool.Paint)
                text += $"  |  {shape}" + (BlockShapes.IsSlope(shape) ? " ↑" + RotationNames[rotation] : "");
            if (pickingTarget)
                text += "  |  укажите цель";
            if (hoverCell.HasValue)
                text += $"  |  клетка {hoverCell.Value}";
            var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 12 };
            var size = style.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(80, 10, size.x + 10, size.y + 4), text, style);
            Handles.EndGUI();
        }
    }
}
