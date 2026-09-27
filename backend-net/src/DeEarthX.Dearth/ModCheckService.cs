using System.Text.Json;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Dearth;

public sealed class ModCheckService
{
    private const int DefaultTimeoutMs = 30_000;

    private static readonly Dictionary<string, int> SourcePriority = new(StringComparer.Ordinal)
    {
        ["Dexpub"] = 1,
        ["Modrinth"] = 2,
        ["Mixin"] = 3,
        ["Hash"] = 4
    };

    private readonly FileExtractor _extractor;
    private readonly FileOperator _operator;
    private readonly HashFilter _hashFilter;
    private readonly DexpubFilter _dexpubFilter;
    private readonly MixinFilter _mixinFilter;
    private readonly ModrinthFilter _modrinthFilter;
    private readonly ILogService _log;

    public ModCheckService(
        FileExtractor extractor,
        FileOperator fileOperator,
        HashFilter hashFilter,
        DexpubFilter dexpubFilter,
        MixinFilter mixinFilter,
        ModrinthFilter modrinthFilter,
        ILogService log)
    {
        _extractor = extractor;
        _operator = fileOperator;
        _hashFilter = hashFilter;
        _dexpubFilter = dexpubFilter;
        _mixinFilter = mixinFilter;
        _modrinthFilter = modrinthFilter;
        _log = log;
    }

