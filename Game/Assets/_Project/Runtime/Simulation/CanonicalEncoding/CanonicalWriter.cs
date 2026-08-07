using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CML.Foundation;

namespace CML.Simulation.CanonicalEncoding
{
    /// <summary>
    /// Minimal writer for the LC canonical schema: shortest unsigned LEB128,
    /// ZigZag signed values and length-prefixed normalized data.
    /// </summary>
    internal sealed class CanonicalWriter : IDisposable
    {
        private readonly MemoryStream _stream = new MemoryStream();

        public long Length => _stream.Length;

        public void WriteFieldCount(ulong count)
        {
            WriteUnsigned(count);
        }

        public void WriteTag(ulong tag)
        {
            if (tag == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(tag), "Canonical field tags start at one.");
            }

            WriteUnsigned(tag);
        }

        public void WriteUnsigned(ulong value)
        {
            do
            {
                var next = (byte)(value & 0x7FUL);
                value >>= 7;
                if (value != 0UL)
                {
                    next |= 0x80;
                }

                _stream.WriteByte(next);
            }
            while (value != 0UL);
        }

        public void WriteUnsigned(Unsigned128 value)
        {
            var currentHigh = value.High;
            var currentLow = value.Low;
            do
            {
                var next = (byte)(currentLow & 0x7FUL);
                var previousHigh = currentHigh;
                currentHigh >>= 7;
                currentLow = (currentLow >> 7) | (previousHigh << 57);
                if (currentHigh != 0UL || currentLow != 0UL)
                {
                    next |= 0x80;
                }

                _stream.WriteByte(next);
            }
            while (currentHigh != 0UL || currentLow != 0UL);
        }

        public void WriteSigned(long value)
        {
            var zigZag = unchecked((ulong)((value << 1) ^ (value >> 63)));
            WriteUnsigned(zigZag);
        }

        public void WriteBoolean(bool value)
        {
            _stream.WriteByte(value ? (byte)1 : (byte)0);
        }

        public void WriteString(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var bytes = Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC));
            WriteBytes(bytes);
        }

        public void WriteBytes(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            WriteUnsigned((ulong)bytes.Length);
            _stream.Write(bytes, 0, bytes.Length);
        }

        public void WriteBytes(IReadOnlyList<byte> bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            WriteUnsigned((ulong)bytes.Count);
            for (var index = 0; index < bytes.Count; index++)
            {
                _stream.WriteByte(bytes[index]);
            }
        }

        public byte[] ToArray()
        {
            return _stream.ToArray();
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}
