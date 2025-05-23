// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Numerics;
using System.Text.Json;

using Nethermind.Core.Extensions;
using Nethermind.Int256;

namespace Nethermind.Abi
{
    public class AbiUInt : AbiType
    {
        private const int MaxSize = 256;
        private const int MinSize = 0;

        public static new readonly AbiUInt UInt8 = new(8);
        public static new readonly AbiUInt UInt16 = new(16);
        public static new readonly AbiUInt UInt32 = new(32);
        public static new readonly AbiUInt UInt64 = new(64);
        public static new readonly AbiUInt UInt96 = new(96);
        public static new readonly AbiUInt UInt256 = new(256);

        private static readonly byte[][] PrealocatedBytes =
            Enumerable.Range(0, 256).Select(x => new[] { (byte)x }).ToArray();

        public AbiUInt(int length)
        {
            ThrowIfNotMultipleOf8(length);

            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxSize);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, MinSize);

            Length = length;
            Name = $"uint{Length}";
            CSharpType = GetCSharpType();
        }

        public int Length { get; }

        public int LengthInBytes => Length / 8;

        public override string Name { get; }

        public override (object, int) Decode(byte[] data, int position, bool packed)
        {
            var (value, length) = DecodeUInt(data, position, packed);

            return Length switch
            {
                { } n when n <= 8 => ((object, int))((byte)value, length),
                { } n when n <= 16 => ((object, int))((ushort)value, length),
                { } n when n <= 32 => ((object, int))((uint)value, length),
                { } n when n <= 64 => ((object, int))((ulong)value, length),
                _ => ((object, int))(value, length),
            };
        }

        public (UInt256, int) DecodeUInt(byte[] data, int position, bool packed)
        {
            int length = (packed ? LengthInBytes : UInt256.LengthInBytes);
            UInt256 lengthData = new(data.Slice(position, length), true);
            return (lengthData, position + length);
        }

        public override byte[] Encode(object? arg, bool packed)
        {
            Span<byte> bytes;
            if (arg is UInt256 uint256)
            {
                bytes = ((BigInteger)uint256).ToBigEndianByteArray();
            }
            else if (arg is BigInteger bigInteger)
            {
                bytes = bigInteger.ToBigEndianByteArray();
            }
            else if (arg is int intInput)
            {
                bytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(bytes, intInput);
            }
            else if (arg is uint uintInput)
            {
                bytes = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(bytes, uintInput);
            }
            else if (arg is long longInput)
            {
                bytes = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(bytes, longInput);
            }
            else if (arg is ulong ulongInput)
            {
                bytes = new byte[8];
                BinaryPrimitives.WriteUInt64BigEndian(bytes, ulongInput);
            }
            else if (arg is short shortInput)
            {
                bytes = new byte[8];
                BinaryPrimitives.WriteInt16BigEndian(bytes, shortInput);
            }
            else if (arg is ushort ushortInput)
            {
                bytes = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(bytes, ushortInput);
            }
            else if (arg is byte byteInput)
            {
                bytes = PrealocatedBytes[byteInput];
            }
            else if (arg is JsonElement element && element.ValueKind == JsonValueKind.Number)
            {
                bytes = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(bytes, element.GetInt64());
            }
            else
            {
                throw new AbiException(AbiEncodingExceptionMessage);
            }

            return bytes.PadLeft(packed ? LengthInBytes : UInt256.LengthInBytes);
        }

        public override Type CSharpType { get; }

        private Type GetCSharpType()
        {
            return Length switch
            {
                { } n when n <= 8 => typeof(byte),
                { } n when n <= 16 => typeof(ushort),
                { } n when n <= 32 => typeof(uint),
                { } n when n <= 64 => typeof(ulong),
                _ => typeof(UInt256),
            };
        }
    }
}
