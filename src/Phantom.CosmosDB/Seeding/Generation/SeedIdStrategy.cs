using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Phantom.CosmosDB.Seeding.Generation;

[JsonConverter(typeof(StringEnumConverter))]
public enum SeedIdStrategy
{
    Auto = 0,
    PrefixSequential = 1,
    GuidV4 = 2,
    GuidV5 = 3,
    GuidV7 = 4,
    Ulid = 5,
    NumericString = 6
}

[JsonConverter(typeof(StringEnumConverter))]
public enum GuidV5InputMode
{
    DeterministicTemplate = 0,
    Random = 1
}
