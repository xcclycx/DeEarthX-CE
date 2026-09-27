using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using DeEarthX.Core;
using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Downloads;
using DeEarthX.Infrastructure.Http;
using DeEarthX.Infrastructure.Zip;

namespace DeEarthX.Templates;

public sealed class TemplateManager
{
    /// <summary>默认的模板商店地址。</summary>
    public const string DefaultStoreUrl = "http://git.xcclyc.com.cn/xcclyc/DeEarthX-CE-Tems/raw/branch/main/template_stor.json";

    /// <summary>
    /// 模板商店 JSON 地址。
    ///
    /// 上游站点可能迁移、改名或临时拒绝访问（实测该站整站返回 403、HTTPS 握手失败），
    /// 因此允许用环境变量 <c>DEEARTHX_TEMPLATES_STORE_URL</c> 覆盖，换源时无需重新编译发版。
    /// </summary>
    public static string StoreUrl
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("DEEARTHX_TEMPLATES_STORE_URL");
            return string.IsNullOrWhiteSpace(custom) ? DefaultStoreUrl : custom.Trim();
        }
    }

    private const int BufferSize = 81920;

    private readonly IAppDirectoryProvider _appDirectoryProvider;
    private readonly ILogService _log;
    private readonly IZipService _zipService;
    private readonly IDeEarthXHttpService _httpService;
    private readonly IDownloadService _downloadService;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly ConcurrentDictionary<string, PendingInstall> _pendingInstalls = new();

    public TemplateManager(
        IAppDirectoryProvider appDirectoryProvider,
        ILogService log,
        IZipService zipService,
        IDeEarthXHttpService httpService,
        IDownloadService downloadService,
        IHttpClientFactory httpClientFactory)
    {
        _appDirectoryProvider = appDirectoryProvider;
        _log = log;
        _zipService = zipService;
        _httpService = httpService;
        _downloadService = downloadService;
        _httpClientFactory = httpClientFactory;
    }

    private string TemplatesRoot => Path.Combine(_appDirectoryProvider.GetAppDirectory(), "templates");

    public async Task<List<Template>> GetTemplatesAsync(CancellationToken ct = default)
    {
        var root = TemplatesRoot;
        Directory.CreateDirectory(root);

        var list = new List<Template>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            ct.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(dir, "metadata.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var metadata = await ReadMetadataAsync(metadataPath, ct).ConfigureAwait(false);
                if (metadata is null)
                {
                    continue;
                }

                var id = Path.GetFileName(dir);
                list.Add(new Template(id, metadata, dir));
            }
            catch (Exception ex)
            {
                _log.Warn($"读取模板 metadata 失败: {dir}", ex);
            }
        }

        return list;
    }

    public async Task<Template?> GetTemplateAsync(string id)
    {
        var dir = Path.Combine(TemplatesRoot, id);
        var metadataPath = Path.Combine(dir, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        var metadata = await ReadMetadataAsync(metadataPath, default).ConfigureAwait(false);
        if (metadata is null)
        {
            return null;
        }

        return new Template(id, metadata, dir);
    }

    public async Task<Template> CreateTemplateAsync(string id, string name, CancellationToken ct = default)
    {
        var root = TemplatesRoot;
        Directory.CreateDirectory(root);

        var dir = Path.Combine(root, id);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "data"));

        var metadata = new TemplateMetadata
        {
            Id = id,
            Name = name,
            Version = "1.0.0",
            Description = string.Empty,
            Author = string.Empty,
            Created = DateTime.Now.ToString("yyyy-MM-dd"),
            Type = "template"
        };

        await WriteMetadataAsync(Path.Combine(dir, "metadata.json"), metadata, ct).ConfigureAwait(false);
        return new Template(id, metadata, dir);
    }

    public Task DeleteTemplateAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var dir = Path.Combine(TemplatesRoot, id);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        return Task.CompletedTask;
    }

    public async Task UpdateTemplateAsync(string id, TemplateMetadata metadata, CancellationToken ct = default)
    {
        var dir = Path.Combine(TemplatesRoot, id);
        var metadataPath = Path.Combine(dir, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            throw new DirectoryNotFoundException($"模板 {id} 不存在");
        }

        metadata.Id = id;
        await WriteMetadataAsync(metadataPath, metadata, ct).ConfigureAwait(false);
    }

    public Task OpenTemplateFolderAsync(string id)
    {
        var dir = Path.Combine(TemplatesRoot, id);
        Directory.CreateDirectory(dir);

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "explorer.exe";
        }
        else if (OperatingSystem.IsMacOS())
        {
            startInfo.FileName = "open";
        }
        else
        {
            startInfo.FileName = "xdg-open";
        }

        startInfo.Arguments = $"\"{dir}\"";

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _log.Warn($"打开模板文件夹失败: {dir}", ex);
        }

        return Task.CompletedTask;
    }

    public async Task<byte[]> ExportZipAsync(string id, CancellationToken ct = default)
    {
        var dir = Path.Combine(TemplatesRoot, id);
        var metadataPath = Path.Combine(dir, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            throw new DirectoryNotFoundException($"模板 {id} 不存在");
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"template-export-{Guid.NewGuid():N}.zip");
        try
        {
            await _zipService.CreateZipAsync(dir, tempFile, ct: ct).ConfigureAwait(false);
            return await File.ReadAllBytesAsync(tempFile, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    public async Task<Template> ImportZipAsync(byte[] zipBytes, CancellationToken ct = default)
    {
        var root = TemplatesRoot;
        Directory.CreateDirectory(root);

        TemplateMetadata? metadata = null;
        var metaBytes = _zipService.ReadEntry(zipBytes, "metadata.json");
        if (metaBytes is { Length: > 0 })
        {
            try
            {
                metadata = JsonSerializer.Deserialize<TemplateMetadata>(metaBytes, DeEarthXJsonOptions.Default);
            }
            catch (Exception ex)
            {
                _log.Warn("解析压缩包内 metadata.json 失败", ex);
            }
        }

        var id = !string.IsNullOrWhiteSpace(metadata?.Id) ? metadata!.Id! : Guid.NewGuid().ToString("N");
        var destDir = Path.Combine(root, id);
        if (Directory.Exists(destDir))
        {
            id = $"template-{Guid.NewGuid():N}";
            destDir = Path.Combine(root, id);
        }

        await _zipService.ExtractToDirectoryAsync(zipBytes, destDir, ct).ConfigureAwait(false);
        metadata ??= new TemplateMetadata { Id = id };

        return new Template(id, metadata, destDir);
    }

    public string InitInstallFromUrl(string url, string? clientRequestId = null, long resumeFrom = 0)
    {
        var requestId = string.IsNullOrEmpty(clientRequestId) ? Guid.NewGuid().ToString("N") : clientRequestId;
        var tempPath = Path.Combine(Path.GetTempPath(), $"template-{requestId}.zip");
        var downloaded = resumeFrom > 0 ? resumeFrom : (File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0);
        _pendingInstalls[requestId] = new PendingInstall(url, tempPath, downloaded);
        return requestId;
    }

    public async IAsyncEnumerable<TemplateInstallEvent> StreamInstallFromUrlAsync(
        string requestId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_pendingInstalls.TryGetValue(requestId, out var pending) || pending is null)
        {
            yield return new TemplateInstallEvent("error", null, null, null, "无效或已过期的 requestId", null);
            yield break;
        }

        yield return new TemplateInstallEvent("init", null, pending.Downloaded, null, null, null);

        var channel = Channel.CreateUnbounded<TemplateInstallEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        var produceTask = Task.Run(async () =>
        {
            try
            {
                await ProduceInstallAsync(pending, channel.Writer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                channel.Writer.TryComplete();
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"从 URL 安装模板失败: {pending.Url}", ex);
                await channel.Writer.WriteAsync(new TemplateInstallEvent("error", null, null, null, ex.Message, null), ct).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.TryComplete();
                _pendingInstalls.TryRemove(requestId, out _);
                TryDelete(pending.TempPath);
            }
        }, ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return evt;
        }

        await produceTask.ConfigureAwait(false);
    }

    private async Task ProduceInstallAsync(
        PendingInstall pending,
        ChannelWriter<TemplateInstallEvent> writer,
        CancellationToken ct)
    {
        var client = CreateInstallClient();

        long totalSize = 0;
        var supportsRange = false;

        try
        {
            using (var headRequest = new HttpRequestMessage(HttpMethod.Head, pending.Url))
            using (var headResponse = await client.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                totalSize = headResponse.Content.Headers.ContentLength ?? 0;
                var acceptRanges = headResponse.Headers.AcceptRanges.FirstOrDefault();
                supportsRange = string.Equals(acceptRanges, "bytes", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            _log.Debug($"HEAD 请求失败，回退到普通下载: {pending.Url}");
        }

        var resumeFrom = (supportsRange && totalSize > 0 && pending.Downloaded > 0 && pending.Downloaded < totalSize)
            ? pending.Downloaded
            : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, pending.Url);
        FileMode fileMode;
        if (resumeFrom > 0)
        {
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
            fileMode = FileMode.Append;
        }
        else
        {
            fileMode = FileMode.Create;
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK && resumeFrom > 0)
        {
            resumeFrom = 0;
            fileMode = FileMode.Create;
            totalSize = response.Content.Headers.ContentLength ?? totalSize;
        }
        else if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange is not null && contentRange.HasLength)
            {
                totalSize = contentRange.Length ?? totalSize;
            }
            else if (response.Content.Headers.ContentLength.HasValue)
            {
                totalSize = resumeFrom + response.Content.Headers.ContentLength.Value;
            }

            response.EnsureSuccessStatusCode();
        }
        else
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength.HasValue)
            {
                totalSize = response.Content.Headers.ContentLength.Value;
            }
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dest = new FileStream(pending.TempPath, fileMode, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);

        var buffer = new byte[BufferSize];
        int read;
        long downloaded = resumeFrom;
        var lastPercent = resumeFrom > 0 && totalSize > 0 ? ComputePercent(resumeFrom, totalSize) : -1;

        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;

            var percent = ComputePercent(downloaded, totalSize);
            if (percent == lastPercent)
            {
                continue;
            }

            lastPercent = percent;
            await writer.WriteAsync(
                new TemplateInstallEvent("progress", percent, downloaded, totalSize > 0 ? totalSize : null, null, null),
                ct).ConfigureAwait(false);
        }

        if (totalSize > 0 && lastPercent < 100)
        {
            await writer.WriteAsync(new TemplateInstallEvent("progress", 100, totalSize, totalSize, null, null), ct).ConfigureAwait(false);
        }

        var zipBytes = await File.ReadAllBytesAsync(pending.TempPath, ct).ConfigureAwait(false);
        var template = await ImportZipAsync(zipBytes, ct).ConfigureAwait(false);
        await writer.WriteAsync(new TemplateInstallEvent("complete", null, null, null, null, template.Id), ct).ConfigureAwait(false);
    }

    public Task<object> GetStoreAsync(CancellationToken ct = default)
    {
        return _httpService.GetJsonAsync<object>(StoreUrl, ct);
    }

    private HttpClient CreateInstallClient()
    {
        HttpClient client;
        try
        {
            client = _httpClientFactory.CreateClient(nameof(TemplateManager));
        }
        catch
        {
            client = new HttpClient();
        }

        client.Timeout = TimeSpan.FromMinutes(10);
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(DeEarthXHttpService.UserAgent);
        }

        return client;
    }

    private static async Task<TemplateMetadata?> ReadMetadataAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<TemplateMetadata>(stream, DeEarthXJsonOptions.Default, ct).ConfigureAwait(false);
    }

    private static async Task WriteMetadataAsync(string path, TemplateMetadata metadata, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, metadata, DeEarthXJsonOptions.Default, ct).ConfigureAwait(false);
    }

    private static int ComputePercent(long downloaded, long total)
    {
        if (total > 0)
        {
            return (int)Math.Round((double)downloaded / total * 100);
        }

        var megabytes = downloaded / 1024.0 / 1024.0;
        return (int)Math.Min(90, Math.Round(megabytes * 10));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record PendingInstall(string Url, string TempPath, long Downloaded);
}
