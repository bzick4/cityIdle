using UnityEngine;

namespace CityGame.Roads
{
    /// <summary>
    /// Отладочная сетка шестиугольных секторов и портов на гранях.
    /// Видна в окне Scene, а в окне Game при включённых Gizmos. Само на игру не влияет.
    /// </summary>
    public sealed class SectorGizmos : MonoBehaviour
    {
        [Tooltip("Расстояние от центра сектора до угла, в мировых единицах")]
        public float HexSize = 5f;

        [Tooltip("Сколько колец секторов вокруг центрального рисовать")]
        public int Rings = 2;

        public bool ShowPorts = true;

        void OnDrawGizmos()
        {
            for (int q = -Rings; q <= Rings; q++)
            {
                for (int r = -Rings; r <= Rings; r++)
                {
                    if (Mathf.Max(Mathf.Abs(q), Mathf.Abs(r), Mathf.Abs(q + r)) > Rings) continue;
                    DrawHex(new HexCoord(q, r));
                }
            }
        }

        void DrawHex(HexCoord h)
        {
            Gizmos.color = h == new HexCoord(0, 0) ? new Color(0.1f, 0.15f, 0.2f, 1f) : new Color(0.6f, 0.65f, 0.7f, 1f);
            for (int i = 0; i < 6; i++)
            {
                Vector2 a = HexMath.Corner(h, i, HexSize);
                Vector2 b = HexMath.Corner(h, (i + 1) % 6, HexSize);
                Gizmos.DrawLine(a, b);
            }

            if (!ShowPorts) return;
            Gizmos.color = new Color(0.9f, 0.45f, 0.2f, 1f);
            for (int d = 0; d < 6; d++)
                Gizmos.DrawWireSphere(HexMath.EdgeMidpoint(h, d, HexSize), 0.15f);
        }
    }
}