    public async Task<List<ModCheckItem>> CheckModsAsync(string modsDir, CancellationToken ct = default)
    {
        _log.Info("开始模组检查流程");
        var files = await _extractor.ExtractFilesInfoAsync(modsDir, ct).ConfigureAwait(false);
        var results = new List<ModCheckItem>(files.Count);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await CheckSingleFileAsync(file, ct).ConfigureAwait(false));
        }

        _log.Info($"模组检查流程完成: 总数={results.Count}");
        return results;
    }

    public async Task<List<ModCheckItem>> CheckModsWithBundleAsync(string modsDir, string bundleName, CancellationToken ct = default)
    {
        _log.Info($"开始模组检查流程（带整合包）: {bundleName}");
        var files = await _extractor.ExtractFilesInfoAsync(modsDir, ct).ConfigureAwait(false);
        var clientMods = await IdentifyClientSideModsAsync(files, ct).ConfigureAwait(false);
        var clientSet = new HashSet<string>(clientMods, StringComparer.Ordinal);

        var results = new List<ModCheckItem>(files.Count);
        foreach (var file in files)
        {
            var isClient = clientSet.Contains(file.FilePath);
            if (isClient)
            {
                results.Add(new ModCheckItem(
                    file.FileName,
                    file.FilePath,
                    ModSide.Required,
                    ModSide.Unsupported,
                    "Multiple",
                    true,
                    null,
                    new List<SingleCheckResult>
                    {
                        new("Multiple", ModSide.Required, ModSide.Unsupported, true, null)
                    },
                    null,
                    null,
                    null,
                    null));
            }
            else
            {
                results.Add(new ModCheckItem(
                    file.FileName,
                    file.FilePath,
                    ModSide.Unknown,
                    ModSide.Unknown,
                    "none",
                    false,
                    null,
                    new List<SingleCheckResult>(),
                    null,
                    null,
                    null,
                    null));
            }
        }

        if (clientMods.Count > 0)
        {
            var moveDir = Path.Combine(".rubbish", bundleName);
            await _operator.MoveFilesAsync(clientMods, moveDir, ct).ConfigureAwait(false);
            _log.Info($"已移动 {clientMods.Count} 个客户端模组到 .rubbish/{bundleName}");
        }

        _log.Info($"模组检查流程完成: 总数={results.Count}, 客户端={clientMods.Count}");
        return results;
    }

    public async Task<List<ModCheckItem>> CheckUploadedFilesAsync(
        IEnumerable<(string FileName, byte[] Content)> jars, CancellationToken ct = default)
    {
        var jarList = jars.ToList();
        _log.Info($"开始检查上传文件: 数量={jarList.Count}");
        var results = new List<ModCheckItem>(jarList.Count);

        foreach (var jar in jarList)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var extracted = await _extractor.ExtractFromBuffersAsync(new[] { jar }, ct).ConfigureAwait(false);
                var file = extracted.FirstOrDefault();
                if (file is null)
                {
                    results.Add(BuildErrorItem(jar.FileName, "文件无法提取"));
                    continue;
                }

                results.Add(await CheckSingleFileAsync(file, ct).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _log.Error($"处理上传文件出错: {jar.FileName}", ex);
                results.Add(BuildErrorItem(jar.FileName, ex.Message));
            }
        }

        _log.Info($"上传文件模组检查完成: 总数={results.Count}");
        return results;
    }

    private async Task<List<string>> IdentifyClientSideModsAsync(List<ModFileInfo> files, CancellationToken ct)
    {
        var clientMods = new List<string>();
        var processed = new HashSet<string>(StringComparer.Ordinal);

        ct.ThrowIfCancellationRequested();
        _log.Info("开始 Galaxy Square (dexpub) 检查客户端模组");
        var (dexpubClient, dexpubServer) = await _dexpubFilter.CheckAsync(files).ConfigureAwait(false);
        foreach (var m in dexpubClient)
        {
            processed.Add(m);
        }

        foreach (var m in dexpubServer)
        {
            processed.Add(m);
        }

        clientMods.AddRange(dexpubClient);

        ct.ThrowIfCancellationRequested();
        _log.Info("开始 Modrinth API 检查客户端模组");
        var unprocessed = files.Where(f => !processed.Contains(f.FilePath)).ToList();
        var modrinthMods = await _modrinthFilter.FilterBatchAsync(unprocessed).ConfigureAwait(false);
        foreach (var m in modrinthMods)
        {
            processed.Add(m);
        }

        clientMods.AddRange(modrinthMods);

        ct.ThrowIfCancellationRequested();
        _log.Info("开始 Mixin 检查客户端模组");
        unprocessed = files.Where(f => !processed.Contains(f.FilePath)).ToList();
        var mixinMods = await _mixinFilter.FilterBatchAsync(unprocessed).ConfigureAwait(false);
        foreach (var m in mixinMods)
        {
            processed.Add(m);
        }

        clientMods.AddRange(mixinMods);

        ct.ThrowIfCancellationRequested();
        _log.Info("开始 Hash 检查客户端模组");
        unprocessed = files.Where(f => !processed.Contains(f.FilePath)).ToList();
        var hashMods = await _hashFilter.FilterBatchAsync(unprocessed).ConfigureAwait(false);
        clientMods.AddRange(hashMods);

        var unique = clientMods.Distinct(StringComparer.Ordinal).ToList();
        _log.Info($"识别到客户端模组: 数量={unique.Count}");
        return unique;
    }

    private async Task<ModCheckItem> CheckSingleFileAsync(ModFileInfo file, CancellationToken ct)
    {
        var allResults = await CollectAllResultsParallelAsync(file, ct).ConfigureAwait(false);

        ModMeta? meta = null;
        try
        {
            meta = await _extractor.ExtractModMetaAsync(file, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Debug($"提取模组元信息失败: {file.FileName}: {ex.Message}");
        }

        var errors = new List<string>();
        foreach (var r in allResults)
        {
            if (!string.IsNullOrEmpty(r.Error))
            {
                errors.Add($"{r.Source}: {r.Error}");
            }
        }

        var successful = allResults.Where(r => r.Checked).ToList();
        if (successful.Count == 0)
        {
            return new ModCheckItem(
                file.FileName,
                file.FilePath,
                ModSide.Unknown,
                ModSide.Unknown,
                "none",
                false,
                errors,
                allResults,
                meta?.ModId,
                meta?.IconUrl,
                meta?.Description,
                meta?.Author);
        }

        successful.Sort((a, b) =>
            SourcePriority.GetValueOrDefault(a.Source, 99).CompareTo(SourcePriority.GetValueOrDefault(b.Source, 99)));
        var best = successful[0];

        return new ModCheckItem(
            file.FileName,
            file.FilePath,
            best.ClientSide,
            best.ServerSide,
            best.Source,
            true,
            errors,
            allResults,
            meta?.ModId,
            meta?.IconUrl,
            meta?.Description,
            meta?.Author);
    }

    private async Task<List<SingleCheckResult>> CollectAllResultsParallelAsync(ModFileInfo file, CancellationToken ct)
    {
        var tasks = new[]
        {
            RunCheckWithTimeoutAsync(CheckDexpubAsync, file, "Dexpub", ct),
            RunCheckWithTimeoutAsync(CheckModrinthAsync, file, "Modrinth", ct),
            RunCheckWithTimeoutAsync(CheckMixinAsync, file, "Mixin", ct),
            RunCheckWithTimeoutAsync(CheckHashAsync, file, "Hash", ct)
        };

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    private async Task<SingleCheckResult> RunCheckWithTimeoutAsync(
        Func<ModFileInfo, Task<SideResult?>> checkFn,
        ModFileInfo file,
        string source,
        CancellationToken ct)
    {
        var checkTask = checkFn(file);
        var timeoutTask = Task.Delay(DefaultTimeoutMs, ct);

        var completed = await Task.WhenAny(checkTask, timeoutTask).ConfigureAwait(false);
        if (completed == timeoutTask)
        {
            return new SingleCheckResult(source, ModSide.Unknown, ModSide.Unknown, false, $"{source} 检查超时: {file.FileName}");
        }

        try
        {
            var result = await checkTask.ConfigureAwait(false);
            if (result is null)
            {
                return new SingleCheckResult(source, ModSide.Unknown, ModSide.Unknown, false, null);
            }

            return new SingleCheckResult(source, result.ClientSide, result.ServerSide, true, null);
        }
        catch (Exception ex)
        {
            _log.Warn($"{file.FileName} 的 {source} 检查失败: {ex.Message}");
            return new SingleCheckResult(source, ModSide.Unknown, ModSide.Unknown, false, ex.Message);
        }
    }

    private async Task<SideResult?> CheckDexpubAsync(ModFileInfo file)
    {
        var (client, server) = await _dexpubFilter.CheckAsync(new List<ModFileInfo> { file }).ConfigureAwait(false);
        var basename = Path.GetFileName(file.FilePath);
        if (client.Any(p => Path.GetFileName(p) == basename))
        {
            return new SideResult(ModSide.Required, ModSide.Unsupported);
        }

        if (server.Any(p => Path.GetFileName(p) == basename))
        {
            return new SideResult(ModSide.Unsupported, ModSide.Required);
        }

        return null;
    }

    private async Task<SideResult?> CheckModrinthAsync(ModFileInfo file)
    {
        var clientMods = await _modrinthFilter.FilterBatchAsync(new List<ModFileInfo> { file }).ConfigureAwait(false);
        var basename = Path.GetFileName(file.FilePath);
        if (clientMods.Any(p => Path.GetFileName(p) == basename))
        {
            return new SideResult(ModSide.Required, ModSide.Unsupported);
        }

        foreach (var info in file.Infos)
        {
            if (!info.Name.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase) &&
                !info.Name.Equals("modrinth.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(info.Data);
                var root = doc.RootElement;
                var clientSide = MapSide(GetString(root, "client_side"));
                var serverSide = MapSide(GetString(root, "server_side"));
                return new SideResult(clientSide, serverSide);
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private Task<SideResult?> CheckMixinAsync(ModFileInfo file)
    {
        var isLib = file.FileName.Contains("lib", StringComparison.OrdinalIgnoreCase);

        foreach (var mixin in file.Mixins)
        {
            try
            {
                using var doc = JsonDocument.Parse(mixin.Data);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var hasMixins = root.TryGetProperty("mixins", out var mixinsEl) &&
                                mixinsEl.ValueKind == JsonValueKind.Array &&
                                mixinsEl.GetArrayLength() > 0;
                var hasClient = root.TryGetProperty("client", out var clientEl) &&
                                clientEl.ValueKind == JsonValueKind.Array &&
                                clientEl.GetArrayLength() > 0;
                if (!hasMixins && hasClient && !isLib)
                {
                    return Task.FromResult<SideResult?>(new SideResult(ModSide.Required, ModSide.Unsupported));
                }
            }
            catch
            {
                continue;
            }
        }

        return Task.FromResult<SideResult?>(null);
    }

    private async Task<SideResult?> CheckHashAsync(ModFileInfo file)
    {
        var clientMods = await _hashFilter.FilterBatchAsync(new List<ModFileInfo> { file }).ConfigureAwait(false);
        var basename = Path.GetFileName(file.FilePath);
        if (clientMods.Any(p => Path.GetFileName(p) == basename))
        {
            return new SideResult(ModSide.Required, ModSide.Unsupported);
        }

        return null;
    }

    private static ModCheckItem BuildErrorItem(string fileName, string error)
    {
        return new ModCheckItem(
            fileName,
            fileName,
            ModSide.Unknown,
            ModSide.Unknown,
            "none",
            false,
            new List<string> { error },
            new List<SingleCheckResult>(),
            null,
            null,
            null,
            null);
    }

    private static ModSide MapSide(string? value) => value switch
    {
        "required" => ModSide.Required,
        "optional" => ModSide.Optional,
        "unsupported" => ModSide.Unsupported,
        _ => ModSide.Unknown
    };

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private sealed record SideResult(ModSide ClientSide, ModSide ServerSide);
}
