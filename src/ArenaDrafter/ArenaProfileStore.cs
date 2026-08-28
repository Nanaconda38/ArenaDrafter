using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArenaDrafter;

public sealed record ArenaProfile(Guid Id, string Name, DateTime CreatedUtc, DateTime UpdatedUtc, ArenaStrategyFile Strategy, BattleOpenerFile Opener)
{
    public void Validate()
    {
        if (Id == Guid.Empty || !ArenaProfileStore.IsValidName(Name) || CreatedUtc == default || UpdatedUtc < CreatedUtc || Strategy is null || Opener is null)
            throw new InvalidDataException("The Arena profile identity or configuration is invalid.");
        Strategy.Validate(false);
        if (Strategy.DraftMode != ArenaDraftMode.PresetLineup)
            throw new InvalidDataException("Distribution profiles support Preset Lineup only.");
        Opener.Validate();
    }
}

public sealed record ArenaProfileStoreFile(int Version, Guid ActiveProfileId, List<ArenaProfile> Profiles);
public sealed record ArenaProfilePackage(int Version, ArenaProfile Profile) { public const int CurrentVersion = 1; }

public sealed class ArenaProfileStore
{
    public const int CurrentVersion = 1;
    public const int MaxProfiles = 50;
    public const int MaxImportBytes = 1_000_000;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly List<ArenaProfile> profiles;
    private readonly string dataRoot;
    private ArenaProfileStore(string dataRoot, List<ArenaProfile> profiles, Guid activeId)
    {
        this.dataRoot = Path.GetFullPath(dataRoot);
        this.profiles = profiles;
        ActiveProfileId = activeId;
    }
    public string FilePath => Path.Combine(dataRoot, "live-arena-profiles.json");
    public string BackupPath => FilePath + ".bak";
    public string MigrationBackupRoot => Path.Combine(dataRoot, "profile-migrations");
    public Guid ActiveProfileId { get; private set; }
    public IReadOnlyList<ArenaProfile> Profiles => profiles;
    public ArenaProfile ActiveProfile => profiles.Single(profile => profile.Id == ActiveProfileId);
    public string? RecoveryNotice { get; private set; }
    public bool WasMigrated { get; private set; }

    public static ArenaProfileStore Load(bool allowAdaptive = false, string? dataRoot = null, string? legacyRoot = null)
    {
        if (allowAdaptive) throw new InvalidOperationException("Distribution does not support Adaptive Draft profiles.");
        dataRoot ??= AppPaths.Data;
        legacyRoot ??= AppPaths.LegacyData;
        Directory.CreateDirectory(dataRoot);
        var store = new ArenaProfileStore(dataRoot, [], Guid.Empty);
        if (File.Exists(store.FilePath))
        {
            try { store.LoadStore(File.ReadAllText(store.FilePath)); return store; }
            catch (Exception primary) when (File.Exists(store.BackupPath))
            {
                try { store.LoadStore(File.ReadAllText(store.BackupPath)); store.RecoveryNotice = $"Profile store recovered from its previous valid version: {primary.Message}"; return store; }
                catch (Exception backup) { throw new InvalidDataException("The Arena profile store and its recovery copy are invalid.", new AggregateException(primary, backup)); }
            }
        }
        store.MigrateLegacy(legacyRoot);
        store.Save();
        return store;
    }

