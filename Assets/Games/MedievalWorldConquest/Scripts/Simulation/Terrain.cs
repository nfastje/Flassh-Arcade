using System;

namespace MedievalWorldConquest.Simulation
{
    public enum TerrainType
    {
        Grass,
        Forest,
        Hills,
        Water,
    }

    /// <summary>
    /// The world's landscape, worked out from the world seed rather than saved: the same seed always gives the same
    /// map. Uses its own noise (not Unity's) so it's plain C# and identical everywhere it's computed.
    /// </summary>
    public static class Terrain
    {
        /// <summary>Fields this close to the map centre are always open grass, so the player never starts in a lake.</summary>
        public const double StartClearingRadius = 4;

        public static TerrainType At(int seed, int x, int y)
        {
            if (x < 0 || y < 0 || x >= World.MapSize || y >= World.MapSize) return TerrainType.Water;
            double c = World.MapSize / 2.0;
            if ((x - c) * (x - c) + (y - c) * (y - c) < StartClearingRadius * StartClearingRadius) return TerrainType.Grass;

            double height = Fbm(seed, x * 0.07, y * 0.07);
            if (height < 0.36) return TerrainType.Water;
            if (height > 0.66) return TerrainType.Hills;
            if (Fbm(seed + 7919, x * 0.11, y * 0.11) > 0.56) return TerrainType.Forest;
            return TerrainType.Grass;
        }

        /// <summary>Smooth noise in 0..1, layered at several scales.</summary>
        public static double Fbm(int seed, double x, double y)
        {
            double sum = 0, amplitude = 0.5, norm = 0, frequency = 1;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += amplitude * ValueNoise(seed + octave * 131, x * frequency, y * frequency);
                norm += amplitude;
                amplitude *= 0.5;
                frequency *= 2;
            }
            return sum / norm;
        }

        static double ValueNoise(int seed, double x, double y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy); // smoothstep
            double a = Hash(seed, x0, y0), b = Hash(seed, x0 + 1, y0);
            double c = Hash(seed, x0, y0 + 1), d = Hash(seed, x0 + 1, y0 + 1);
            return Lerp(Lerp(a, b, sx), Lerp(c, d, sx), sy);
        }

        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>A repeatable pseudo-random number in 0..1 for a grid point (or any pair of numbers).</summary>
        internal static double Hash(int seed, int x, int y)
        {
            unchecked
            {
                uint h = (uint)seed * 374761393u + (uint)x * 668265263u + (uint)y * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }
}
