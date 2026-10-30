using UnityEngine;

namespace CityGame.City
{
    /// <summary>
    /// Рисует мигающий цветной квадрат над клетками с активными событиями.
    /// Использует GL-рендеринг поверх сцены.
    /// </summary>
    public sealed class EventRenderer : MonoBehaviour
    {
        public float CellSize { get; set; } = 1f;

        EventSystem _events;
        Material _mat;

        public void Bind(EventSystem events)
        {
            _events = events;
        }

        void Awake()
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _mat = new Material(shader) { hideFlags = HideFlags.DontSave };
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }

        void OnRenderObject()
        {
            if (_events == null || _mat == null) return;
            var active = _events.Active;
            if (active.Count == 0) return;

            var cam = Camera.main;
            if (cam == null) return;

            _mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
            GL.Begin(GL.QUADS);

            float alpha = 0.45f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * 3.5f));
            float cs = CellSize;

            foreach (var ev in active)
            {
                Color c = EventColor(ev.Type);
                c.a = alpha;
                GL.Color(c);
                float x = (ev.Cell.x + 0.5f) * cs;
                float y = (ev.Cell.y + 0.5f) * cs;
                float s = cs * 0.42f;
                GL.Vertex3(x - s, y - s, 0f);
                GL.Vertex3(x + s, y - s, 0f);
                GL.Vertex3(x + s, y + s, 0f);
                GL.Vertex3(x - s, y + s, 0f);
            }

            GL.End();
            GL.PopMatrix();
        }

        static Color EventColor(EventType t)
        {
            switch (t)
            {
                case EventType.Fire:    return new Color(1f, 0.18f, 0.05f);
                case EventType.Disease: return new Color(1f, 0.75f, 0.0f);
                case EventType.Crime:   return new Color(0.45f, 0.18f, 0.95f);
                default:                return Color.red;
            }
        }
    }
}
