using System;
using CityGame;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.Water
{
    /// <summary>
    /// Основа для штучных построек (порт, автостанция): одно касание, одна постройка.
    /// Провести пальцем нельзя, держать тоже: расширять такие постройки не нужно.
    /// </summary>
    public abstract class TapPlacer : MonoBehaviour
    {
        public Camera Cam;
        public float CellSize = 1f;
        public Func<Vector2, bool> IsPointerBlocked;
        public event Action<string> Message;

        bool _armed;

        protected void Say(string text)
        {
            Message?.Invoke(text);
        }

        protected Vector2 CenterOf(Vector2Int cell)
        {
            return new Vector2((cell.x + 0.5f) * CellSize, (cell.y + 0.5f) * CellSize);
        }

        /// <summary>Пробует поставить постройку на клетку.</summary>
        protected abstract void TryPlace(Vector2Int cell);

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
        }

        void OnDisable()
        {
            _armed = false;
        }

        void Update()
        {
            if (Cam == null) return;

            PointerInput.Poll();
            if (PointerInput.Gesture)
            {
                _armed = false;
                return;
            }

            if (PointerInput.Down)
            {
                _armed = !(IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position));
                return;
            }

            if (!_armed || !PointerInput.Up) return;
            _armed = false;

            Vector2 w = GroundRay.ToGround(PointerInput.Position, Cam);
            TryPlace(new Vector2Int(Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize)));
        }
    }
}
