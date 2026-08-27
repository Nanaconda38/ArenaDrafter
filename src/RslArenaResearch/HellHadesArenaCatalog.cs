using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RslArenaResearch;

public sealed record HellHadesArenaForm(int Form, int ArenaRating, ArenaRole Roles);

public sealed record HellHadesArenaChampion(
    int BaseId,
    int HellHadesPostId,
    string EnglishName,
    int Rarity,
    ArenaRole Roles,
    IReadOnlyList<HellHadesArenaForm> Forms,
    string SourceUrl,
    string SourceUpdated);

public sealed record HellHadesCatalogCompatibility(int Matched, int Missing, int Ignored, int[] RarityMismatchBaseIds);

public sealed class HellHadesArenaCatalog
{
    private const string ResourceName = "RslArenaResearch.Data.hellhades-arena-catalog.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IReadOnlyDictionary<int, HellHadesArenaChampion> champions;

    private HellHadesArenaCatalog(IReadOnlyDictionary<int, HellHadesArenaChampion> champions, DateTimeOffset generatedUtc)
    {
        this.champions = champions;
        GeneratedUtc = generatedUtc;
        RolesByBaseId = champions.ToDictionary(item => item.Key, item => item.Value.Roles);
    }

    public DateTimeOffset GeneratedUtc { get; }
    public IReadOnlyDictionary<int, ArenaRole> RolesByBaseId { get; }
    public int Count => champions.Count;

    public static HellHadesArenaCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The embedded HellHades Arena catalog is missing.");
        return Load(stream);
    }

    public static HellHadesArenaCatalog Load(Stream stream)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(stream, JsonOptions)
            ?? throw new InvalidDataException("The HellHades Arena catalog is empty.");
        if (file.Version != 1 || file.GeneratedUtc == default || file.Source != "https://hellhades.com/wp-json/hh-api/v3/raid/export"
            || file.Champions is null || file.Champions.Count is < 100 or > 2000)
            throw new InvalidDataException("The HellHades Arena catalog header is invalid.");

        var result = new Dictionary<int, HellHadesArenaChampion>();
        foreach (var row in file.Champions)
        {
            if (row.BaseId <= 0 || row.BaseId % 10 != 0 || row.HellHadesPostId <= 0 || string.IsNullOrWhiteSpace(row.EnglishName)
                || row.Rarity is < 3 or > 6 || string.IsNullOrWhiteSpace(row.SourceUrl) || !Uri.TryCreate(row.SourceUrl, UriKind.Absolute, out var source)
                || source.Scheme != Uri.UriSchemeHttps || source.Host != "hellhades.com" || string.IsNullOrWhiteSpace(row.SourceUpdated)
                || row.ArenaRoles is null || row.Forms is null || row.Forms.Count == 0)
                throw new InvalidDataException("The HellHades Arena catalog contains an invalid champion.");

            var roles = MapRoles(row.ArenaRoles);
            var forms = row.Forms.Select(form =>
            {
                if (form.Form is < 1 or > 4 || form.ArenaRating is < 0 or > 10 || form.ArenaRoles is null)
                    throw new InvalidDataException("The HellHades Arena catalog contains an invalid champion form.");
                return new HellHadesArenaForm(form.Form, form.ArenaRating, MapRoles(form.ArenaRoles));
            }).ToArray();
            if (forms.Select(form => form.Form).Distinct().Count() != forms.Length || (forms.Aggregate(ArenaRole.None, (all, form) => all | form.Roles) & roles) != roles)
                throw new InvalidDataException("The HellHades Arena catalog contains inconsistent champion forms.");
            if (!result.TryAdd(row.BaseId, new(row.BaseId, row.HellHadesPostId, row.EnglishName.Trim(), row.Rarity, roles, forms, row.SourceUrl, row.SourceUpdated)))
                throw new InvalidDataException("The HellHades Arena catalog contains a duplicate RAID Base ID.");
        }
        return new(result, file.GeneratedUtc);
    }

    public ArenaRole RolesFor(int baseId) => champions.TryGetValue(baseId, out var champion) ? champion.Roles : ArenaRole.None;

    public bool TryGetChampion(int baseId, out HellHadesArenaChampion? champion) => champions.TryGetValue(baseId, out champion);

    public HellHadesCatalogCompatibility ValidateAgainstRaid(IReadOnlyCollection<ChampionCatalogWire> raidCatalog)
    {
        var matched = 0;
        var missing = 0;
        var ignored = 0;
        var rarityMismatches = new List<int>();
        foreach (var raid in raidCatalog)
        {
            if (raid.Rarity is < 3 or > 6) { ignored++; continue; }
            if (!champions.TryGetValue(raid.BaseId, out var imported)) { missing++; continue; }
            if (imported.Rarity != raid.Rarity) rarityMismatches.Add(raid.BaseId);
            matched++;
        }
        return new(matched, missing, ignored, [.. rarityMismatches]);
    }

    private static ArenaRole MapRoles(IEnumerable<string> values)
    {
        var roles = ArenaRole.None;
        foreach (var value in values)
        {
            roles |= value switch
            {
                "cleanser" => ArenaRole.Cleanse | ArenaRole.Sustain | ArenaRole.Utility,
                "crowdcontrol" => ArenaRole.Control,
                "damageabsorption" => ArenaRole.Protection | ArenaRole.Sustain,
                "damagedealer" => ArenaRole.Damage,
                "debuffer" => ArenaRole.Opener | ArenaRole.Utility,
                "healer" => ArenaRole.Sustain,
                "reviver" => ArenaRole.Sustain | ArenaRole.Protection,
                "skillmanipulator" => ArenaRole.Opener | ArenaRole.Control,
                "speedmanipulator" => ArenaRole.Initiative | ArenaRole.Utility,
                _ => throw new InvalidDataException($"Unknown HellHades Arena role '{value}'.")
            };
        }
        return roles;
    }

    private sealed record CatalogFile(int Version, DateTimeOffset GeneratedUtc, string Source, List<CatalogChampion> Champions);
    private sealed record CatalogChampion(int BaseId, int HellHadesPostId, string EnglishName, int Rarity, List<string> ArenaRoles, List<CatalogForm> Forms, string SourceUrl, string SourceUpdated);
    private sealed record CatalogForm(int Form, int ArenaRating, List<string> ArenaRoles);
}
