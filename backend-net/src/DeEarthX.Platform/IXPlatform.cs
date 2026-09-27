using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DeEarthX.Core.Models;
using DeEarthX.Realtime;

namespace DeEarthX.Platform;

public interface IXPlatform
{
    ModpackInfo GetInfo(JsonObject manifest);

    Task DownloadFilesAsync(JsonObject manifest, string destPath, IMessageService? message, CancellationToken ct = default);
}
