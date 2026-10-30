using CityGame.Roads;
using System;
using UnityEngine;

namespace CityGame.Sectors
{
    /// <summary>
    /// Режим «Расширить»: тап по доступному закрытому сектору выделяет его,
    /// дальше интерфейс спрашивает подтверждение и вызывает ConfirmPending.
    /// </summary>
    public sealed class SectorTapper : MonoBehaviour
    {
        [SerializeField] Camera _cam;
        public Camera Cam => _cam;
        public Func<Vector2, bool> IsPointerBlocked;

        SectorMap _map;
        SectorRenderer _renderer;

        public bool HasPending => _renderer != null && _renderer.Pending.HasValue;

        /// <summary>Выбранный сектор, ожидающий подтверждения.</summary>
        public HexCoord? PendingHex => _renderer != null ? _renderer.Pending : null;

        public void Bind(SectorMap map, SectorRenderer view)
        {
            _map = map;
            _renderer = view;
        }

        void Awake()
        {
            if (_cam == null) _cam = Camera.main;
        }

        void OnDisable()
        {
            if (_renderer != null) _renderer.ClearPending();
        }

        public void ConfirmPending()
        {
            if (!HasPending) return;
            HexCoord h = _renderer.Pending.Value;
            _renderer.SetPending(null);
            if (!_map.CanOpen(h)) return;
            _renderer.BeginDrop(h);   // сначала анимация, потом открытие: рендереры увидят «падающий» сектор
            _map.TryOpen(h);
        }

        public void CancelPending()
        {
            if (_renderer != null) _renderer.SetPending(null);
        }

        void Update()
        {
            if (_map == null || _cam == null) return;

            PointerInput.Poll();
            if (PointerInput.Gesture || !PointerInput.Down) return;
            if (IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position)) return;

            Vector3 w = _cam.ScreenToWorldPoint(new Vector3(PointerInput.Position.x, PointerInput.Position.y,
                -_cam.transform.position.z));
            HexCoord h = _map.SectorAt(new Vector2(w.x, w.y));
            _renderer.SetPending(_map.CanOpen(h) ? h : (HexCoord?)null);
        }
    }
}
