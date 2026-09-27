using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Downloads;
using DeEarthX.Infrastructure.Toml;
using DeEarthX.Infrastructure.Zip;
using Tomlyn.Model;

namespace DeEarthX.Dearth;

public sealed class FileExtractor
{
    private readonly IZipService _zip;
    private readonly ITomlService _toml;
    private readonly ILogService _log;
    private readonly Sha1Service _sha1;

    public FileExtractor(IZipService zip, ITomlService toml, ILogService log, Sha1Service sha1)
    {
        _zip = zip;
        _toml = toml;
        _log = log;
        _sha1 = sha1;
    }

    public async Task<List<ModFileInfo>> ExtractFilesInfoAsync(string modsDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(modsDir))
        {
            Directory.CreateDirectory(modsDir);
        }

        var jarPaths = Directory.EnumerateFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly).ToList();
        var files = new List<ModFileInfo>(jarPaths.Count);

        foreach (var fullPath in jarPaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var hash = RetryIo(() => _sha1.Calculate(fullPath));
                var entries = _zip.ReadEntries(fullPath);
                var (mixins, infos) = await ReadMixinAndInfoNamesAsync(entries, fullPath, null, ct).ConfigureAwait(false);
                files.Add(new ModFileInfo(fullPath, Path.GetFileName(fullPath), hash, mixins, infos, null));
            }
            catch (Exception ex)
            {
                _log.Error($"处理文件时出错: {fullPath}", ex);
            }
        }

        return files;
    }

    private static T RetryIo<T>(Func<T> action, int maxAttempts = 5)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(200 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(200 * attempt);
            }
        }
        return action();
    }

    public async Task<List<ModFileInfo>> ExtractFromBuffersAsync(IEnumerable<(string FileName, byte[] Content)> jars, CancellationToken ct = default)
    {
        var files = new List<ModFileInfo>();
        foreach (var jar in jars)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var hash = Convert.ToHexString(SHA1.HashData(jar.Content)).ToLowerInvariant();
                var entries = _zip.ReadEntries(jar.Content);
                var (mixins, infos) = await ReadMixinAndInfoNamesAsync(entries, null, jar.Content, ct).ConfigureAwait(false);
                files.Add(new ModFileInfo(jar.FileName, jar.FileName, hash, mixins, infos, jar.Content));
            }
            catch (Exception ex)
            {
                _log.Error($"处理内存文件时出错: {jar.FileName}", ex);
            }
        }

        return files;
    }

    public async Task<ModMeta?> ExtractModMetaAsync(ModFileInfo file, CancellationToken ct = default)
    {
        foreach (var info in file.Infos)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (info.Name.EndsWith("neoforge.mods.toml", StringComparison.OrdinalIgnoreCase) ||
                    info.Name.EndsWith("mods.toml", StringComparison.OrdinalIgnoreCase))
                {
                    var isNeo = info.Name.EndsWith("neoforge.mods.toml", StringComparison.OrdinalIgnoreCase);
                    var table = _toml.Parse(info.Data);
                    if (table.TryGetValue("mods", out var modsObj) && modsObj is TomlTableArray mods && mods.Count > 0)
                    {
                        var mod = mods[0];
                        var modId = GetString(mod, "modId") ?? GetString(mod, "modid");
                        var version = GetString(mod, "version");
                        var logoFile = GetString(mod, "logoFile");
                        var description = GetString(mod, "description");
                        var authors = GetString(mod, "authors") ?? GetString(mod, "author");
                        var iconUrl = logoFile is null ? null : await ExtractIconAsync(file, logoFile, ct).ConfigureAwait(false);
                        return new ModMeta(modId ?? string.Empty, version, isNeo ? "neoforge" : "forge", iconUrl, description, authors, file.Mixins.Select(m => m.Name).ToList());
                    }
                }
                else if (info.Name.EndsWith("fabric.mod.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var doc = JsonDocument.Parse(info.Data);
                    var root = doc.RootElement;
                    var id = GetString(root, "id");
                    var version = GetString(root, "version");
                    var description = GetString(root, "description");
                    string? icon = null;
                    if (root.TryGetProperty("icon", out var iconEl))
                    {
                        icon = iconEl.ValueKind == JsonValueKind.String
                            ? iconEl.GetString()
                            : iconEl.EnumerateObject().FirstOrDefault().Value.GetString();
                    }

                    string? author = null;
                    if (root.TryGetProperty("authors", out var authorsEl) && authorsEl.ValueKind == JsonValueKind.Array)
                    {
                        author = string.Join(", ", authorsEl.EnumerateArray().Select(a =>
                            a.ValueKind == JsonValueKind.String ? a.GetString() : GetString(a, "name")));
                    }

                    var iconUrl = icon is null ? null : await ExtractIconAsync(file, icon, ct).ConfigureAwait(false);
                    return new ModMeta(id ?? string.Empty, version, "fabric", iconUrl, description, author, file.Mixins.Select(m => m.Name).ToList());
                }
                else if (info.Name.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase) ||
                         info.Name.Equals("modrinth.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var doc = JsonDocument.Parse(info.Data);
                    var root = doc.RootElement;
                    var id = GetString(root, "project_id") ?? GetString(root, "id");
                    var description = GetString(root, "summary") ?? GetString(root, "description");
                    return new ModMeta(id ?? string.Empty, null, null, null, description, null, file.Mixins.Select(m => m.Name).ToList());
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"解析 {info.Name} 失败: {ex.Message}");
            }
        }

        return null;
    }

    private async Task<(List<MixinFile> Mixins, List<InfoFile> Infos)> ReadMixinAndInfoNamesAsync(
        List<ZipEntryInfo> entries, string? fullPath, byte[]? buffer, CancellationToken ct)
    {
        var mixinNames = new List<string>();
        var infoNames = new List<string>();

        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            var name = entry.FullName;
            var isMixin = (name.EndsWith(".mixins.json", StringComparison.OrdinalIgnoreCase) ||
                          name.EndsWith(".mixin.json", StringComparison.OrdinalIgnoreCase)) &&
                         !name.Contains('/');
            if (isMixin)
            {
                mixinNames.Add(name);
                continue;
            }

            var isInfo = name.EndsWith("mods.toml", StringComparison.OrdinalIgnoreCase) ||
                         name.EndsWith("fabric.mod.json", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("modrinth.json", StringComparison.OrdinalIgnoreCase);
            if (isInfo)
            {
                infoNames.Add(name);
            }
        }

        var mixins = new List<MixinFile>(mixinNames.Count);
        var infos = new List<InfoFile>(infoNames.Count);

        if (buffer is not null)
        {
            var dict = await _zip.ReadEntriesAsync(buffer, mixinNames.Concat(infoNames)).ConfigureAwait(false);
            foreach (var n in mixinNames)
            {
                if (dict.TryGetValue(n, out var data))
                {
                    mixins.Add(new MixinFile(n, Encoding.UTF8.GetString(data)));
                }
            }

            foreach (var n in infoNames)
            {
                if (dict.TryGetValue(n, out var data))
                {
                    infos.Add(BuildInfo(n, data));
                }
            }
        }
        else
        {
            foreach (var n in mixinNames)
            {
                var data = await _zip.ReadEntryAsync(fullPath!, n, ct).ConfigureAwait(false);
                if (data.Length > 0)
                {
                    mixins.Add(new MixinFile(n, Encoding.UTF8.GetString(data)));
                }
            }

            foreach (var n in infoNames)
            {
                var data = await _zip.ReadEntryAsync(fullPath!, n, ct).ConfigureAwait(false);
                if (data.Length > 0)
                {
                    infos.Add(BuildInfo(n, data));
                }
            }
        }

        return (mixins, infos);
    }

    private InfoFile BuildInfo(string name, byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        if (name.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var table = _toml.Parse(text);
                text = JsonSerializer.Serialize(TomlToObj(table), DeEarthXJsonOptions.Default);
            }
            catch (Exception ex)
            {
                _log.Debug($"TOML 解析失败 {name}: {ex.Message}");
            }
        }

        return new InfoFile(name, text);
    }

    private async Task<string?> ExtractIconAsync(ModFileInfo file, string iconPath, CancellationToken ct)
    {
        try
        {
            byte[]? data;
            if (file.FileData is not null)
            {
                data = _zip.ReadEntry(file.FileData, iconPath);
            }
            else
            {
                var bytes = await _zip.ReadEntryAsync(file.FilePath, iconPath, ct).ConfigureAwait(false);
                data = bytes.Length == 0 ? null : bytes;
            }

            if (data is null || data.Length == 0)
            {
                return null;
            }

            var ext = Path.GetExtension(iconPath).TrimStart('.').ToLowerInvariant();
            var mime = ext switch { "png" => "png", "gif" => "gif", "webp" => "webp", _ => "jpeg" };
            return $"data:image/{mime};base64,{Convert.ToBase64String(data)}";
        }
        catch (Exception ex)
        {
            _log.Debug($"提取图标 {iconPath} 失败: {ex.Message}");
            return null;
        }
    }

    private static string? GetString(TomlTable table, string key)
        => table.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;

    private static string? GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static object? TomlToObj(object? value) => value switch
    {
        TomlTable t => t.ToDictionary(kv => kv.Key, kv => TomlToObj(kv.Value)),
        TomlArray arr => arr.Select(TomlToObj).ToList(),
        _ => value
    };
}
