namespace DeEarthX.Core.Filter;

public interface IFilterConfig
{
    bool Hashes { get; }
    bool Dexpub { get; }
    bool Mixins { get; }
    bool Modrinth { get; }
}
