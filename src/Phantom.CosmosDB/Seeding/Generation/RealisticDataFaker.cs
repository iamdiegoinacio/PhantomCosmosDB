using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Phantom.CosmosDB.Seeding.Generation;

/// <summary>
/// Gerador leve e determinístico (quando recebe <see cref="Random"/> com seed)
/// de nomes, e-mails, CPFs/CNPJs válidos, regiões, telefones e valores de domínio.
/// </summary>
internal static class RealisticDataFaker
{
    private static readonly string[] FirstNames =
    [
        "Diego", "Lucas", "Mariana", "Ana", "Rafael", "Camila", "Bruno", "Juliana",
        "Gabriel", "Larissa", "Matheus", "Beatriz", "Thiago", "Fernanda", "Felipe", "Carla",
        "Gustavo", "Patricia", "Eduardo", "Renata", "Rodrigo", "Amanda", "Daniel", "Sofia"
    ];

    private static readonly string[] LastNames =
    [
        "Silva", "Santos", "Oliveira", "Souza", "Pereira", "Costa", "Rodrigues", "Almeida",
        "Nascimento", "Lima", "Araujo", "Fernandes", "Carvalho", "Gomes", "Martins", "Rocha",
        "Ribeiro", "Alves", "Monteiro", "Mendes", "Barros", "Freitas", "Cardoso", "Teixeira"
    ];

    private static readonly string[] Regions = ["norte", "nordeste", "centro-oeste", "sudeste", "sul"];
    private static readonly string[] States = ["SP", "RJ", "MG", "RS", "PR", "SC", "BA", "PE", "CE", "DF"];
    private static readonly string[] Statuses = ["pending", "paid", "processing", "shipped", "completed", "cancelled"];
    private static readonly string[] Categories = ["eletronicos", "livros", "acessorios", "casa", "games", "escritorio"];
    private static readonly string[] LogLevels = ["Info", "Warning", "Error", "Debug"];
    private static readonly string[] ProductAdjectives = ["Pro", "Ultra", "Compact", "Wireless", "Ergonômico", "Smart", "Plus", "Studio"];
    private static readonly string[] ProductNouns = ["Teclado", "Mouse", "Monitor", "Headset", "Webcam", "Hub USB-C", "Notebook", "Suporte"];

    public static string NextFullName(Random random)
    {
        var first = FirstNames[random.Next(FirstNames.Length)];
        var last = LastNames[random.Next(LastNames.Length)];
        return $"{first} {last}";
    }

    public static string NextEmail(Random random, string? fullName, long seq)
    {
        var basePart = string.IsNullOrWhiteSpace(fullName)
            ? $"usuario{seq}"
            : NormalizeSlug(fullName).Replace(' ', '.');
        return $"{basePart}.{seq}@exemplo.com";
    }

    public static string NextCpf(Random random, bool formatted = true)
    {
        Span<int> digits = stackalloc int[11];
        for (var i = 0; i < 9; i++) digits[i] = random.Next(0, 10);

        digits[9] = ComputeMod11(digits[..9], 10);
        digits[10] = ComputeMod11(digits[..10], 11);

        return formatted
            ? $"{digits[0]}{digits[1]}{digits[2]}.{digits[3]}{digits[4]}{digits[5]}.{digits[6]}{digits[7]}{digits[8]}-{digits[9]}{digits[10]}"
            : string.Concat(digits.ToArray());
    }

    public static string NextCnpj(Random random, bool formatted = true)
    {
        Span<int> d = stackalloc int[14];
        for (var i = 0; i < 8; i++) d[i] = random.Next(0, 10);
        d[8] = 0; d[9] = 0; d[10] = 0; d[11] = 1;

        int[] w1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] w2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        d[12] = ComputeWeightedMod11(d[..12], w1);
        d[13] = ComputeWeightedMod11(d[..13], w2);

