using CityGame;
using CityGame.Buildings;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.City
{
    public sealed class BuildingTapper : MonoBehaviour
    {
        public float CellSize = 1f;
        public System.Func<Vector2, bool> IsPointerBlocked;

        public event System.Action<BuildingCell> Tapped;

        CityModel _model;
        bool _pressing;
        Vector2Int _pressCell;

        public void Bind(CityModel model)
        {
            _model = model;
        }

        Camera Cam => Camera.main;

        void Update()
        {
            if (_model == null) return;

            PointerInput.Poll();
            if (PointerInput.Gesture) { _pressing = false; return; }

            if (PointerInput.Down)
            {
                if (IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position)) return;
                _pressing = true;
                _pressCell = ToCell(PointerInput.Position);
            }

            if (PointerInput.Up && _pressing)
            {
                _pressing = false;
                Vector2Int rel = ToCell(PointerInput.Position);
                if (rel == _pressCell)
                {
                    BuildingCell cell = _model.Get(_pressCell);
                    if (cell != null) Tapped?.Invoke(cell);
                }
            }

            if (!PointerInput.Pressed) _pressing = false;
        }

        Vector2Int ToCell(Vector2 screen)
        {
            if (Cam == null) return Vector2Int.zero;
            Vector2 w = GroundRay.ToGround(screen, Cam);
            return new Vector2Int(Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize));
        }
    }
}
