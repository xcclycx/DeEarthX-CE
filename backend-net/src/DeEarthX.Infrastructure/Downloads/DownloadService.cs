using System.Net;
using System.Net.Http.Headers;
using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Http;

namespace DeEarthX.Infrastructure.Downloads;

public interface IDownloadService
{
    Task DownloadFileAsync(
        string url,
        string filePath,
        string? expectedHash = null,
        bool forceDownload = false,
        bool useChunked = false,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    Task FastDownloadAsync(List<DownloadItem> items, bool enableHashVerify = true, CancellationToken ct = default);

    Task WFastDownloadAsync(
        List<DownloadItem> items,
        IProgress<DownloadProgress>? progress,
        bool enableHashVerify = true,
        bool useChunked = false,
        CancellationToken ct = default);
}

public sealed class DownloadService : IDownloadService
{
    private const int MaxAttempts = 4;
    private const int FastConcurrency = 32;
    private const int WFastConcurrency = 48;

    private const long DefaultChunkSize = 5L * 1024 * 1024;
    private const int DefaultChunkConcurrency = 8;
    private const long McChunkSize = 256L * 1024;
    private const int McChunkConcurrency = 32;

    private readonly HttpClient _httpClient;
    private readonly ILogService _log;
    private readonly Sha1Service _sha1;

    public DownloadService(HttpClient httpClient, ILogService log, Sha1Service sha1)
    {
        _httpClient = httpClient;
        DeEarthXHttpService.Configure(_httpClient);
        _log = log;
        _sha1 = sha1;
    }

    public async Task DownloadFileAsync(
        string url,
        string filePath,
        string? expectedHash = null,
        bool forceDownload = false,
        bool useChunked = false,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await DownloadOnceAsync(url, filePath, expectedHash, forceDownload, useChunked, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < MaxAttempts)
                {
                    _log.Warn($"{url} 下载失败，正在重试 ({attempt}/{MaxAttempts - 1})");
                }
            }
        }

