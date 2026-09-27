using System.Text.Json;
using System.Threading.Tasks;

namespace DeEarthX.Realtime;

public interface ISocketIOServer
{
    void On(string eventName, Func<JsonElement?, Task> handler);

    Task EmitAsync(string eventName, object? payload);

    int ClientCount { get; }
}
