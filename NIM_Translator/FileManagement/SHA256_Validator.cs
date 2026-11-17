using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NIM.FileManagement
{
    public static class SHA256_Validator
    {
        /// <summary>
        /// Computes the file hash of the file at the given path.
        /// </summary>
        /// <param name="FilePath">Full or relative path to the file.</param>
        /// <returns>Lowercase hex hash string (64 characters).</returns>
        public static string ComputeFileSHA256(string FilePath)
        {
            if (string.IsNullOrWhiteSpace(FilePath))
                throw new ArgumentNullException(nameof(FilePath), "File path cannot be null or empty.");

            if (!File.Exists(FilePath))
                throw new FileNotFoundException("File not found.", FilePath);

            using (SHA256 Sha = SHA256.Create())
            using (FileStream Stream = File.OpenRead(FilePath))
            {
                byte[] Hash = Sha.ComputeHash(Stream);
                return ToHex(Hash);
            }
        }

        /// <summary>
        /// Verifies that the file at <paramref name="FilePath"/> matches the expected hash.
        /// </summary>
        public static bool VerifyFileSHA256(string FilePath, string ExpectedSHA256)
        {
            if (string.IsNullOrWhiteSpace(ExpectedSHA256))
                throw new ArgumentNullException(nameof(ExpectedSHA256));

            string Actual = ComputeFileSHA256(FilePath);
            return string.Equals(Actual, ExpectedSHA256.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Computes the hash of an in-memory byte array.
        /// </summary>
        public static string ComputeBytesSHA256(byte[] Data)
        {
            if (Data == null)
                throw new ArgumentNullException(nameof(Data));

            using (SHA256 Sha = SHA256.Create())
            {
                byte[] Hash = Sha.ComputeHash(Data);
                return ToHex(Hash);
            }
        }

        /// <summary>
        /// Computes the hash of a UTF-8 string.
        /// </summary>
        public static string ComputeStringSHA256(string Text)
        {
            if (Text == null)
                throw new ArgumentNullException(nameof(Text));

            return ComputeBytesSHA256(Encoding.UTF8.GetBytes(Text));
        }

        private static string ToHex(byte[] Bytes)
        {
            StringBuilder NStringBuilder = new StringBuilder(Bytes.Length * 2);
            foreach (byte B in Bytes)
                NStringBuilder.Append(B.ToString("x2"));
            return NStringBuilder.ToString();
        }
    }
}
