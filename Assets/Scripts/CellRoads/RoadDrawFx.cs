using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>
    /// Эффекты рисования дороги: пунктирные направляющие, плавный контур с узлами, «голова» за пальцем,
    /// цена у пальца и волна света, бегущая по дороге после постройки. Не влияет на саму карту дорог.
    /// </summary>
    public sealed class RoadDrawFx : MonoBehaviour
    {
        public int SortingOrder = 18;

        sealed class Wave
        {
            public List<Vector2Int> Cells;
            public float Start;
            public float Cost;
            public float Width;
        }

        const float CellDelay = 0.035f;
        const float CellPulse = 0.45f;

        IRoadStroke[] _strokes = new IRoadStroke[0];
        IRoadStroke _active;
        float _cs = 1f;
        float _width = 0.5f;

        readonly MeshBatch _batch = new MeshBatch();
        readonly List<Wave> _waves = new List<Wave>();
        readonly List<Vector2> _line = new List<Vector2>();
        GameObject _view;
        Material _material;
        Vector2 _head;
        bool _hadHead;

        GUIStyle _price;

        /// <summary>Подключает все инструменты-штрихи: дорогу, шоссе, одностороннюю дорогу, канал.</summary>
        public void Bind(float cellSize, params IRoadStroke[] strokes)
        {
            foreach (IRoadStroke s in _strokes) s.Committed -= OnCommitted;
            _cs = cellSize;
            _strokes = strokes;
            foreach (IRoadStroke s in _strokes) s.Committed += OnCommitted;
        }

        void OnDestroy()
        {
            foreach (IRoadStroke s in _strokes) s.Committed -= OnCommitted;
        }

        void OnCommitted(IReadOnlyList<Vector2Int> cells, float cost)
        {
            if (cells.Count == 0) return;
            float w = _active != null ? _active.StrokeWidth : _width;
            _waves.Add(new Wave { Cells = new List<Vector2Int>(cells), Start = Time.unscaledTime, Cost = cost, Width = w });
        }

        IRoadStroke FindActive()
        {
            foreach (IRoadStroke s in _strokes)
                if ((s.Drawing || s.Pending) && s.Path.Count > 0) return s;
            return null;
        }

        Vector2 Center(Vector2Int c)
        {
            return new Vector2((c.x + 0.5f) * _cs, (c.y + 0.5f) * _cs);
        }

        void LateUpdate()
        {
            _active = FindActive();
            bool drawing = _active != null;
            for (int i = _waves.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - _waves[i].Start > _waves[i].Cells.Count * CellDelay + CellPulse + 0.9f)
                    _waves.RemoveAt(i);

            if (!drawing && _waves.Count == 0)
            {
                _hadHead = false;
                if (_view != null && _view.activeSelf) _view.SetActive(false);
                return;
            }

            EnsureView();
            _batch.Clear();
            if (drawing) DrawStroke();
            else _hadHead = false;
            DrawWaves();

            var f = _view.GetComponent<MeshFilter>();
            f.sharedMesh = _batch.ToMesh(f.sharedMesh);
            _view.SetActive(true);
        }

        void EnsureView()
        {
            if (_view != null) return;
            _material = new Material(Shader.Find("Sprites/Default"));
            _material.color = Color.white;
            _view = new GameObject("Road draw fx");
            _view.transform.SetParent(transform, false);
            _view.AddComponent<MeshFilter>();
            var r = _view.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.sortingOrder = SortingOrder;
        }

        // ---------------------------------------------------------------
        // Штрих

        void DrawStroke()
        {
            IReadOnlyList<Vector2Int> path = _active.Path;
            _width = _active.StrokeWidth;
            Vector2Int tailCell = path[path.Count - 1];
            Vector2 tail = Center(tailCell);

            bool affordable = _active.StrokeCost <= _active.Money + 0.001f;
            Color ink = affordable ? new Color32(0x20, 0x29, 0x38, 0xF0) : new Color32(0xD0, 0x3F, 0x3F, 0xF0);
            Color accent = affordable ? new Color32(0xF2, 0xB8, 0x4B, 0xFF) : new Color32(0xE5, 0x5B, 0x5B, 0xFF);

            // Голова плавно догоняет конец дороги.
            if (!_hadHead) { _head = Center(path[0]); _hadHead = true; }
            _head = Vector2.Lerp(_head, tail, 1f - Mathf.Exp(-22f * Time.unscaledDeltaTime));

            DrawGuides(tail);
            BuildLine(path);

            float half = _cs * _width * 0.5f;
            float thick = _cs * 0.05f;
            if (_line.Count >= 2)
            {
                Vector2 prevL = Vector2.zero, prevR = Vector2.zero;
                for (int i = 0; i < _line.Count; i++)
                {
                    Vector2 t = Tangent(i);
                    Vector2 n = new Vector2(-t.y, t.x);
                    Vector2 l = _line[i] + n * half, r = _line[i] - n * half;
                    if (i == 0)
                    {
                        Seg(l, r, thick, ink);
                    }
                    else
                    {
                        Seg(prevL, l, thick, ink);
                        Seg(prevR, r, thick, ink);
                    }

                    if (i == _line.Count - 1) Seg(l, r, thick, ink);
                    prevL = l; prevR = r;
                }
            }

            // Узлы: начало, повороты, конец.
            for (int i = 0; i < path.Count; i++)
            {
                bool end = i == 0 || i == path.Count - 1;
                bool corner = !end && (path[i] - path[i - 1]) != (path[i + 1] - path[i]);
                if (!end && !corner) continue;
                Node(Center(path[i]), _cs * 0.075f, ink);
            }

            // Кольцо-голова.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
            _batch.Ring(_head, _cs * (0.34f + 0.05f * pulse), _cs * (0.40f + 0.05f * pulse), 36,
                new Color(accent.r, accent.g, accent.b, 0.55f + 0.35f * pulse));
            _batch.Disc(_head, _cs * 0.11f, 16, accent);
        }

        Vector2 Tangent(int i)
        {
            Vector2 a = _line[Mathf.Max(0, i - 1)], b = _line[Mathf.Min(_line.Count - 1, i + 1)];
            Vector2 t = b - a;
            return t.sqrMagnitude < 1e-8f ? Vector2.right : t.normalized;
        }

        /// <summary>Центральная линия: прямые куски и закругления на поворотах (дуга от середины до середины клетки).</summary>
        void BuildLine(IReadOnlyList<Vector2Int> path)
        {
            _line.Clear();
            int n = path.Count;
            if (n == 0) return;
            _line.Add(Center(path[0]));
            for (int i = 1; i < n - 1; i++)
            {
                Vector2 c = Center(path[i]);
                bool straight = (path[i] - path[i - 1]) == (path[i + 1] - path[i]);
                if (straight) { _line.Add(c); continue; }
                Vector2 m1 = (Center(path[i - 1]) + c) * 0.5f, m2 = (c + Center(path[i + 1])) * 0.5f;
                const int steps = 8;
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    _line.Add((1f - t) * (1f - t) * m1 + 2f * (1f - t) * t * c + t * t * m2);
                }
            }

            if (n > 1) _line.Add(Center(path[n - 1]));
        }

        /// <summary>Пунктир по осям через конец дороги: показывает, куда дорога пойдёт ровно.</summary>
        void DrawGuides(Vector2 at)
        {
            const int dashes = 14;
            float len = _cs * 0.34f, gap = _cs * 0.26f;
            for (int dir = 0; dir < 4; dir++)
            {
                Vector2 d = dir == 0 ? Vector2.right : dir == 1 ? Vector2.left : dir == 2 ? Vector2.up : Vector2.down;
                for (int k = 0; k < dashes; k++)
                {
                    float a = _cs * 0.75f + k * (len + gap);
                    float fade = 1f - k / (float)dashes;
                    Seg(at + d * a, at + d * (a + len), _cs * 0.028f, new Color(0.13f, 0.16f, 0.22f, 0.36f * fade));
                }
            }
        }

        void Node(Vector2 p, float r, Color ink)
        {
            _batch.Disc(p, r * 1.5f, 14, Color.white);
            _batch.Disc(p, r, 14, ink);
        }

        void Seg(Vector2 a, Vector2 b, float thick, Color col)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-10f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (thick * 0.5f);
            _batch.Quad(a - n, a + n, b + n, b - n, col);
        }

        // ---------------------------------------------------------------
        // Волна после постройки

        void DrawWaves()
        {
            foreach (Wave w in _waves)
            {
                float side = _cs * w.Width * 1.15f * 0.5f;
                float age = Time.unscaledTime - w.Start;
                for (int i = 0; i < w.Cells.Count; i++)
                {
                    float x = age - i * CellDelay;
                    if (x < 0f || x > CellPulse) continue;
                    float k = Mathf.Sin(Mathf.PI * x / CellPulse);
                    Vector2 c = Center(w.Cells[i]);
                    float e = side * (1f + 0.25f * k);
                    _batch.Rect(c - new Vector2(e, e), c + new Vector2(e, e), new Color(1f, 1f, 1f, 0.55f * k));
                }
            }
        }

        // ---------------------------------------------------------------
        // Цена у пальца

        void OnGUI()
        {
            Camera cam = _active != null ? _active.Cam : (_strokes.Length > 0 ? _strokes[0].Cam : null);
            if (cam == null) return;
            if (_price == null)
            {
                _price = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }

            _price.fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.024f));

            if (_active != null && _active.Drawing && _active.Path.Count > 1)
            {
                float cost = _active.StrokeCost;
                bool ok = cost <= _active.Money + 0.001f;
                Vector2 tail = Center(_active.Path[_active.Path.Count - 1]);
                Bubble(cam, tail, "-" + Mathf.RoundToInt(cost), ok ? new Color32(0x20, 0x29, 0x38, 0xE6) : new Color32(0xB8, 0x33, 0x33, 0xE6), 0f);
            }

            foreach (Wave w in _waves)
            {
                float age = Time.unscaledTime - w.Start;
                float rise = Mathf.Clamp01(age / 0.9f);
                if (w.Cost <= 0f || age > 0.9f) continue;
                Vector2 at = Center(w.Cells[w.Cells.Count - 1]);
                Bubble(cam, at, "-" + Mathf.RoundToInt(w.Cost), new Color32(0x20, 0x29, 0x38, (byte)(230 * (1f - rise))), rise * 60f);
            }
        }

        void Bubble(Camera cam, Vector2 world, string text, Color bg, float risePx)
        {
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            if (sp.z < 0f) return;
            float w = _price.fontSize * (text.Length * 0.6f + 1.6f), h = _price.fontSize * 1.7f;
            float x = sp.x - w * 0.5f;
            float y = Screen.height - sp.y - h - Screen.height * 0.055f - risePx;
            Color old = GUI.color;
            GUI.color = bg;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, bg.a);
            GUI.Label(new Rect(x, y, w, h), text, _price);
            GUI.color = old;
        }
    }
}
