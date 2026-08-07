using System;
using System.Security.Cryptography;
using System.Text;
using CML.Simulation.CanonicalEncoding;

namespace CML.Simulation
{
    public static class LogicalStateHasher
    {
        private static readonly byte[] DomainPrefix = Encoding.ASCII.GetBytes("LC-HLOGIC-v1");

        public static byte[] ComputeHash(SimulationState state)
        {
            var canonicalState = CanonicalStateSerializer.Serialize(state);
            var input = new byte[checked(DomainPrefix.Length + 1 + canonicalState.Length)];
            Buffer.BlockCopy(DomainPrefix, 0, input, 0, DomainPrefix.Length);
            input[DomainPrefix.Length] = 0;
            Buffer.BlockCopy(canonicalState, 0, input, DomainPrefix.Length + 1, canonicalState.Length);

            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(input);
            }
        }

        public static string ComputeHashHex(SimulationState state)
        {
            var hash = ComputeHash(state);
            var characters = new char[hash.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (var index = 0; index < hash.Length; index++)
            {
                characters[index * 2] = alphabet[hash[index] >> 4];
                characters[(index * 2) + 1] = alphabet[hash[index] & 0x0F];
            }

            return new string(characters);
        }
    }
}
