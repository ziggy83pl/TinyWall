using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace pylorak.TinyWall
{
    public static class Hasher
    {
        public static string HashStream(Stream stream)
        {
            using SHA256Cng hasher = new();
            return Utils.HexEncode(hasher.ComputeHash(stream));
        }

        public static string HashFile(string filePath)
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
            return HashStream(fs);
        }

        public static string HashString(string text)
        {
            using SHA256Cng hasher = new();
            return Utils.HexEncode(hasher.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        // ---------------------------------------------------------------
        // NAPRAWA BEZPIECZEŃSTWA #2: SHA1 oznaczony jako przestarzały
        // SHA1 jest kryptograficznie słaby od 2017 roku (atak SHAttered).
        // Zachowany tylko dla kompatybilności z istniejącymi profilami.
        // Nowy kod powinien używać HashFile() opartego na SHA256.
        // ---------------------------------------------------------------
        [System.Obsolete("SHA1 jest kryptograficznie słaby. Używaj HashFile() (SHA256) dla nowych danych.")]
        public static string HashFileSha1(string filePath)
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
#pragma warning disable CA5350 // Do not use SHA1 - retained for legacy profile compatibility only
            using SHA1Cng hasher = new();
#pragma warning restore CA5350
            return Utils.HexEncode(hasher.ComputeHash(fs));
        }

        /// <summary>
        /// Porównuje dwa hasze w czasie stałym (constant-time),
        /// zapobiegając atakom czasowym (timing attacks).
        /// </summary>
        public static bool SecureCompare(string hashA, string hashB)
        {
            if (hashA == null || hashB == null)
                return false;

            byte[] a = Encoding.UTF8.GetBytes(hashA);
            byte[] b = Encoding.UTF8.GetBytes(hashB);

            if (a.Length != b.Length)
                return false;

            // XOR wszystkich bajtów — nie wychodzi wcześniej przy różnicy
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];

            return diff == 0;
        }
    }
}
