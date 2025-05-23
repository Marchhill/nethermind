// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nethermind.Serialization.Json;

public class BloomConverter : JsonConverter<Bloom>
{
    public override Bloom? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        byte[]? bytes = ByteArrayConverter.Convert(ref reader);
        return bytes is null ? null : new Bloom(bytes);
    }

    public override void Write(
        Utf8JsonWriter writer,
        Bloom bloom,
        JsonSerializerOptions options)
    {
        ByteArrayConverter.Convert(writer, bloom.Bytes, skipLeadingZeros: false);
    }
}
