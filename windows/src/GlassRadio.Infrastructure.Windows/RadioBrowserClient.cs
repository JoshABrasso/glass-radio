using System.Net;
using System.Net.Http.Json;
using GlassRadio.Application.Abstractions;
using GlassRadio.Domain;

namespace GlassRadio.Infrastructure.Windows;

public sealed class RadioBrowserClient(HttpClient httpClient) : IRadioBrowserClient
{
    private const int CountryPreviewLimit = 600;
    private const int CountryStationLimit = 1800;
    private const int CountryTopStationLimit = 200;

    private static readonly Uri[] Endpoints =
    [
        new("https://de1.api.radio-browser.info/json/"),
        new("https://de2.api.radio-browser.info/json/"),
        new("https://all.api.radio-browser.info/json/"),
        new("https://nl1.api.radio-browser.info/json/"),
        new("https://fr1.api.radio-browser.info/json/"),
        new("https://fi1.api.radio-browser.info/json/")
    ];

    public async Task<IReadOnlyList<RadioStation>> GetCountryStationsPreviewAsync(CountryPreset country, CancellationToken cancellationToken)
    {
        var code = country.CountryCode.ToUpperInvariant();
        var queries = new[]
        {
            $"stations/bycountrycodeexact/{Uri.EscapeDataString(code)}?hidebroken=true&order=votes&reverse=true&limit={CountryPreviewLimit}",
            $"stations/search?countrycode={Uri.EscapeDataString(code)}&hidebroken=true&order=clickcount&reverse=true&limit={CountryPreviewLimit}"
        };

        var batches = await Task.WhenAll(queries.Select(query => GetCountryBatchAsync(query, cancellationToken))).ConfigureAwait(false);
        var stations = batches
            .SelectMany(static batch => batch)
            .GroupBy(static station => station.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        if (stations.Length == 0)
            throw new HttpRequestException("Radio Browser returned no preview stations for the selected country.");

        return stations;
    }

    public async Task<IReadOnlyList<RadioStation>> GetCountryStationsAsync(CountryPreset country, CancellationToken cancellationToken)
    {
        // Radio Browser's exact country-name route is not reliable for names such as
        // "United Kingdom". The Mac implementation uses the ISO country-code route
        // and ranked pulls, which also gives the UI meaningful top-station ordering.
        var code = country.CountryCode.ToUpperInvariant();
        var topQuery = $"stations/bycountrycodeexact/{Uri.EscapeDataString(code)}?hidebroken=true&order=clickcount&reverse=true&limit={CountryTopStationLimit}";
        var queries = new[]
        {
            $"stations/bycountrycodeexact/{Uri.EscapeDataString(code)}?hidebroken=true&order=votes&reverse=true&limit={CountryStationLimit}",
            $"stations/bycountrycodeexact/{Uri.EscapeDataString(code)}?hidebroken=true&order=clickcount&reverse=true&limit={CountryStationLimit}",
            $"stations/bycountrycodeexact/{Uri.EscapeDataString(code)}?hidebroken=true&order=name&reverse=false&limit={CountryStationLimit}",
            $"stations/bycountryexact/{Uri.EscapeDataString(country.ApiName)}?hidebroken=true&order=votes&reverse=true&limit={CountryStationLimit}",
            $"stations/bycountryexact/{Uri.EscapeDataString(country.ApiName)}?hidebroken=true&order=clickcount&reverse=true&limit={CountryStationLimit}",
            $"stations/bycountryexact/{Uri.EscapeDataString(country.ApiName)}?hidebroken=true&order=name&reverse=false&limit={CountryStationLimit}",
            $"stations/search?countrycode={Uri.EscapeDataString(code)}&hidebroken=true&order=votes&reverse=true&limit={CountryStationLimit}",
            $"stations/search?countrycode={Uri.EscapeDataString(code)}&hidebroken=true&order=clickcount&reverse=true&limit={CountryStationLimit}",
            $"stations/search?country={Uri.EscapeDataString(country.ApiName)}&hidebroken=true&order=votes&reverse=true&limit={CountryStationLimit}"
        };

        var topTask = GetCountryBatchAsync(topQuery, cancellationToken);
        var batchesTask = Task.WhenAll(queries.Select(query => GetCountryBatchAsync(query, cancellationToken)));
        await Task.WhenAll(topTask, batchesTask).ConfigureAwait(false);
        var topStations = await topTask.ConfigureAwait(false);
        var batches = await batchesTask.ConfigureAwait(false);
        if (batches.All(static stations => stations.Count == 0))
            throw new HttpRequestException("Radio Browser returned no stations for the selected country.");

        var topRanks = topStations
            .Select((station, index) => (station.Id, Rank: index + 1))
            .GroupBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Rank, StringComparer.OrdinalIgnoreCase);

        var stations = batches
            .SelectMany(static stations => stations)
            .GroupBy(static station => station.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var station = group.First();
                return topRanks.TryGetValue(station.Id, out var rank)
                    ? station with { CountryTopRank = rank }
                    : station;
            })
            .ToList();

