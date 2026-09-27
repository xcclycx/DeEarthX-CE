using DeEarthX.Core.Abstractions;

namespace DeEarthX.Dearth;

public sealed class FileOperator
{
    private readonly ILogService _log;

    public FileOperator(ILogService log)
    {
        _log = log;
    }

    public async Task<(int Success, int Error, int Skipped)> MoveFilesAsync(
        IEnumerable<string> sourcePaths, string moveDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(moveDir);

        var success = 0;
        var error = 0;
        var skipped = 0;

        foreach (var sourcePath in sourcePaths)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(sourcePath))
            {
                _log.Warn($"文件不存在，跳过: {sourcePath}");
                skipped++;
                continue;
            }

            var filename = Path.GetFileName(sourcePath);
            var targetPath = Path.Combine(moveDir, filename);

            try
            {
                await Task.Run(() =>
                {
                    for (var attempt = 1; attempt <= 5; attempt++)
                    {
                        try
                        {
                            if (File.Exists(targetPath))
                            {
                                File.Delete(targetPath);
                            }
                            File.Move(sourcePath, targetPath);
                            return;
                        }
                        catch (IOException) when (attempt < 5)
                        {
                            Thread.Sleep(200 * attempt);
                        }
                        catch (UnauthorizedAccessException) when (attempt < 5)
                        {
                            Thread.Sleep(200 * attempt);
                        }
                    }
                }, ct).ConfigureAwait(false);

                success++;
            }
            catch (Exception ex)
            {
                _log.Error($"移动文件失败: {sourcePath}", ex);
                error++;
            }
        }

        _log.Info($"文件移动完成: success={success}, error={error}, skipped={skipped}");
        return (success, error, skipped);
    }
}
