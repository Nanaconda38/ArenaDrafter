using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RslArenaResearch;

public sealed class PortraitCache
{
    private const string CacheFormat = "sprite-atlas-render-data-1";
    private static readonly object CacheLock = new();
    private readonly string cacheRoot = Path.Combine(AppPaths.Data, "cache", "avatars", BuildValidator.Version);
    private readonly ConcurrentDictionary<int, Task<string?>> pendingLoads = new();

    public async Task<string?> GetAsync(int baseId, CancellationToken cancellationToken = default)
    {
        if (baseId <= 0) throw new ArgumentOutOfRangeException(nameof(baseId));
        var load = pendingLoads.GetOrAdd(baseId, LoadCoreAsync);
        return await load.WaitAsync(cancellationToken);
    }

    private async Task<string?> LoadCoreAsync(int baseId)
    {
        var requestedPath = Path.Combine(cacheRoot, $"{baseId}.png");
        try
        {
            EnsureCurrentCache();
            if (File.Exists(requestedPath) && new FileInfo(requestedPath).Length > 0) return requestedPath;

            var bundles = EnumerateBundles().ToArray();
            var result = await Task.Run(() => OfficialSpriteExtractor.Extract(bundles,
                name => IsPortraitName(name, baseId), requestedPath, CancellationToken.None));
            if (result is null)
                Log.Error($"Official RAID portrait was not found. ChampionBaseId={baseId}; requestedPath='{requestedPath}'; bundleCount={bundles.Length}.");
            return result;
        }
        catch (Exception exception)
        {
            Log.Error($"Official RAID portrait load failed. ChampionBaseId={baseId}; requestedPath='{requestedPath}'.", exception);
            return null;
        }
        finally
        {
            pendingLoads.TryRemove(baseId, out _);
        }
    }

    private void EnsureCurrentCache()
    {
        lock (CacheLock)
        {
            Directory.CreateDirectory(cacheRoot);
            var marker = Path.Combine(cacheRoot, ".format");
            if (File.Exists(marker) && File.ReadAllText(marker) == CacheFormat) return;
            foreach (var portrait in Directory.EnumerateFiles(cacheRoot, "*.png", SearchOption.TopDirectoryOnly)) File.Delete(portrait);
            File.WriteAllText(marker, CacheFormat);
            Log.Info("Invalidated portrait cache for corrected Unity texture orientation.");
        }
    }

    private static bool IsPortraitName(string name, int baseId) =>
        name.Equals(baseId.ToString(), StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith($"{baseId}_", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith($"_{baseId}", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateBundles()
    {
        var candidates = new[]
        {
            Path.Combine(AppPaths.RaidRoot, "resources"),
            Path.Combine(AppPaths.Build, "Raid_Data", "StreamingAssets", "AssetBundles")
        };
        return candidates.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root, "HeroAvatars*", SearchOption.TopDirectoryOnly))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(IsBundleData)
            .OrderByDescending(File.GetLastWriteTimeUtc);
    }

    internal static bool IsBundleData(string path) =>
        Path.GetFileName(path).Equals("__data", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase);
}

public sealed class SkillIconCache
{
    private const string CacheFormat = "official-skill-sprite-1";
    private static readonly object CacheLock = new();
    private readonly string cacheRoot = Path.Combine(AppPaths.Data, "cache", "skills", BuildValidator.Version);

    public async Task<string?> GetAsync(int baseId, int slot, int variant, CancellationToken cancellationToken = default)
    {
        if (baseId <= 0 || slot is < 0 or > 11 || variant < 0) throw new ArgumentOutOfRangeException(nameof(baseId));
        EnsureCurrentCache();
        var cached = Path.Combine(cacheRoot, $"{baseId}-{variant}-{slot}.png");
        if (File.Exists(cached)) return cached;
        var spriteName = variant == 0 ? $"{baseId}_s{slot + 1}" : $"{baseId}_f2_s{slot + 1}";
        var baseFormName = $"{baseId}_f1_s{slot + 1}";
        return await Task.Run(() => OfficialSpriteExtractor.Extract(EnumerateBundles(baseId),
            name => name.Equals(spriteName, StringComparison.OrdinalIgnoreCase)
                || variant == 0 && name.Equals(baseFormName, StringComparison.OrdinalIgnoreCase),
            cached, cancellationToken), cancellationToken);
    }

    private void EnsureCurrentCache()
    {
        lock (CacheLock)
        {
            Directory.CreateDirectory(cacheRoot);
            var marker = Path.Combine(cacheRoot, ".format");
            if (File.Exists(marker) && File.ReadAllText(marker) == CacheFormat) return;
            foreach (var icon in Directory.EnumerateFiles(cacheRoot, "*.png", SearchOption.TopDirectoryOnly)) File.Delete(icon);
            File.WriteAllText(marker, CacheFormat);
            Log.Info("Invalidated the official RAID skill-icon cache.");
        }
    }

    private static IEnumerable<string> EnumerateBundles(int baseId)
    {
        var candidates = new[]
        {
            Path.Combine(AppPaths.RaidRoot, "resources"),
            Path.Combine(AppPaths.Build, "Raid_Data", "StreamingAssets", "AssetBundles")
        };
        return candidates.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root, $"SkillIcons_{baseId}*", SearchOption.TopDirectoryOnly))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(PortraitCache.IsBundleData)
            .OrderByDescending(File.GetLastWriteTimeUtc);
    }
}

