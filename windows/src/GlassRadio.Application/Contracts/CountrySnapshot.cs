using GlassRadio.Domain;

namespace GlassRadio.Application.Contracts;

public sealed record CountrySnapshot(
    int SchemaVersion,
    string CountryId,
    DateTimeOffset RetrievedAt,
    IReadOnlyList<RadioStation> Stations)
{
    public const int CurrentSchemaVersion = 1;
}