    private void LoadStore(string json)
    {
        EnsureSize(json);
        var file = JsonSerializer.Deserialize<ArenaProfileStoreFile>(json, Options) ?? throw new InvalidDataException("The Arena profile store is empty.");
        ValidateStore(file);
        profiles.Clear(); profiles.AddRange(file.Profiles); ActiveProfileId = file.ActiveProfileId;
    }
    private void ValidateStore(ArenaProfileStoreFile? file)
    {
        if (file is null || file.Version != CurrentVersion || file.Profiles is null || file.Profiles.Count is < 1 or > MaxProfiles || file.ActiveProfileId == Guid.Empty)
            throw new InvalidDataException("The Arena profile store version or profile count is invalid.");
        if (file.Profiles.Select(item => item.Id).Distinct().Count() != file.Profiles.Count || file.Profiles.Select(item => item.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != file.Profiles.Count)
            throw new InvalidDataException("Arena profile identities and names must be unique.");
        foreach (var profile in file.Profiles) profile.Validate();
        if (!file.Profiles.Any(profile => profile.Id == file.ActiveProfileId)) throw new InvalidDataException("The active Arena profile is missing.");
    }
    private void MigrateLegacy(string root)
    {
        var strategyPath = Path.Combine(root, "live-arena-strategy.json");
        var openerPath = Path.Combine(root, "live-arena-opener.json");
        ArenaStrategyFile strategy = File.Exists(strategyPath) ? ArenaStrategyFile.Parse(File.ReadAllText(strategyPath)) : new(ArenaStrategyFile.CurrentVersion, [], [], ArenaDraftMode.PresetLineup, ArenaStrategyFile.EmptyPresetLineup(), [], []);
        BattleOpenerFile opener = File.Exists(openerPath) ? BattleOpenerFile.Parse(File.ReadAllText(openerPath)) : new(BattleOpenerFile.CurrentVersion, []);
        var now = DateTime.UtcNow;
        var profile = new ArenaProfile(Guid.NewGuid(), "Default", now, now, strategy, opener); profile.Validate();
        profiles.Clear(); profiles.Add(profile); ActiveProfileId = profile.Id;
        WasMigrated = File.Exists(strategyPath) || File.Exists(openerPath);
        if (WasMigrated)
        {
            var backup = Path.Combine(MigrationBackupRoot, now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)); Directory.CreateDirectory(backup);
            if (File.Exists(strategyPath)) File.Copy(strategyPath, Path.Combine(backup, Path.GetFileName(strategyPath)), true);
            if (File.Exists(openerPath)) File.Copy(openerPath, Path.Combine(backup, Path.GetFileName(openerPath)), true);
        }
    }