        return formatted
            ? $"{d[0]}{d[1]}.{d[2]}{d[3]}{d[4]}.{d[5]}{d[6]}{d[7]}/{d[8]}{d[9]}{d[10]}{d[11]}-{d[12]}{d[13]}"
            : string.Concat(d.ToArray());
    }

    public static string NextPhone(Random random) =>
        $"(11) 9{random.Next(1000, 9999)}-{random.Next(1000, 9999)}";

    public static string NextStringForField(
        string fieldName,
        string? sampleValue,
        IReadOnlyList<string>? observedPool,
        long sequenceIndex,
        Random random,
        string? generatedNameContext)
    {
        var lower = fieldName.ToLowerInvariant();

        if (lower.Contains("email"))
            return NextEmail(random, generatedNameContext, sequenceIndex);

        if (lower.Contains("cpf"))
            return NextCpf(random, sampleValue?.Contains('.') ?? true);

        if (lower.Contains("cnpj"))
            return NextCnpj(random, sampleValue?.Contains('.') ?? true);

        if (lower.Contains("telefone") || lower.Contains("phone") || lower.Contains("celular"))
            return NextPhone(random);

        if (lower is "nome" or "name" or "fullname" or "cliente" or "autor" or "usuario")
        {
            if (sampleValue != null && sampleValue.Contains(' ') && !lower.Contains("produto"))
                return NextFullName(random);
            return lower.Contains("produto")
                ? $"{ProductNouns[random.Next(ProductNouns.Length)]} {ProductAdjectives[random.Next(ProductAdjectives.Length)]}"
                : NextFullName(random);
        }

        if (lower.Contains("sku"))
            return $"SKU-{sequenceIndex:D6}";

        if (lower.Contains("regiao") || lower.Contains("region"))
            return PickFromPoolOrDefault(observedPool, Regions, random);

        if (lower is "uf" or "estado" or "state")
            return PickFromPoolOrDefault(observedPool, States, random);

        if (lower.Contains("status"))
            return PickFromPoolOrDefault(observedPool, Statuses, random);

        if (lower.Contains("categoria") || lower.Contains("category"))
            return PickFromPoolOrDefault(observedPool, Categories, random);

        if (lower.Contains("nivel") || lower.Contains("level"))
            return PickFromPoolOrDefault(observedPool, LogLevels, random);

        if (lower.Contains("tenant"))
            return observedPool is { Count: > 0 }
                ? observedPool[random.Next(observedPool.Count)]
                : $"tenant-{(sequenceIndex % 10) + 1}";

        if (observedPool is { Count: > 1 })
            return observedPool[random.Next(observedPool.Count)];

        if (!string.IsNullOrWhiteSpace(sampleValue) && DateTimeOffset.TryParse(sampleValue, out _))
            return DateTimeOffset.UtcNow.AddMinutes(-random.Next(0, 60 * 24 * 90)).ToString("O");

        if (!string.IsNullOrWhiteSpace(sampleValue) && sampleValue.Contains('-') && sampleValue.Length <= 24)
        {
            var prefix = sampleValue.Split('-')[0];
            return $"{prefix}-{sequenceIndex}";
        }

        return $"{fieldName}-{sequenceIndex}";
    }

    private static string PickFromPoolOrDefault(IReadOnlyList<string>? observed, string[] defaults, Random random)
    {
        if (observed is { Count: > 1 })
            return observed[random.Next(observed.Count)];
        return defaults[random.Next(defaults.Length)];
    }

    private static int ComputeMod11(ReadOnlySpan<int> digits, int startWeight)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
            sum += digits[i] * (startWeight - i);
        var rem = sum % 11;
        return rem < 2 ? 0 : 11 - rem;
    }

    private static int ComputeWeightedMod11(ReadOnlySpan<int> digits, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
            sum += digits[i] * weights[i];
        var rem = sum % 11;
        return rem < 2 ? 0 : 11 - rem;
    }

    private static string NormalizeSlug(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
