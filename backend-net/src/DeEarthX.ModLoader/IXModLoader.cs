namespace DeEarthX.ModLoader;

public interface IXModLoader
{
    Task SetupAsync(CancellationToken ct = default);

    Task InstallerAsync(CancellationToken ct = default);
}
