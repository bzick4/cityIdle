using UnityEngine;

namespace CityGame.Roads
{
    /// <summary>
    /// Точка входа шага 1. Повесьте на пустой объект: он сам создаст граф дорог,
    /// отрисовку и рисование пальцем.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoadSystem : MonoBehaviour
    {
        public RoadGraph Graph { get; private set; }
        public RoadDrawer Drawer { get; private set; }

        void Awake()
        {
            // Все меши строятся в мировых координатах и лежат дочерними объектами этого объекта.
            // Если объект стоит не в нуле, картинка уезжает относительно ввода. Поэтому обнуляем.
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            Graph = new RoadGraph();

            var roadRenderer = GetComponent<RoadRenderer>();
            if (roadRenderer == null) roadRenderer = gameObject.AddComponent<RoadRenderer>();
            roadRenderer.Bind(Graph);

            Drawer = GetComponent<RoadDrawer>();
            if (Drawer == null) Drawer = gameObject.AddComponent<RoadDrawer>();
            Drawer.Bind(Graph);
        }

        // Эти методы можно вызывать из кнопок интерфейса (UnityEvent).
        public void UseLocalRoad()
        {
            Drawer.Type = RoadType.Local;
        }

        public void UseHighway()
        {
            Drawer.Type = RoadType.Highway;
        }
    }
}