    public void Save()
    {
        var file = new ArenaProfileStoreFile(CurrentVersion, ActiveProfileId, profiles.ToList()); ValidateStore(file); Directory.CreateDirectory(dataRoot);
        var temp = FilePath + ".tmp"; var json = JsonSerializer.Serialize(file, Options); EnsureSize(json); File.WriteAllText(temp, json, new UTF8Encoding(false));
        var verify = JsonSerializer.Deserialize<ArenaProfileStoreFile>(File.ReadAllText(temp), Options) ?? throw new InvalidDataException("The temporary Arena profile store is empty."); ValidateStore(verify);
        if (File.Exists(FilePath) && IsValidStoreFile(File.ReadAllText(FilePath))) File.Copy(FilePath, BackupPath, true); File.Move(temp, FilePath, true); RecoveryNotice = null;
    }
    public ArenaProfile Create(string name, ArenaStrategyFile? strategy = null, BattleOpenerFile? opener = null)
    {
        if (profiles.Count >= MaxProfiles) throw new InvalidOperationException($"Arena profiles are limited to {MaxProfiles}.");
        var now = DateTime.UtcNow; var profile = new ArenaProfile(Guid.NewGuid(), UniqueName(name), now, now, strategy ?? new(ArenaStrategyFile.CurrentVersion, [], [], ArenaDraftMode.PresetLineup, ArenaStrategyFile.EmptyPresetLineup(), [], []), opener ?? new(BattleOpenerFile.CurrentVersion, [])); profile.Validate(); profiles.Add(profile); ActiveProfileId = profile.Id; Save(); return profile;
    }
    public ArenaProfile Duplicate(Guid id, string? name = null) { var source = Get(id); return Create(name ?? $"{source.Name} Copy", CloneStrategy(source.Strategy), CloneOpener(source.Opener)); }
    public void Rename(Guid id, string name) { var p = Get(id); Replace(p with { Name = UniqueName(name, id), UpdatedUtc = DateTime.UtcNow }); Save(); }
    public void SetActive(Guid id) { _ = Get(id); ActiveProfileId = id; Save(); }
    public void Delete(Guid id) { if (profiles.Count == 1) throw new InvalidOperationException("The final Arena profile cannot be deleted."); profiles.Remove(Get(id)); if (ActiveProfileId == id) ActiveProfileId = profiles[0].Id; Save(); }
    public void UpdateActive(ArenaStrategyFile strategy, BattleOpenerFile opener) { strategy.Validate(false); opener.Validate(); if (strategy.DraftMode != ArenaDraftMode.PresetLineup) throw new InvalidDataException("Distribution profiles support Preset Lineup only."); var p = ActiveProfile; Replace(p with { Strategy = strategy, Opener = opener, UpdatedUtc = DateTime.UtcNow }); Save(); }
    public void RestorePreviousVersion() { if (!File.Exists(BackupPath)) throw new FileNotFoundException("No previous Arena profile store version is available."); LoadStore(File.ReadAllText(BackupPath)); Save(); }
    public string Export(Guid id) { var json = JsonSerializer.Serialize(new ArenaProfilePackage(ArenaProfilePackage.CurrentVersion, Get(id)), Options); EnsureSize(json); return json; }
    public ArenaProfile Import(string json) { EnsureSize(json); var package = JsonSerializer.Deserialize<ArenaProfilePackage>(json, Options) ?? throw new InvalidDataException("The Arena profile package is empty."); if (package.Version != ArenaProfilePackage.CurrentVersion || package.Profile is null) throw new InvalidDataException("The Arena profile package version is unsupported."); if (profiles.Count >= MaxProfiles) throw new InvalidOperationException($"Arena profiles are limited to {MaxProfiles}."); package.Profile.Validate(); var now = DateTime.UtcNow; var p = package.Profile with { Id = Guid.NewGuid(), Name = UniqueName(package.Profile.Name), CreatedUtc = now, UpdatedUtc = now }; profiles.Add(p); ActiveProfileId = p.Id; Save(); return p; }
    public static bool IsValidName(string? name) { if (string.IsNullOrWhiteSpace(name)) return false; var value = name.Trim(); return value.Length <= 50 && !value.Any(char.IsControl) && !value.Contains(Path.DirectorySeparatorChar) && !value.Contains(Path.AltDirectorySeparatorChar); }
    private ArenaProfile Get(Guid id) => profiles.SingleOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException("The requested Arena profile was not found.");
    private void Replace(ArenaProfile profile) { var index = profiles.FindIndex(p => p.Id == profile.Id); if (index < 0) throw new KeyNotFoundException("The requested Arena profile was not found."); profile.Validate(); profiles[index] = profile; }
    private string UniqueName(string name, Guid? except = null) { if (!IsValidName(name)) throw new InvalidDataException("Arena profile names must contain 1 to 50 visible characters."); var baseName = name.Trim(); var candidate = baseName; for (var suffix = 2; profiles.Any(p => p.Id != except && p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); suffix++) { var text = $" ({suffix})"; candidate = baseName[..Math.Min(baseName.Length, 50 - text.Length)].TrimEnd() + text; } return candidate; }
    private static void EnsureSize(string value) { if (Encoding.UTF8.GetByteCount(value) > MaxImportBytes) throw new InvalidDataException("The Arena profile payload exceeds the bounded size limit."); }
    private static ArenaStrategyFile CloneStrategy(ArenaStrategyFile strategy) => JsonSerializer.Deserialize<ArenaStrategyFile>(JsonSerializer.Serialize(strategy, Options), Options) ?? throw new InvalidDataException("The Arena strategy could not be cloned.");
    private static BattleOpenerFile CloneOpener(BattleOpenerFile opener) => JsonSerializer.Deserialize<BattleOpenerFile>(JsonSerializer.Serialize(opener, Options), Options) ?? throw new InvalidDataException("The Arena opener could not be cloned.");
    private bool IsValidStoreFile(string json) { try { EnsureSize(json); var file = JsonSerializer.Deserialize<ArenaProfileStoreFile>(json, Options); ValidateStore(file); return true; } catch { return false; } }
}
