using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CityGame.Roads
{
    /// <summary>
    /// Единая точка ввода: палец, мышь или перо. Работает и с новым Input System,
    /// и со старым Input Manager, и с обоими сразу.
    /// Если включён старый Input Manager, касания читаются из него в первую очередь:
    /// Unity Remote передаёт их туда с правильными координатами окна Game.
    /// Вызывать Poll() раз в кадр (можно и чаще, результат в кадре один и тот же).
    ///
    /// Одиночный указатель: Position, Pressed, Down, Up (первый палец или мышь).
    /// Жесты: Gesture = true, когда на экране два пальца и больше. Тогда FingerA и FingerB это их позиции,
    /// а Down у одиночного указателя гасится, чтобы инструменты не начинали штрих.
    /// Для проверки в редакторе: Scroll это колесо мыши (-1, 0, 1), SecondaryHeld это правая или средняя кнопка.
    /// </summary>
    public static class PointerInput
    {
        public static Vector2 Position { get; private set; }
        public static bool Pressed { get; private set; }
        public static bool Down { get; private set; }
        public static bool Up { get; private set; }

        public static int Fingers { get; private set; }
        public static bool Gesture { get; private set; }
        public static Vector2 FingerA { get; private set; }
        public static Vector2 FingerB { get; private set; }

        public static float Scroll { get; private set; }
        public static bool SecondaryHeld { get; private set; }

        /// <summary>Откуда пришёл последний ввод. Нужно для отладки.</summary>
        public static string Source { get; private set; } = "none";

        public static void Poll()
        {
            Fingers = 0;
            Gesture = false;
            Scroll = 0f;
            SecondaryHeld = false;

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                UnityEngine.Touch touch = Input.GetTouch(0);
                Position = touch.position;
                Down = touch.phase == UnityEngine.TouchPhase.Began;
                Up = touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled;
                Pressed = !Up;
                Source = "legacy touch";

                Fingers = Input.touchCount;
                if (Fingers >= 2)
                {
                    FingerA = Input.GetTouch(0).position;
                    FingerB = Input.GetTouch(1).position;
                    Gesture = true;
                    Down = false;
                }

                return;
            }
#endif

#if ENABLE_INPUT_SYSTEM
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                int n = 0;
                Vector2 a = default, b = default;
                foreach (var tc in touchscreen.touches)
                {
                    if (!tc.isInProgress) continue;
                    if (n == 0) a = tc.position.ReadValue();
                    else if (n == 1) b = tc.position.ReadValue();
                    n++;
                }

                var t = touchscreen.primaryTouch;
                if (n > 0 || t.press.isPressed || t.press.wasPressedThisFrame || t.press.wasReleasedThisFrame)
                {
                    Position = t.position.ReadValue();
                    Pressed = t.press.isPressed;
                    Down = t.press.wasPressedThisFrame;
                    Up = t.press.wasReleasedThisFrame;
                    Source = "input system touch";

                    Fingers = n;
                    if (n >= 2)
                    {
                        FingerA = a;
                        FingerB = b;
                        Gesture = true;
                        Down = false;
                    }

                    return;
                }
            }

            Pointer pointer = Pointer.current;
            if (pointer == null)
            {
                Pressed = false;
                Down = false;
                Up = false;
                return;
            }

            Position = pointer.position.ReadValue();
            Pressed = pointer.press.isPressed;
            Down = pointer.press.wasPressedThisFrame;
            Up = pointer.press.wasReleasedThisFrame;
            Source = "input system pointer";

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                float y = mouse.scroll.ReadValue().y;
                Scroll = Mathf.Abs(y) > 0.01f ? Mathf.Sign(y) : 0f;
                SecondaryHeld = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            Position = Input.mousePosition;
            Down = Input.GetMouseButtonDown(0);
            Up = Input.GetMouseButtonUp(0);
            Pressed = Input.GetMouseButton(0);
            Source = "legacy mouse";

            float sy = Input.mouseScrollDelta.y;
            Scroll = Mathf.Abs(sy) > 0.01f ? Mathf.Sign(sy) : 0f;
            SecondaryHeld = Input.GetMouseButton(1) || Input.GetMouseButton(2);
#endif
        }
    }
}
