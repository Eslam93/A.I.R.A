using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Proofsheet.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Proofsheet.Infrastructure.Clients;

public sealed class SecEdgarClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly SecEdgarOptions _options;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<SecEdgarClient> _logger;

    public SecEdgarClient(
        HttpClient httpClient,
        IOptions<SecEdgarOptions> options,
        IMemoryCache memoryCache,
        ILogger<SecEdgarClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _memoryCache = memoryCache;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(EnsureTrailingSlash(_options.DataBaseUrl));
        }

        if (!string.IsNullOrWhiteSpace(_options.UserAgent) &&
            !_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }
    }

    public bool Enabled => _options.Enabled;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.UserAgent);

    public async Task<SecTickerProfile?> GetTickerProfileAsync(string identifier, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var tickerMap = await _memoryCache.GetOrCreateAsync(
            "sec:ticker-map",
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
                return await LoadTickerMapAsync(cancellationToken);
            });

        return tickerMap is not null && tickerMap.TryGetValue(identifier.ToUpperInvariant(), out var profile)
            ? profile
            : null;
    }

    public async Task<SecSubmissionsData?> GetSubmissionsAsync(string identifier, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var tickerProfile = await GetTickerProfileAsync(identifier, cancellationToken);
        if (tickerProfile is null)
        {
            return null;
        }

        return await _memoryCache.GetOrCreateAsync(
            $"sec:submissions:{tickerProfile.CikPadded}",
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                return await LoadSubmissionsAsync(tickerProfile, cancellationToken);
            });
    }

    public async Task<SecCompanyFactsData?> GetCompanyFactsAsync(string identifier, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var tickerProfile = await GetTickerProfileAsync(identifier, cancellationToken);
        if (tickerProfile is null)
        {
            return null;
        }

        return await _memoryCache.GetOrCreateAsync(
            $"sec:companyfacts:{tickerProfile.CikPadded}",
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                return await LoadCompanyFactsAsync(tickerProfile, cancellationToken);
            });
    }

    public async Task<IReadOnlyList<SecTickerProfile>> SearchTickerProfilesAsync(string query, int limit, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SecTickerProfile>();
        }

        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length < 2)
        {
            return Array.Empty<SecTickerProfile>();
        }

        var tickerMap = await _memoryCache.GetOrCreateAsync(
            "sec:ticker-map",
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
                return await LoadTickerMapAsync(cancellationToken);
            });

        if (tickerMap is null || tickerMap.Count == 0)
        {
            return Array.Empty<SecTickerProfile>();
        }

        return tickerMap.Values
            .Select(profile => new
            {
                Profile = profile,
                Score = CalculateMatchScore(profile, normalizedQuery)
            })
            .Where(item => item.Score < int.MaxValue)
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Profile.Identifier, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 20))
            .Select(item => item.Profile)
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<string, SecTickerProfile>> LoadTickerMapAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(new Uri(_options.MappingUrl), cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("fields", out var fieldsElement) ||
            !root.TryGetProperty("data", out var dataElement))
        {
            return new Dictionary<string, SecTickerProfile>(StringComparer.OrdinalIgnoreCase);
        }

        var fieldIndexes = fieldsElement
            .EnumerateArray()
            .Select((item, index) => new { Name = item.GetString(), Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToDictionary(item => item.Name!, item => item.Index, StringComparer.OrdinalIgnoreCase);

        if (!fieldIndexes.TryGetValue("ticker", out var tickerIndex) ||
            !fieldIndexes.TryGetValue("name", out var nameIndex) ||
            !fieldIndexes.TryGetValue("cik", out var cikIndex))
        {
            return new Dictionary<string, SecTickerProfile>(StringComparer.OrdinalIgnoreCase);
        }

        var exchangeIndex = fieldIndexes.TryGetValue("exchange", out var resolvedExchangeIndex)
            ? resolvedExchangeIndex
            : -1;

        var profiles = new Dictionary<string, SecTickerProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in dataElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var values = row.EnumerateArray().ToArray();
            var ticker = GetArrayString(values, tickerIndex);
            var companyName = GetArrayString(values, nameIndex);
            var cik = GetArrayString(values, cikIndex);
            var exchange = exchangeIndex >= 0 ? GetArrayString(values, exchangeIndex) : null;

            if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(cik))
            {
                continue;
            }

            var normalizedTicker = ticker.Trim().ToUpperInvariant();
            profiles[normalizedTicker] = new SecTickerProfile(
                normalizedTicker,
                companyName.Trim(),
                exchange?.Trim(),
                cik.PadLeft(10, '0'));
        }

        return profiles;
    }

    private async Task<SecSubmissionsData?> LoadSubmissionsAsync(SecTickerProfile tickerProfile, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"submissions/CIK{tickerProfile.CikPadded}.json", cancellationToken);
        var root = document.RootElement;

        var companyName = TryGetString(root, "name") ?? tickerProfile.CompanyName;
        var tickers = TryGetStringArray(root, "tickers");
        var exchanges = TryGetStringArray(root, "exchanges");
        var recentFilings = ParseRecentFilings(root, tickerProfile.CikPadded);

        return new SecSubmissionsData(
            tickerProfile.CikPadded,
            companyName,
            tickers,
            exchanges,
            recentFilings);
    }

    private async Task<SecCompanyFactsData?> LoadCompanyFactsAsync(SecTickerProfile tickerProfile, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"api/xbrl/companyfacts/CIK{tickerProfile.CikPadded}.json", cancellationToken);
        var root = document.RootElement;
        return new SecCompanyFactsData(
            tickerProfile.CikPadded,
            TryGetString(root, "entityName") ?? tickerProfile.CompanyName,
            root.Clone());
    }

    private async Task<JsonDocument> GetJsonAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativePath, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SEC request for {Path} returned {StatusCode}.", relativePath, response.StatusCode);
            response.EnsureSuccessStatusCode();
        }

        return await ReadJsonDocumentAsync(response, relativePath, cancellationToken);
    }

    private async Task<JsonDocument> GetJsonAsync(Uri absoluteUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SEC request for {Path} returned {StatusCode}.", absoluteUri, response.StatusCode);
            response.EnsureSuccessStatusCode();
        }

        return await ReadJsonDocumentAsync(response, absoluteUri.ToString(), cancellationToken);
    }

    // Since .NET 9, a JSON "null" body deserializes to a JsonDocument whose root is Null instead of to null,
    // so check for both to keep the original "empty payload" failure.
    private static async Task<JsonDocument> ReadJsonDocumentAsync(HttpResponseMessage response, string source, CancellationToken cancellationToken)
    {
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(SerializerOptions, cancellationToken);
        if (document is null || document.RootElement.ValueKind == JsonValueKind.Null)
        {
            document?.Dispose();
            throw new InvalidOperationException($"SEC returned an empty JSON payload for {source}.");
        }

        return document;
    }

    private static IReadOnlyList<SecRecentFiling> ParseRecentFilings(JsonElement root, string cikPadded)
    {
        if (!root.TryGetProperty("filings", out var filingsElement) ||
            !filingsElement.TryGetProperty("recent", out var recentElement))
        {
            return Array.Empty<SecRecentFiling>();
        }

        var forms = GetArray(recentElement, "form");
        var filingDates = GetArray(recentElement, "filingDate");
        var accessionNumbers = GetArray(recentElement, "accessionNumber");
        var primaryDocuments = GetArray(recentElement, "primaryDocument");
        var descriptions = GetArray(recentElement, "primaryDocDescription");

        var maxLength = new[]
        {
            forms.Length,
            filingDates.Length,
            accessionNumbers.Length,
            primaryDocuments.Length,
            descriptions.Length
        }.Max();

        if (maxLength == 0)
        {
            return Array.Empty<SecRecentFiling>();
        }

        var cikWithoutPadding = long.TryParse(cikPadded, NumberStyles.None, CultureInfo.InvariantCulture, out var cikValue)
            ? cikValue.ToString(CultureInfo.InvariantCulture)
            : cikPadded.TrimStart('0');

        var filings = new List<SecRecentFiling>(maxLength);
        for (var index = 0; index < maxLength; index++)
        {
            var form = GetArrayString(forms, index);
            var filingDateRaw = GetArrayString(filingDates, index);
            var accessionNumber = GetArrayString(accessionNumbers, index);
            var primaryDocument = GetArrayString(primaryDocuments, index);
            var description = GetArrayString(descriptions, index);

            if (string.IsNullOrWhiteSpace(form) ||
                string.IsNullOrWhiteSpace(filingDateRaw) ||
                !DateTimeOffset.TryParse(filingDateRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var filingDate))
            {
                continue;
            }

            string? filingUrl = null;
            if (!string.IsNullOrWhiteSpace(accessionNumber) && !string.IsNullOrWhiteSpace(primaryDocument))
            {
                filingUrl = $"https://www.sec.gov/Archives/edgar/data/{cikWithoutPadding}/{accessionNumber.Replace("-", string.Empty, StringComparison.Ordinal)}/{primaryDocument}";
            }

            filings.Add(new SecRecentFiling(form, filingDate, accessionNumber, primaryDocument, description, filingUrl));
        }

        return filings
            .OrderByDescending(item => item.FilingDate)
            .ToArray();
    }

    private static JsonElement[] GetArray(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray().ToArray()
            : [];

    private static string? GetArrayString(JsonElement[] values, int index)
    {
        if (index < 0 || index >= values.Length)
        {
            return null;
        }

        return values[index].ValueKind switch
        {
            JsonValueKind.String => values[index].GetString(),
            JsonValueKind.Number => values[index].GetRawText(),
            _ => null
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string[] TryGetStringArray(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray()
            : [];

    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";

    private static int CalculateMatchScore(SecTickerProfile profile, string query)
    {
        var symbol = profile.Identifier;
        var companyName = profile.CompanyName;

        if (symbol.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (symbol.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (companyName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (symbol.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (companyName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        return int.MaxValue;
    }
}

public sealed record SecTickerProfile(
    string Identifier,
    string CompanyName,
    string? Exchange,
    string CikPadded);

public sealed record SecSubmissionsData(
    string CikPadded,
    string CompanyName,
    IReadOnlyList<string> Tickers,
    IReadOnlyList<string> Exchanges,
    IReadOnlyList<SecRecentFiling> RecentFilings);

public sealed record SecRecentFiling(
    string Form,
    DateTimeOffset FilingDate,
    string? AccessionNumber,
    string? PrimaryDocument,
    string? PrimaryDocDescription,
    string? FilingUrl);

public sealed record SecCompanyFactsData(
    string CikPadded,
    string CompanyName,
    JsonElement CompanyFactsRoot);
