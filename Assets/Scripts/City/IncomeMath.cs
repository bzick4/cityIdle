namespace CityGame.Buildings
{
    /// <summary>
    /// Правило дохода слияния: район из n ячеек приносит n·X, а если n кратно 3, то ещё +X.
    /// 1→1, 2→2, 3→4, 4→4, 5→5, 6→7, 7→7, 8→8, 9→10 ...
    /// </summary>
    public static class IncomeMath
    {
        public static int Multiplier(int n)
        {
            if (n <= 0) return 0;
            return n % 3 == 0 ? n + 1 : n;
        }
    }
}
