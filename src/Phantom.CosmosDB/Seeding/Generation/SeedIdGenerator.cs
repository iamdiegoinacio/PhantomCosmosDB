using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Phantom.CosmosDB.Seeding.Generation;

/// <summary>
/// Gera identificadores (<c>id</c>) em múltiplos formatos (Auto, Prefixo Sequencial,
/// GUID v4, GUID v5 determinístico/randômico, GUID v7 ordenável, ULID e string numérica).
/// </summary>
internal static class SeedIdGenerator
{
    public static readonly Guid DefaultNamespaceGuid = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
    private const string CrockfordBase32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static (SeedIdStrategy Strategy, string Prefix, long NextIndex) InferFromTemplateId(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            return (SeedIdStrategy.GuidV7, "item-", 1);

        if (Guid.TryParse(templateId, out var parsedGuid))
        {
            var version = (parsedGuid.ToByteArray()[7] >> 4) & 0x0F;
            return version switch
            {
                7 => (SeedIdStrategy.GuidV7, string.Empty, 1),
                5 => (SeedIdStrategy.GuidV5, string.Empty, 1),
                _ => (SeedIdStrategy.GuidV4, string.Empty, 1)
            };
        }

        if (long.TryParse(templateId, out var num))
            return (SeedIdStrategy.NumericString, string.Empty, num + 1);

        var match = Regex.Match(templateId, @"^(.*?)(\d+)$");
        if (match.Success)
        {
            var prefix = match.Groups[1].Value;
            var number = long.TryParse(match.Groups[2].Value, out var n) ? n + 1 : 1;
            return (SeedIdStrategy.PrefixSequential, prefix, number);
        }

        return (SeedIdStrategy.PrefixSequential, templateId + "-", 1);
    }

    public static string GenerateId(
        SeedIdStrategy strategy,
        string containerName,
        long sequenceIndex,
        string? prefix,
        GuidV5InputMode guidV5Mode,
        string? guidV5Template,
        Guid? guidV5Namespace,
        JObject generatedDocument,
        Random random)
    {
        return strategy switch
        {
            SeedIdStrategy.PrefixSequential => $"{prefix ?? "item-"}{sequenceIndex}",
            SeedIdStrategy.NumericString => sequenceIndex.ToString(),
            SeedIdStrategy.GuidV4 => CreateGuidV4(random).ToString("D"),
            SeedIdStrategy.GuidV7 => CreateGuidV7(random, sequenceIndex).ToString("D"),
            SeedIdStrategy.Ulid => CreateUlid(random, sequenceIndex),
            SeedIdStrategy.GuidV5 => CreateGuidV5(
                guidV5Namespace ?? DefaultNamespaceGuid,
                ResolveGuidV5Input(guidV5Mode, guidV5Template, containerName, sequenceIndex, generatedDocument, random)).ToString("D"),
            _ => $"{prefix ?? "item-"}{sequenceIndex}"
        };
    }

    public static Guid CreateGuidV4(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    public static Guid CreateGuidV5(Guid namespaceId, string name)
    {
        Span<byte> nsBytes = stackalloc byte[16];
        namespaceId.TryWriteBytes(nsBytes);
        SwapGuidByteOrder(nsBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var buffer = new byte[16 + nameBytes.Length];
        nsBytes.CopyTo(buffer);
        nameBytes.CopyTo(buffer.AsSpan(16));

        var hash = SHA1.HashData(buffer);
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);

        // RFC 4122: version 5 (0101) & variant (10xx) in network byte order
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        SwapGuidByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    public static Guid CreateGuidV7(Random random, long sequenceOffset = 0)
    {
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (sequenceOffset / 10_000);
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);

        // 48-bit big-endian Unix timestamp in bytes[0..5]
        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)unixMs;

        // Monotonicity hint in rand_a when generating tight loops
        var seq12 = (ushort)(sequenceOffset & 0x0FFF);
        bytes[6] = (byte)(0x70 | ((seq12 >> 8) & 0x0F));
        bytes[7] = (byte)(seq12 & 0xFF);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        SwapGuidByteOrder(bytes);
        return new Guid(bytes);
    }

    public static string CreateUlid(Random random, long sequenceOffset = 0)
    {
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Span<byte> data = stackalloc byte[16];
        data[0] = (byte)(unixMs >> 40);
        data[1] = (byte)(unixMs >> 32);
        data[2] = (byte)(unixMs >> 24);
        data[3] = (byte)(unixMs >> 16);
        data[4] = (byte)(unixMs >> 8);
        data[5] = (byte)unixMs;

        random.NextBytes(data[6..]);
        BinaryPrimitives.WriteUInt16BigEndian(data[6..8], (ushort)(sequenceOffset & 0xFFFF));

        Span<char> chars = stackalloc char[26];
        // Timestamp (48 bits -> 10 chars)
        chars[0] = CrockfordBase32[(data[0] & 0xE0) >> 5];
        chars[1] = CrockfordBase32[data[0] & 0x1F];
        chars[2] = CrockfordBase32[(data[1] & 0xF8) >> 3];
        chars[3] = CrockfordBase32[((data[1] & 0x07) << 2) | ((data[2] & 0xC0) >> 6)];
        chars[4] = CrockfordBase32[(data[2] & 0x3E) >> 1];
        chars[5] = CrockfordBase32[((data[2] & 0x01) << 4) | ((data[3] & 0xF0) >> 4)];
        chars[6] = CrockfordBase32[((data[3] & 0x0F) << 1) | ((data[4] & 0x80) >> 7)];
        chars[7] = CrockfordBase32[(data[4] & 0x7C) >> 2];
        chars[8] = CrockfordBase32[((data[4] & 0x03) << 3) | ((data[5] & 0xE0) >> 5)];
        chars[9] = CrockfordBase32[data[5] & 0x1F];

        // Randomness (80 bits -> 16 chars)
        for (var i = 0; i < 16; i++)
        {
            var bitOffset = i * 5;
            var byteIndex = 6 + (bitOffset / 8);
            var shift = 8 - (bitOffset % 8) - 5;
            int val = shift >= 0
                ? (data[byteIndex] >> shift) & 0x1F
                : ((data[byteIndex] << -shift) | (byteIndex + 1 < 16 ? data[byteIndex + 1] >> (8 + shift) : 0)) & 0x1F;
            chars[10 + i] = CrockfordBase32[val];
        }

        return new string(chars);
    }

    internal static string ResolveGuidV5Input(
        GuidV5InputMode mode,
        string? template,
        string containerName,
        long sequenceIndex,
        JObject doc,
        Random random)
    {
        if (mode == GuidV5InputMode.Random)
            return $"{containerName}:{sequenceIndex}:{CreateGuidV4(random):N}";

        var effectiveTemplate = string.IsNullOrWhiteSpace(template)
            ? "{container}:{index}"
            : template;

        return Regex.Replace(effectiveTemplate, @"\{([^{}]+)\}", match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (key.Equals("index", StringComparison.OrdinalIgnoreCase))
                return sequenceIndex.ToString();
            if (key.Equals("container", StringComparison.OrdinalIgnoreCase))
                return containerName;

            var token = doc.SelectToken(key);
            return token?.Type switch
            {
                null or JTokenType.Null => string.Empty,
                _ => token.ToString()
            };
        });
    }

    private static void SwapGuidByteOrder(Span<byte> bytes)
    {
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
    }
}
