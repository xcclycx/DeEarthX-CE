using DeEarthX.Core.Configuration;

namespace DeEarthX.Core.Abstractions;

public interface IConfigService
{
    DeEarthXConfig Get();
    void Write(DeEarthXConfig config);
    void Reload();
}
