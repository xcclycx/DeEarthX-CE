using System.Diagnostics;
using DeEarthX.Core.Abstractions;
using DeEarthX.Core.Configuration;
using DeEarthX.Core.Filter;
using DeEarthX.Realtime;

namespace DeEarthX.Dearth;

public sealed class ModFilterService
{
    private readonly IAppDirectoryProvider _appDirectory;
    private readonly ILogService _log;
    private readonly FileExtractor _extractor;
    private readonly FileOperator _operator;
    private readonly HashFilter _hashFilter;
    private readonly DexpubFilter _dexpubFilter;
    private readonly MixinFilter _mixinFilter;
    private readonly ModrinthFilter _modrinthFilter;

    public ModFilterService(
        IAppDirectoryProvider appDirectory,
        ILogService log,
        FileExtractor extractor,
        FileOperator fileOperator,
        HashFilter hashFilter,
        DexpubFilter dexpubFilter,
        MixinFilter mixinFilter,
        ModrinthFilter modrinthFilter)
    {
        _appDirectory = appDirectory;
        _log = log;
        _extractor = extractor;
        _operator = fileOperator;
        _hashFilter = hashFilter;
        _dexpubFilter = dexpubFilter;
        _mixinFilter = mixinFilter;
        _modrinthFilter = modrinthFilter;
    }

    public async Task<FilterResult> FilterAsync(
        string modsDir,
        string modpackName,
        DeEarthXConfig config,
        IEnumerable<IFilterStrategy>? extraStrategies,
        IMessageService? message,
        CancellationToken ct = default)
    {
        var startTime = Stopwatch.StartNew();
        var filterConfig = new DeEarthXFilterConfig(config);

        try
        {
            _log.Info("开始模组筛选流程");
            var files = await _extractor.ExtractFilesInfoAsync(modsDir, ct).ConfigureAwait(false);

            if (message is not null)
            {
                await message.FilterModsStart(files.Count).ConfigureAwait(false);
            }

            var clientMods = new HashSet<string>(StringComparer.Ordinal);
            var processedFiles = new HashSet<string>(StringComparer.Ordinal);

            const bool useReplacement = false;

            if (!useReplacement)
            {
                var dexpubTask = filterConfig.Dexpub
                    ? _dexpubFilter.CheckAsync(files)
                    : Task.FromResult<(HashSet<string> Client, HashSet<string> Server)>((new(StringComparer.Ordinal), new(StringComparer.Ordinal)));
                var mixinTask = filterConfig.Mixins
                    ? _mixinFilter.FilterBatchAsync(files)
                    : Task.FromResult(new HashSet<string>(StringComparer.Ordinal));

                await Task.WhenAll(dexpubTask, mixinTask).ConfigureAwait(false);
                var dexpubResult = await dexpubTask.ConfigureAwait(false);
                var mixinResult = await mixinTask.ConfigureAwait(false);

                if (filterConfig.Dexpub)
                {
                    _log.Info("Galaxy Square (dexpub) 检查完成", new { client = dexpubResult.Client.Count, server = dexpubResult.Server.Count });
                    foreach (var m in dexpubResult.Client)
                    {
                        processedFiles.Add(m);
                        clientMods.Add(m);
                    }

                    if (message is not null)
                    {
                        await message.FilterModsProgress(processedFiles.Count, files.Count, "Galaxy Square (dexpub) 检查").ConfigureAwait(false);
                    }
                }

                if (filterConfig.Mixins)
                {
                    _log.Info("Mixin 检查完成", new { count = mixinResult.Count });
                    foreach (var m in mixinResult)
                    {
                        processedFiles.Add(m);
                        clientMods.Add(m);
                    }

                    if (message is not null)
                    {
                        await message.FilterModsProgress(processedFiles.Count, files.Count, "Mixin 检查").ConfigureAwait(false);
                    }
                }

                if (filterConfig.Modrinth)
                {
                    _log.Info("开始 Modrinth API 检查客户端模组");
                    var unprocessed = files.Where(f => !processedFiles.Contains(f.FilePath)).ToList();
                    var modrinthMods = await _modrinthFilter.FilterBatchAsync(unprocessed).ConfigureAwait(false);
                    foreach (var m in modrinthMods)
                    {
                        processedFiles.Add(m);
                        clientMods.Add(m);
                    }

                    if (message is not null)
                    {
                        await message.FilterModsProgress(processedFiles.Count, files.Count, "Modrinth API 检查").ConfigureAwait(false);
                    }
                }

                if (filterConfig.Hashes)
                {
                    _log.Info("开始 Hash 检查客户端模组");
                    var unprocessed = files.Where(f => !processedFiles.Contains(f.FilePath)).ToList();
                    var hashMods = await _hashFilter.FilterBatchAsync(unprocessed).ConfigureAwait(false);
                    foreach (var m in hashMods)
                    {
                        clientMods.Add(m);
                    }

                    if (message is not null)
                    {
                        await message.FilterModsProgress(processedFiles.Count, files.Count, "Hash 检查").ConfigureAwait(false);
                    }
                }
            }

            if (extraStrategies is not null)
            {
                foreach (var strategy in extraStrategies)
                {
                    _log.Info($"开始插件策略检查: {strategy.Name}");
                    var unprocessed = files.Where(f => !processedFiles.Contains(f.FilePath)).ToList();
                    if (unprocessed.Count == 0)
                    {
                        break;
                    }

                    var adapter = new PluginStrategyAdapter(strategy, _extractor);
                    var mods = await adapter.FilterBatchAsync(unprocessed).ConfigureAwait(false);
                    foreach (var m in mods)
                    {
                        processedFiles.Add(m);
                        clientMods.Add(m);
                    }

                    if (message is not null)
                    {
                        await message.FilterModsProgress(processedFiles.Count, files.Count, strategy.Name).ConfigureAwait(false);
                    }
                }
            }

            var uniqueMods = clientMods.ToList();
            _log.Info("识别到客户端模组", new { count = uniqueMods.Count });

            var moveDir = Path.Combine(_appDirectory.GetAppDirectory(), ".rubbish", modpackName);
            var (success, error, skipped) = await _operator.MoveFilesAsync(uniqueMods, moveDir, ct).ConfigureAwait(false);

            startTime.Stop();
            var duration = startTime.ElapsedMilliseconds;

            if (message is not null)
            {
                await message.FilterModsComplete(uniqueMods.Count, success, duration).ConfigureAwait(false);
            }

            _log.Info("模组筛选流程完成", new
            {
                filtered = uniqueMods.Count,
                moved = success,
                skipped,
                error
            });

            return new FilterResult(uniqueMods.Count, success, skipped, error);
        }
        catch (Exception ex)
        {
            if (message is not null)
            {
                await message.FilterModsError(ex.Message).ConfigureAwait(false);
            }

            throw;
        }
    }
}