        // The Mac build supplements the ranked country pull with targeted major-
        // brand searches. The country endpoint alone is not guaranteed to contain
        // every approved presentation brand, so without this step the featured
        // rail becomes sparse or silently loses later slots such as Smash Hits.
        foreach (var batch in country.TopBrands
                     .Where(static brand => !string.IsNullOrWhiteSpace(brand))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Chunk(4))
        {
            var brandResults = await Task.WhenAll(batch.Select(brand => GetBrandBatchAsync(country, brand, cancellationToken))).ConfigureAwait(false);
            stations.AddRange(brandResults.SelectMany(static result => result));
        }

        return stations
            .GroupBy(static station => station.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    public async Task<IReadOnlyList<RadioStation>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(query);
        var queries = new[]
        {
            $"stations/search?name={encoded}&hidebroken=true&order=votes&reverse=true&limit=160",
            $"stations/search?state={encoded}&hidebroken=true&order=votes&reverse=true&limit=160",
            $"stations/search?city={encoded}&hidebroken=true&order=votes&reverse=true&limit=160",
            $"stations/search?country={encoded}&hidebroken=true&order=votes&reverse=true&limit=160"
        };

        var batches = await Task.WhenAll(queries.Select(path => GetSearchBatchAsync(path, cancellationToken))).ConfigureAwait(false);
        if (batches.All(static batch => !batch.Succeeded))
            throw new HttpRequestException("All Radio Browser search queries failed.");

        return batches
            .Where(static batch => batch.Succeeded)
            .SelectMany(static batch => batch.Stations)
            .GroupBy(static station => station.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            // Radio Browser's search endpoint is an OR over several fields and some mirrors
            // occasionally return unrelated rows for a broad country/state request. Enforce the
            // same station/city/frequency semantics locally before handing results to either
            // Explore or Quick Search; otherwise a search for "Capital" can visibly begin with a
            // station that contains no matching text at all.
            .Where(station => StationRules.MatchesSearch(station, query))
            .OrderByDescending(station => SearchMatchScore(station, query))
            .ThenByDescending(static station => station.ClickCount ?? 0)
            .ThenBy(static station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int SearchMatchScore(RadioStation station, string query)
    {
        var target = StationRules.Normalize(query);
        var name = StationRules.Normalize(station.Name);
        if (name.Equals(target, StringComparison.Ordinal)) return 1000;
        if (name.StartsWith(target + " ", StringComparison.Ordinal)) return 800;
        if (name.Contains(target, StringComparison.Ordinal)) return 600;
        if (StationRules.Normalize(station.City).Contains(target, StringComparison.Ordinal) ||
            StationRules.Normalize(station.State).Contains(target, StringComparison.Ordinal)) return 400;
        return 200;
    }

    public Task<IReadOnlyList<RadioStation>> GetDiscoverStationsAsync(CancellationToken cancellationToken) =>
        GetDiscoverStationsAsync(Array.Empty<string>(), cancellationToken);

    public async Task<IReadOnlyList<RadioStation>> GetDiscoverStationsAsync(
        IReadOnlyList<string> preferredLanguages,
        CancellationToken cancellationToken)
    {
        // Do not use CountryPreset.All.Take(6): that is alphabetical, not discovery. The Mac
        // page chooses spotlight countries from the user's language profile, then lets the API's
        // votes/clicks ranking choose stations inside those countries. This keeps Explore global
        // without hard-coding a station list or silently showing Argentina/Austria just because
        // they happen to be first alphabetically.
        var spotlightCountries = SelectDiscoveryCountries(preferredLanguages);
        var batches = await Task.WhenAll(spotlightCountries.Select(country =>
            GetDiscoverCountryBatchAsync(country, cancellationToken))).ConfigureAwait(false);
        return batches
            .SelectMany(static stations => stations)
            .GroupBy(static station => station.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<CountryPreset> SelectDiscoveryCountries(IReadOnlyList<string> preferredLanguages)
    {
        var normalized = preferredLanguages
            .Where(static language => !string.IsNullOrWhiteSpace(language))
            .Select(static language => language.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var preferredCountryIds = new List<string>();
        foreach (var language in normalized)
        {
            if (!DiscoveryCountriesByLanguage.TryGetValue(language, out var countries)) continue;
            preferredCountryIds.AddRange(countries);
        }

        // Keep a useful global fallback when a language has no country mapping or the app is
        // opened before onboarding has supplied a profile. These are discovery markets, not a
        // replacement for API ranking and not a hard-coded station selection.
        if (preferredCountryIds.Count == 0)
            preferredCountryIds.AddRange(["uk", "us", "ca", "au", "ie", "nz", "za", "in"]);

        return preferredCountryIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => CountryPreset.All.FirstOrDefault(country => country.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(static country => country is not null)
            .Cast<CountryPreset>()
            .Take(12)
            .ToArray();
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DiscoveryCountriesByLanguage =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = ["uk", "us", "ca", "au", "ie", "nz", "za", "sg", "in", "ph"],
            ["es"] = ["es", "mx", "ar", "co", "cl", "pe", "uy", "ve"],
            ["fr"] = ["fr", "be", "ca", "ch", "lu"],
            ["de"] = ["de", "at", "ch", "lu"],
            ["it"] = ["it", "ch"],
            ["pt"] = ["pt", "br"],
            ["ar"] = ["eg", "sa", "ae"],
            ["ur"] = ["pk", "in"],
            ["hi"] = ["in"],
            ["pa"] = ["pk", "in"],
            ["nl"] = ["nl", "be"],
            ["pl"] = ["pl"],
            ["tr"] = ["tr"],
            ["ru"] = ["us", "de"],
            ["zh"] = ["sg", "ca", "au"],
            ["ja"] = ["jp"],
            ["ko"] = ["kr"],
            ["sw"] = ["ke", "za"],
            ["ro"] = ["ro"],
            ["bn"] = ["in"]
        };

    private async Task<IReadOnlyList<RadioStation>> GetDiscoverCountryBatchAsync(CountryPreset country, CancellationToken cancellationToken)
    {
        try { return await GetCountryStationsPreviewAsync(country, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Array.Empty<RadioStation>(); }
    }

    private async Task<IReadOnlyList<RadioStation>> GetAsync(string path, CancellationToken cancellationToken)
    {
        Exception? last = null;
        var failures = new List<string>();
        foreach (var endpoint in Endpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, path));
                request.Headers.UserAgent.ParseAdd("GlassRadio/1.0");
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound) continue;
                response.EnsureSuccessStatusCode();
                var stations = await response.Content.ReadFromJsonAsync<List<RadioStation>>(cancellationToken: cancellationToken).ConfigureAwait(false);
                return stations?.ToArray() ?? Array.Empty<RadioStation>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                last = ex;
                failures.Add($"{endpoint.Host}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        throw new HttpRequestException($"All Radio Browser endpoints failed. {string.Join(" | ", failures)}", last);
    }

    private async Task<IReadOnlyList<RadioStation>> GetCountryBatchAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A country load is assembled from several equivalent provider pulls. One failed
            // route must not discard the valid batches returned by the other routes.
            return Array.Empty<RadioStation>();
        }
    }

    private async Task<IReadOnlyList<RadioStation>> GetBrandBatchAsync(CountryPreset country, string brand, CancellationToken cancellationToken)
    {
        try
        {
            var code = country.CountryCode.ToUpperInvariant();
            var path = $"stations/search?name={Uri.EscapeDataString(brand)}&countrycode={Uri.EscapeDataString(code)}&hidebroken=true&order=votes&reverse=true&limit=80";
            var stations = await GetAsync(path, cancellationToken).ConfigureAwait(false);
            return stations
                .Where(station => string.Equals(station.CountryCode, code, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(StationRules.Normalize(station.Country), StationRules.Normalize(country.ApiName), StringComparison.Ordinal) ||
                                  string.Equals(StationRules.Normalize(station.Country), StationRules.Normalize(country.DisplayName), StringComparison.Ordinal))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<RadioStation>();
        }
    }

    private async Task<(IReadOnlyList<RadioStation> Stations, bool Succeeded)> GetSearchBatchAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return (await GetAsync(path, cancellationToken).ConfigureAwait(false), true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (Array.Empty<RadioStation>(), false);
        }
    }
}
