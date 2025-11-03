namespace ComplexTweaks.Utilities.Extensions;
public static class RandomExtensions
{
    public static double NextDouble(this Random random, double min, double max)
        => random.NextDouble() * (max - min) + min;

    public static float NextFloat(this Random random, float min, float max) => (float)random.NextDouble(min, max);
}
