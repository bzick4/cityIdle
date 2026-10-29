using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>
    /// Штрих пальцем для дорог и каналов. После отпускания палца ничего не строится:
    /// штрих ждёт подтверждения (Pending), и игрок решает кнопками «Подтвердить» или «Отмена».
    /// </summary>
    public interface IRoadStroke
    {
        string Title { get; }
        bool Drawing { get; }
        bool Pending { get; }
        IReadOnlyList<Vector2Int> Path { get; }

        /// <summary>Полная цена штриха, без учёта того, хватает ли денег.</summary>
        float StrokeCost { get; }
        float Money { get; }

        /// <summary>Ширина дороги в долях клетки, для контура.</summary>
        float StrokeWidth { get; }
        Camera Cam { get; }

        /// <summary>Почему построить нельзя (null, если можно). Деньги сюда не входят.</summary>
        string Problem { get; }

        /// <summary>Длина штриха для отображения в диалоге подтверждения.</summary>
        string LengthLabel { get; }

        void Confirm();
        void Cancel();
        event Action<IReadOnlyList<Vector2Int>, float> Committed;
    }
}