internal static class OfficialSpriteExtractor
{
    public static string? Extract(IEnumerable<string> bundlePaths, Func<string, bool> isMatch, string destination, CancellationToken cancellationToken)
    {
        foreach (var bundlePath in bundlePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = File.Open(bundlePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var manager = new AssetsManager();
                var bundle = manager.LoadBundleFile(stream, bundlePath, true);
                for (var index = 0; index < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; index++)
                {
                    var assets = manager.LoadAssetsFileFromBundle(bundle, index, true);
                    if (assets is null) continue;
                    foreach (var spriteInfo in assets.file.GetAssetsOfType(AssetClassID.Sprite))
                    {
                        var sprite = manager.GetBaseField(assets, spriteInfo);
                        if (!isMatch(sprite["m_Name"].AsString)) continue;
                        var renderData = sprite["m_RD"];
                        var sourceFile = assets;
                        var texturePointer = renderData["texture"];
                        if (texturePointer.IsDummy || texturePointer["m_PathID"].AsLong == 0)
                        {
                            var atlasPointer = sprite["m_SpriteAtlas"];
                            if (atlasPointer.IsDummy || atlasPointer["m_PathID"].AsLong == 0) continue;
                            var atlasAsset = manager.GetExtAsset(assets, atlasPointer);
                            if (atlasAsset.baseField.IsDummy) continue;
                            var key = sprite["m_RenderDataKey"].WriteToByteArray();
                            renderData = atlasAsset.baseField["m_RenderDataMap"]["Array"].Children
                                .FirstOrDefault(entry => entry["first"].WriteToByteArray().SequenceEqual(key))?["second"]
                                ?? AssetTypeValueField.DUMMY_FIELD;
                            if (renderData.IsDummy) continue;
                            sourceFile = atlasAsset.file;
                            texturePointer = renderData["texture"];
                        }
                        if (texturePointer.IsDummy || texturePointer["m_PathID"].AsLong == 0) continue;
                        var textureField = manager.GetExtAsset(sourceFile, texturePointer).baseField;
                        if (textureField.IsDummy) continue;
                        var texture = TextureFile.ReadTextureFile(textureField);
                        if (texture.pictureData is null || texture.pictureData.Length == 0) FillStreamData(texture, bundle);
                        if (texture.pictureData is null || texture.pictureData.Length == 0) continue;
                        var pixels = TextureFile.DecodeManagedData(texture.pictureData, (TextureFormat)texture.m_TextureFormat,
                            texture.m_Width, texture.m_Height, true);
                        if (pixels is null) continue;
                        FlipVertically(pixels, texture.m_Width, texture.m_Height);
                        SavePng(pixels, texture.m_Width, texture.m_Height, renderData["textureRect"], destination);
                        manager.UnloadAll();
                        return destination;
                    }
                }
                manager.UnloadAll();
            }
            catch (Exception) { /* An official resource shard may not be a Unity bundle; continue with the next shard. */ }
        }
        return null;
    }

    private static void FillStreamData(TextureFile texture, BundleFileInstance bundle)
    {
        if (texture.m_StreamData.size == 0 || string.IsNullOrWhiteSpace(texture.m_StreamData.path)) return;
        var info = BundleHelper.GetDirInfo(bundle.file, Path.GetFileName(texture.m_StreamData.path));
        if (info is null) return;
        var reader = bundle.file.DataReader;
        lock (reader)
        {
            reader.Position = info.Offset + (long)texture.m_StreamData.offset;
            texture.pictureData = reader.ReadBytes((int)texture.m_StreamData.size);
        }
    }

    private static void SavePng(byte[] bgra, int width, int height, AssetTypeValueField rect, string destination)
    {
        var x = 0;
        var y = 0;
        var cropWidth = width;
        var cropHeight = height;
        if (!rect.IsDummy)
        {
            x = Math.Clamp((int)rect["x"].AsFloat, 0, width - 1);
            cropWidth = Math.Clamp((int)rect["width"].AsFloat, 1, width - x);
            cropHeight = Math.Clamp((int)rect["height"].AsFloat, 1, height);
            y = Math.Clamp(height - (int)rect["y"].AsFloat - cropHeight, 0, height - cropHeight);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        BitmapSource output = x == 0 && y == 0 && cropWidth == width && cropHeight == height
            ? bitmap
            : new CroppedBitmap(bitmap, new Int32Rect(x, y, cropWidth, cropHeight));
        using var file = File.Create(destination);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        encoder.Save(file);
    }

    private static void FlipVertically(byte[] pixels, int width, int height)
    {
        var stride = width * 4;
        var row = new byte[stride];
        for (var top = 0; top < height / 2; top++)
        {
            var bottom = height - top - 1;
            Buffer.BlockCopy(pixels, top * stride, row, 0, stride);
            Buffer.BlockCopy(pixels, bottom * stride, pixels, top * stride, stride);
            Buffer.BlockCopy(row, 0, pixels, bottom * stride, stride);
        }
    }
}
