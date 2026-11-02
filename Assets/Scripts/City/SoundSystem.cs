using System;
using UnityEngine;

namespace CityGame
{
    /// <summary>
    /// Процедурные звуковые эффекты. Все клипы генерируются в памяти — файлов не нужно.
    /// Bind вызывается из CitySystem после инициализации игры.
    /// </summary>
    public sealed class SoundSystem : MonoBehaviour
    {
        AudioSource _source;

        AudioClip _clipBuild;
        AudioClip _clipLevelUp;
        AudioClip _clipError;
        AudioClip _clipTap;

        void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake  = false;
            _source.spatialBlend = 0f;
            _source.volume       = 0.7f;

            _clipBuild   = MakeBuild();
            _clipLevelUp = MakeLevelUp();
            _clipError   = MakeError();
            _clipTap     = MakeTap();
        }

        public void Bind(CitySystem city)
        {
            city.Economy.Spent      += OnSpent;
            city.Economy.CantAfford += OnCantAfford;
            city.Levels.LevelUp     += OnLevelUp;
        }

        void OnDestroy()
        {
            // CitySystem может уже быть уничтожен — проверяем через поле.
        }

        public void PlayTap() => Play(_clipTap, 0.55f);

        void OnSpent()      => Play(_clipBuild,   1f);
        void OnCantAfford() => Play(_clipError,   0.8f);
        void OnLevelUp(int _) => Play(_clipLevelUp, 1f);

        void Play(AudioClip clip, float volume = 1f)
        {
            if (clip != null && _source != null)
                _source.PlayOneShot(clip, volume);
        }

        // ── Генераторы ────────────────────────────────────────────────────────

        // Мягкий «плоп»: синус со спадом частоты 400→140 Гц, затухание 0.18 с
        static AudioClip MakeBuild()
        {
            const int SR = 44100;
            const float Dur = 0.18f;
            int n = (int)(SR * Dur);
            var d = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t    = i / (float)SR;
                float freq = 400f * Mathf.Pow(140f / 400f, t / Dur); // экспоненциальный спад
                phase += 2.0 * Math.PI * freq / SR;
                float env = Mathf.Exp(-9f * t);
                d[i] = (float)(Math.Sin(phase) * env * 0.45f);
            }
            return Bake("Build", d, SR);
        }

        // Восходящий аккорд C5–E5–G5 (три ноты с перекрытием)
        static AudioClip MakeLevelUp()
        {
            const int SR = 44100;
            const float Dur = 0.55f;
            int n = (int)(SR * Dur);
            var d = new float[n];

            float[] freqs = { 523f, 659f, 784f }; // C5, E5, G5
            const float NoteLen = 0.18f;
            const float NoteStep = 0.13f;

            for (int note = 0; note < 3; note++)
            {
                int start = (int)(note * NoteStep * SR);
                int noteN = (int)(NoteLen * SR);
                double phase = 0;
                float freq = freqs[note];
                for (int i = 0; i < noteN && start + i < n; i++)
                {
                    float t = i / (float)SR;
                    phase += 2.0 * Math.PI * freq / SR;
                    float env = Mathf.Sin(Mathf.PI * i / noteN); // Hann
                    d[start + i] += (float)(Math.Sin(phase) * env * 0.32f);
                }
            }
            return Bake("LevelUp", d, SR);
        }

        // Короткий низкий «бздынь» с вибрато — нет денег
        static AudioClip MakeError()
        {
            const int SR = 44100;
            const float Dur = 0.22f;
            int n = (int)(SR * Dur);
            var d = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t    = i / (float)SR;
                float freq = 110f + 7f * Mathf.Sin(2f * Mathf.PI * 22f * t); // вибрато
                phase += 2.0 * Math.PI * freq / SR;
                float env = Mathf.Exp(-7f * t);
                d[i] = (float)(Math.Sin(phase) * env * 0.42f);
            }
            return Bake("Error", d, SR);
        }

        // Лёгкий высокий «клик» для карточек
        static AudioClip MakeTap()
        {
            const int SR = 44100;
            const float Dur = 0.05f;
            int n = (int)(SR * Dur);
            var d = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t    = i / (float)SR;
                float freq = 1400f * Mathf.Pow(700f / 1400f, t / Dur);
                phase += 2.0 * Math.PI * freq / SR;
                float env = Mathf.Exp(-30f * t);
                d[i] = (float)(Math.Sin(phase) * env * 0.3f);
            }
            return Bake("Tap", d, SR);
        }

        static AudioClip Bake(string name, float[] data, int sampleRate)
        {
            var clip = AudioClip.Create(name, data.Length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
