using System.Security.Cryptography;

namespace SharpTS.Compilation;

/// <summary>
/// Static implementation of scrypt key derivation (RFC 7914).
/// Used by both interpreter and compiled code.
/// </summary>
public static class ScryptImpl
{
    /// <summary>
    /// Derives a key using the scrypt key derivation function.
    /// </summary>
    public static byte[] DeriveBytes(byte[] password, byte[] salt, int N, int r, int p, int dkLen)
    {
        // Validate parameters
        if (N < 2 || (N & (N - 1)) != 0)
            throw new ArgumentException("N must be a power of 2 greater than 1", nameof(N));
        if (r < 1)
            throw new ArgumentException("r must be at least 1", nameof(r));
        if (p < 1)
            throw new ArgumentException("p must be at least 1", nameof(p));

        // Step 1: Generate initial data B using PBKDF2-HMAC-SHA256
        int blockSize = 128 * r;
        byte[] B = Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, HashAlgorithmName.SHA256, p * blockSize);

        // Step 2: Apply scryptROMix to each block
        for (int i = 0; i < p; i++)
        {
            byte[] block = new byte[blockSize];
            Array.Copy(B, i * blockSize, block, 0, blockSize);
            ScryptROMix(block, N, r);
            Array.Copy(block, 0, B, i * blockSize, blockSize);
        }

        // Step 3: Derive final key using PBKDF2-HMAC-SHA256
        return Rfc2898DeriveBytes.Pbkdf2(password, B, 1, HashAlgorithmName.SHA256, dkLen);
    }

    private static void ScryptROMix(byte[] B, int N, int r)
    {
        int blockSize = 128 * r;
        byte[][] V = new byte[N][];

        // Step 1: Store intermediate values in V
        for (int i = 0; i < N; i++)
        {
            V[i] = (byte[])B.Clone();
            ScryptBlockMix(B, r);
        }

        // Step 2: Mix with random lookups
        for (int i = 0; i < N; i++)
        {
            // Get last 64 bits as little-endian integer mod N
            long j = BitConverter.ToInt64(B, blockSize - 64) & (N - 1);
            if (j < 0) j += N;

            // XOR B with V[j]
            for (int k = 0; k < blockSize; k++)
                B[k] ^= V[j][k];

            ScryptBlockMix(B, r);
        }
    }

    private static void ScryptBlockMix(byte[] B, int r)
    {
        int blockSize = 128 * r;
        byte[] X = new byte[64];
        byte[] Y = new byte[blockSize];

        // Copy last 64-byte block to X
        Array.Copy(B, blockSize - 64, X, 0, 64);

        // Process 2r blocks
        for (int i = 0; i < 2 * r; i++)
        {
            // XOR X with current block
            for (int j = 0; j < 64; j++)
                X[j] ^= B[i * 64 + j];

            // Apply Salsa20/8 core
            Salsa20Core(X);

            // Copy to Y (even blocks first, then odd blocks)
            int destOffset = (i / 2) * 64 + (i % 2) * r * 64;
            Array.Copy(X, 0, Y, destOffset, 64);
        }

        Array.Copy(Y, 0, B, 0, blockSize);
    }

    private static void Salsa20Core(byte[] block)
    {
        // Convert bytes to uint32 array (little-endian)
        uint[] x = new uint[16];
        for (int i = 0; i < 16; i++)
            x[i] = BitConverter.ToUInt32(block, i * 4);

        uint[] original = (uint[])x.Clone();

        // 8 rounds (4 double-rounds)
        for (int i = 0; i < 4; i++)
        {
            // Column round
            x[4] ^= RotateLeft(x[0] + x[12], 7);
            x[8] ^= RotateLeft(x[4] + x[0], 9);
            x[12] ^= RotateLeft(x[8] + x[4], 13);
            x[0] ^= RotateLeft(x[12] + x[8], 18);

            x[9] ^= RotateLeft(x[5] + x[1], 7);
            x[13] ^= RotateLeft(x[9] + x[5], 9);
            x[1] ^= RotateLeft(x[13] + x[9], 13);
            x[5] ^= RotateLeft(x[1] + x[13], 18);

            x[14] ^= RotateLeft(x[10] + x[6], 7);
            x[2] ^= RotateLeft(x[14] + x[10], 9);
            x[6] ^= RotateLeft(x[2] + x[14], 13);
            x[10] ^= RotateLeft(x[6] + x[2], 18);

            x[3] ^= RotateLeft(x[15] + x[11], 7);
            x[7] ^= RotateLeft(x[3] + x[15], 9);
            x[11] ^= RotateLeft(x[7] + x[3], 13);
            x[15] ^= RotateLeft(x[11] + x[7], 18);

            // Row round
            x[1] ^= RotateLeft(x[0] + x[3], 7);
            x[2] ^= RotateLeft(x[1] + x[0], 9);
            x[3] ^= RotateLeft(x[2] + x[1], 13);
            x[0] ^= RotateLeft(x[3] + x[2], 18);

            x[6] ^= RotateLeft(x[5] + x[4], 7);
            x[7] ^= RotateLeft(x[6] + x[5], 9);
            x[4] ^= RotateLeft(x[7] + x[6], 13);
            x[5] ^= RotateLeft(x[4] + x[7], 18);

            x[11] ^= RotateLeft(x[10] + x[9], 7);
            x[8] ^= RotateLeft(x[11] + x[10], 9);
            x[9] ^= RotateLeft(x[8] + x[11], 13);
            x[10] ^= RotateLeft(x[9] + x[8], 18);

            x[12] ^= RotateLeft(x[15] + x[14], 7);
            x[13] ^= RotateLeft(x[12] + x[15], 9);
            x[14] ^= RotateLeft(x[13] + x[12], 13);
            x[15] ^= RotateLeft(x[14] + x[13], 18);
        }

        // Add original to result
        for (int i = 0; i < 16; i++)
            x[i] += original[i];

        // Convert back to bytes
        for (int i = 0; i < 16; i++)
        {
            byte[] bytes = BitConverter.GetBytes(x[i]);
            Array.Copy(bytes, 0, block, i * 4, 4);
        }
    }

    private static uint RotateLeft(uint value, int count)
    {
        return (value << count) | (value >> (32 - count));
    }
}
