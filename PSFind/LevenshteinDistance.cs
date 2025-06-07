using System;

namespace PSFind;

public static class LevenshteinDistance
{
    public static int GetDistance(string a, string b)
    {
        // Implementation of the "Iterative with two matrix rows" algorithm on https://en.wikipedia.org/wiki/Levenshtein_distance

        int[] v0 = new int[b.Length + 1];
        int[] v1 = new int[b.Length + 1];

        for (int i = 0; i < b.Length; i++)
        {
            v0[i] = i;
        }

        for (int i = 0; i < a.Length; i++)
        {
            v1[0] = i + 1;

            for(int j = 0; j < b.Length; j++)
            {
                int deletionCost = v0[j + 1] + 1;
                int insertionCost = v1[j] + 1;
                int substitutionCost = a[i] == b[j] ? v0[j] : v0[j] + 1;

                v1[j + 1] = Min(deletionCost, insertionCost, substitutionCost);
            }

            Array.Copy(v1, v0, v0.Length);
        }

        return v0[^2];
    }

    static int Min(int a, int b, int c) => Math.Min(Math.Min(a, b), c);
}
