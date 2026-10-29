using System.Collections.Generic;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>Рисует все дороги по клеткам: дорога, остров кольца, тень и полотно эстакады. Четыре меша, перестраиваются при изменении.</summary>
    public sealed class CellRoadRenderer : MonoBehaviour
    {
        public float CellSize = 1f;
        [Range(0.2f, 0.9f)] public float WidthFraction = 0.5f;
        public Color MarkingColor = new Color32(0xE9, 0xE6, 0xDA, 0xFF);
        public Color MedianColor = new Color32(0xF2, 0xB8, 0x4B, 0xFF);
        public Color SelectionColor = new Color32(0x4D, 0xA3, 0xFF, 0xFF);
        public Color IslandColor = new Color32(0xA9, 0xD1, 0x8E, 0xFF);
        public Color ShadowColor = new Color32(0x1E, 0x24, 0x2B, 0x59);

        CellRoadMap _map;
        readonly List<GameObject> _views = new List<GameObject>();
        Material[] _materials;

        public void Bind(CellRoadMap map)
        {
            if (_map != null)
            {
                _map.Changed -= Redraw;
                _map.PreviewChanged -= Redraw;
            }

            _map = map;
            _map.Changed += Redraw;
            _map.PreviewChanged += Redraw;

            Color road = RoadSpecs.BodyColor(RoadType.Local);
            _materials = new[]
            {
                Make(road), Make(MarkingColor), Make(IslandColor), Make(ShadowColor), Make(road), Make(MarkingColor), Make(SelectionColor), Make(MedianColor)
            };
            Redraw();
        }

        static Material Make(Color c)
        {
            var m = new Material(Shader.Find("Sprites/Default"));
            m.color = c;
            return m;
        }

        void OnDestroy()
        {
            if (_map == null) return;
            _map.Changed -= Redraw;
            _map.PreviewChanged -= Redraw;
        }

        void Redraw()
        {
            foreach (GameObject go in _views)
            {
                if (go == null) continue;
                var f = go.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
                Destroy(go);
            }

            _views.Clear();

            RoadLayers layers = RoadCellMesher.Build(_map.Cells, _map.DisplayTypeAt, _map.LaneAt, CellSize, WidthFraction);
            if (_map.PreviewCell.HasValue && _map.IsJunction(_map.PreviewCell.Value))
                RoadCellMesher.AppendSelection(layers, _map.PreviewCell.Value, CellSize);
            int baseOrder = RoadSpecs.SortingOrder(RoadLayer.Ground);
            for (int i = 0; i < RoadLayers.Count; i++)
            {
                if (layers.Verts[i].Count == 0) continue;
                var go = new GameObject("Roads layer " + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = layers.ToMesh(i);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _materials[i];
                // Шаг 2, чтобы между слоями дороги вставали машины: нижние на +3, верхние на мостах +13.
                r.sortingOrder = baseOrder + i * 2 + (i == RoadLayers.Selection ? 20 : 0);
                _views.Add(go);
            }
        }
    }
}