        throw lastError!;
    }

    private async Task DownloadOnceAsync(
        string url,
        string filePath,
        string? expectedHash,
        bool forceDownload,
        bool useChunked,
        CancellationToken ct)
    {
        if (File.Exists(filePath) && !forceDownload)
        {
            _log.Debug($"文件已存在，跳过: {filePath}");
            if (!string.IsNullOrEmpty(expectedHash) && !_sha1.Verify(filePath, expectedHash))
            {
                _log.Warn($"已存在文件哈希不匹配，将重新下载: {filePath}");
                TryDelete(filePath);
            }
            else
            {
                return;
            }
        }

        _log.Debug($"正在下载 {url} 到 {filePath}");
        try
        {
            EnsureDir(Path.GetDirectoryName(filePath)!);

            if (useChunked)
            {
                await ChunkedDownloadAsync(url, filePath, ct).ConfigureAwait(false);
            }
            else
            {
                await SimpleDownloadAsync(url, filePath, ct).ConfigureAwait(false);
            }

            _log.Debug($"下载 {url} 成功");

            if (!string.IsNullOrEmpty(expectedHash) && !_sha1.Verify(filePath, expectedHash!))
            {
                throw new IOException("文件哈希验证失败，下载的文件可能已损坏");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            TryDelete(filePath);
            throw;
        }
    }

    private async Task SimpleDownloadAsync(string url, string filePath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dest = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await source.CopyToAsync(dest, 81920, ct).ConfigureAwait(false);
    }

    private async Task ChunkedDownloadAsync(string url, string filePath, CancellationToken ct)
    {
        var isMc = MirrorResolver.IsMcMirrorUrl(url);
        var chunkSize = isMc ? McChunkSize : DefaultChunkSize;
        var concurrency = isMc ? McChunkConcurrency : DefaultChunkConcurrency;

        var chunkLabel = chunkSize >= 1024 * 1024
            ? $"{chunkSize / 1024 / 1024}MB"
            : $"{chunkSize / 1024}KB";
        _log.Debug($"开始分块下载 {url}，块大小: {chunkLabel}，并发数: {concurrency}");

        long fileSize = 0;
        var supportsRange = false;

        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            fileSize = headResponse.Content.Headers.ContentLength ?? 0;
            var acceptRanges = headResponse.Headers.AcceptRanges.FirstOrDefault();
            var isBytes = string.Equals(acceptRanges, "bytes", StringComparison.OrdinalIgnoreCase);
            var threshold = isMc ? McChunkSize : chunkSize;
            supportsRange = isBytes && fileSize > threshold;
        }
        catch
        {
            _log.Debug($"HEAD 请求失败，回退到普通下载: {url}");
            await SimpleDownloadAsync(url, filePath, ct).ConfigureAwait(false);
            return;
        }

        if (!supportsRange)
        {
            _log.Debug($"文件较小或服务器不支持分块下载，使用普通下载: {url}");
            await SimpleDownloadAsync(url, filePath, ct).ConfigureAwait(false);
            return;
        }

        var totalChunks = (int)Math.Ceiling(fileSize / (double)chunkSize);
        _log.Debug($"文件大小: {(fileSize / 1024.0 / 1024.0):F2}MB，分 {totalChunks} 个块下载");

        var rangeSupportedBox = new bool[1];
        FileStream fileStream;
        try
        {
            fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous);
            fileStream.SetLength(fileSize);
        }
        catch
        {
            await SimpleDownloadAsync(url, filePath, ct).ConfigureAwait(false);
            return;
        }

        var handle = fileStream.SafeFileHandle;
        try
        {
            await RunWithConcurrencyAsync(
                Enumerable.Range(0, totalChunks),
                Math.Min(concurrency, totalChunks),
                async index =>
                {
                    var start = (long)index * chunkSize;
                    var end = Math.Min(start + chunkSize - 1, fileSize - 1);
                    await DownloadChunkAsync(url, handle, start, end, index, rangeSupportedBox, ct).ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            fileStream.Dispose();
            TryDelete(filePath);
            throw;
        }
        catch (Exception ex)
        {
            fileStream.Dispose();
            TryDelete(filePath);
            if (!rangeSupportedBox[0])
            {
                _log.Warn($"服务器不支持分块下载，切换到普通下载: {url}");
                await SimpleDownloadAsync(url, filePath, ct).ConfigureAwait(false);
                return;
            }
            throw new IOException($"分块下载失败: {url}", ex);
        }

        fileStream.Dispose();
        _log.Debug($"分块下载完成: {filePath}");
    }

    private async Task DownloadChunkAsync(
        string url,
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        long start,
        long end,
        int chunkIndex,
        bool[] rangeSupportedBox,
        CancellationToken ct)
    {
        const int chunkMaxAttempts = 5;

        for (var attempt = 1; attempt <= chunkMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(start, end);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    rangeSupportedBox[0] = false;
                    throw new IOException("服务器不支持范围请求 (返回完整内容)");
                }

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    var wait = Get429Wait(response, attempt);
                    _log.Warn($"遇到 429 错误，等待 {wait / 1000.0:F1} 秒后重试 ({attempt}/{chunkMaxAttempts})");
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                await RandomAccess.WriteAsync(handle, bytes, start, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex) when (ex.Message.Contains("服务器不支持范围请求"))
            {
                rangeSupportedBox[0] = false;
                throw;
            }
            catch (HttpRequestException ex)
            {
                var code = (int?)ex.StatusCode;
                if (code == 429)
                {
                    var wait = Get429Wait(null, attempt);
                    _log.Warn($"遇到 429 错误，等待 {wait / 1000.0:F1} 秒后重试 ({attempt}/{chunkMaxAttempts})");
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                    continue;
                }

                if (code.HasValue)
                {
                    rangeSupportedBox[0] = false;
                    throw new IOException($"服务器返回状态码 {code.Value}，不支持分块下载", ex);
                }

                throw;
            }
        }

        throw new IOException($"下载块 {chunkIndex} 失败，已重试 {chunkMaxAttempts} 次");
    }

    public Task FastDownloadAsync(List<DownloadItem> items, bool enableHashVerify = true, CancellationToken ct = default)
    {
        _log.Info($"开始快速下载 {items.Count} 个文件{(enableHashVerify ? "（启用 hash 验证）" : string.Empty)}");
        return RunWithConcurrencyAsync(items, FastConcurrency, async item =>
        {
            try
            {
                await DownloadFileAsync(item.Url, item.FilePath, enableHashVerify ? item.ExpectedHash : null, ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error($"{item.Url} 下载失败，已重试 {MaxAttempts - 1} 次", ex);
                throw;
            }
        }, ct);
    }

    public Task WFastDownloadAsync(
        List<DownloadItem> items,
        IProgress<DownloadProgress>? progress,
        bool enableHashVerify = true,
        bool useChunked = false,
        CancellationToken ct = default)
    {
        _log.Info($"开始 Web 下载 {items.Count} 个文件{(enableHashVerify ? "（启用 hash 验证）" : string.Empty)}{(useChunked ? "（启用分块下载）" : string.Empty)}");

        var completed = new HashSet<int>();
        var completedLock = new object();
        var total = items.Count;

        return RunWithConcurrencyAsync(
            items.Select((item, idx) => (item, idx)),
            WFastConcurrency,
            async pair =>
            {
                var item = pair.item;
                var index = pair.idx;
                try
                {
                await DownloadFileAsync(
                    item.Url,
                    item.FilePath,
                    enableHashVerify ? item.ExpectedHash : null,
                    useChunked: useChunked,
                    ct: ct).ConfigureAwait(false);

                bool shouldReport;
                lock (completedLock)
                {
                    shouldReport = completed.Add(index);
                }

                if (shouldReport && progress is not null)
                {
                    int done;
                    lock (completedLock)
                    {
                        done = completed.Count;
                    }
                    progress.Report(new DownloadProgress(done, total, item.FilePath));
                }
            }
            catch (Exception ex)
            {
                _log.Error($"{item.Url} 下载失败，已重试 {MaxAttempts - 1} 次", ex);
                throw;
            }
        }, ct);
    }

    private static async Task RunWithConcurrencyAsync<T>(
        IEnumerable<T> source,
        int concurrency,
        Func<T, Task> action,
        CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(Math.Max(1, concurrency));
        var tasks = new List<Task>();
        foreach (var item in source)
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            tasks.Add(RunItem(semaphore, action, item, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task RunItem<T>(SemaphoreSlim semaphore, Func<T, Task> action, T item, CancellationToken ct)
    {
        try
        {
            await action(item).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static int Get429Wait(HttpResponseMessage? response, int attempt)
    {
        if (response is not null &&
            response.Headers.TryGetValues("Retry-After", out var values) &&
            int.TryParse(values.FirstOrDefault(), out var retryAfter))
        {
            return retryAfter * 1000;
        }

        return (int)Math.Min(5000 * Math.Pow(2, attempt), 60000);
    }

    private static void EnsureDir(string dir)
    {
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }
}
