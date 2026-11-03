using GlassRadio.Domain;

namespace GlassRadio.Application.Abstractions;

public interface IRadioBrowserClient
{
    // The Mac loader publishes a quick ranked feed before its expanded pull.
    // Implementations that do not have a distinct preview route may use the
    // full request as a safe compatibility default.
    Task<IReadOnlyList<RadioStation>> GetCountryStationsPreviewAsync(CountryPreset country, CancellationToken cancellationToken) =>
        GetCountryStationsAsync(country, cancellationToken);
    Task<IReadOnlyList<RadioStation>> GetCountryStationsAsync(CountryPreset country, CancellationToken cancellationToken);
    Task<IReadOnlyList<RadioStation>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<RadioStation>> GetDiscoverStationsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RadioStation>> GetDiscoverStationsAsync(
        IReadOnlyList<string> preferredLanguages,
        CancellationToken cancellationToken) => GetDiscoverStationsAsync(cancellationToken);
}
