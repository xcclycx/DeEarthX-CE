namespace DeEarthX.Core.Filter;

public interface IFilterStrategy
{
    string Name { get; }

    Task<bool> ShouldFilterAsync(FilterContext context);
}
