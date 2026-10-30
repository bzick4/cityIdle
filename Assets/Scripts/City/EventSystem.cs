using System;
using System.Collections.Generic;
using CityGame.Buildings;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CityGame.City
{
    public enum EventType { Fire = 0, Disease = 1, Crime = 2 }

    public struct CityEvent
    {
        public EventType Type;
        public Vector2Int Cell;
        public float TimeLeft;
    }

    public sealed class EventSystem : MonoBehaviour
    {
        [SerializeField] float _minInterval = 45f;
        [SerializeField] float _maxInterval = 90f;
        [SerializeField] float _uncoveredTimer = 30f;

        const float CheckInterval = 0.5f;

        public event Action<string> Message;
        public IReadOnlyList<CityEvent> Active => _active;

        CityModel _model;
        ServiceCoverage _coverage;
        readonly List<CityEvent> _active = new List<CityEvent>();

        public void Bind(CityModel model, ServiceCoverage coverage)
        {
            _model = model;
            _coverage = coverage;
            StopAllCoroutines();
            StartCoroutine(SpawnLoop());
            StartCoroutine(CheckLoop());
        }

        System.Collections.IEnumerator SpawnLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(_minInterval, _maxInterval));
                if (_model != null) TrySpawnEvent();
            }
        }

        System.Collections.IEnumerator CheckLoop()
        {
            var wait = new WaitForSeconds(CheckInterval);
            while (true)
            {
                yield return wait;
                if (_model == null) continue;
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var ev = _active[i];
                    if (IsCovered(ev.Cell, ev.Type))
                    {
                        Message?.Invoke(EventName(ev.Type) + " ликвидирован");
                        _active.RemoveAt(i);
                        continue;
                    }
                    ev.TimeLeft -= CheckInterval;
                    _active[i] = ev;
                    if (ev.TimeLeft <= 0f)
                    {
                        Message?.Invoke(EventName(ev.Type) + ": здание уничтожено");
                        _model.Remove(ev.Cell);
                        _active.RemoveAt(i);
                    }
                }
            }
        }

        void TrySpawnEvent()
        {
            var candidates = new List<BuildingCell>();
            foreach (var c in _model.Cells)
                if (c.Category != BuildingCategory.Service) candidates.Add(c);
            if (candidates.Count == 0) return;

            var target = candidates[Random.Range(0, candidates.Count)];
            var type = (EventType)Random.Range(0, 3);

            if (IsCovered(target.Pos, type))
            {
                Message?.Invoke(EventName(type) + " — службы справились");
            }
            else
            {
                _active.Add(new CityEvent { Type = type, Cell = target.Pos, TimeLeft = _uncoveredTimer });
                Message?.Invoke(EventName(type) + "! Нет нужной службы рядом");
            }
        }

        bool IsCovered(Vector2Int cell, EventType type)
        {
            if (_coverage == null) return false;
            int bit = 1 << (int)ServiceFor(type);
            return (_coverage.MaskAt(cell) & bit) != 0;
        }

        static ServiceKind ServiceFor(EventType t)
        {
            switch (t)
            {
                case EventType.Fire:    return ServiceKind.Fire;
                case EventType.Disease: return ServiceKind.Ambulance;
                case EventType.Crime:   return ServiceKind.Police;
                default:                return ServiceKind.Fire;
            }
        }

        static string EventName(EventType t)
        {
            switch (t)
            {
                case EventType.Fire:    return "Пожар";
                case EventType.Disease: return "Эпидемия";
                case EventType.Crime:   return "Преступность";
                default:                return "Событие";
            }
        }
    }
}
