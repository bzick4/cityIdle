using UnityEngine;

namespace CityGame
{
    /// <summary>
    /// Преобразует экранную точку в мировую точку на плоскости Z=0.
    /// Работает корректно при любом угле наклона камеры (в том числе изометрическом).
    /// </summary>
    public static class GroundRay
    {
        static readonly Plane Ground = new Plane(Vector3.forward, Vector3.zero);

        /// <summary>Мировая точка на Z=0 под экранной координатой screen.</summary>
        public static Vector2 ToGround(Vector2 screen, Camera cam)
        {
            Ray ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            float t;
            if (!Ground.Raycast(ray, out t))
            {
                // Камера параллельна плоскости — fallback без изометрии
                Vector3 w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
                return new Vector2(w.x, w.y);
            }
            Vector3 hit = ray.GetPoint(t);
            return new Vector2(hit.x, hit.y);
        }

        /// <inheritdoc cref="ToGround(Vector2, Camera)"/>
        public static Vector3 ToGround3(Vector2 screen, Camera cam)
        {
            Vector2 g = ToGround(screen, cam);
            return new Vector3(g.x, g.y, 0f);
        }
    }
}
