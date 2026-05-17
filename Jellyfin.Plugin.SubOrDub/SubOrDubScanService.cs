using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SubOrDub.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubOrDub;

/// <summary>
/// Core service that scans libraries and determines dubbed/subbed availability for foreign-language content.
/// </summary>
public class SubOrDubScanService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<SubOrDubScanService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="SubOrDubScanService"/> class.
    /// </summary>
    public SubOrDubScanService(
        ILibraryManager libraryManager,
        IApplicationPaths appPaths,
        ILogger<SubOrDubScanService> logger)
    {
        _libraryManager = libraryManager;
        _appPaths = appPaths;
        _logger = logger;
    }

    private string CachePath => Path.Combine(_appPaths.DataPath, "SubOrDub", "scan_results.json");

    /// <summary>
    /// Loads cached scan results from disk, or returns null if none exist.
    /// </summary>
    public ScanResults? LoadCachedResults()
    {
        var path = CachePath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ScanResults>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SubOrDub] Failed to read cache from {Path}.", path);
            return null;
        }
    }

    /// <summary>
    /// Runs a full scan of all movie and TV libraries and persists results.
    /// </summary>
    public async Task RunScanAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            _logger.LogWarning("[SubOrDub] Plugin configuration not available.");
            return;
        }

        var preferredLanguage = config.DefaultLanguage;
        _logger.LogInformation("[SubOrDub] Starting scan for preferred language '{Lang}'.", preferredLanguage);

        var results = new List<MediaLanguageInfo>();
        var allFolders = _libraryManager.GetVirtualFolders();

        foreach (var folder in allFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Guid.TryParse(folder.ItemId, out var libraryGuid))
            {
                _logger.LogWarning("[SubOrDub] Could not parse library ID '{Id}', skipping.", folder.ItemId);
                continue;
            }

            if (folder.CollectionType == CollectionTypeOptions.movies)
            {
                var movies = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Movie],
                    AncestorIds = [libraryGuid],
                    Recursive = true
                })
                .OfType<Movie>()
                .ToList();

                _logger.LogDebug("[SubOrDub] Scanning movie library '{Name}' ({Count} movies).", folder.Name, movies.Count);

                foreach (var movie in movies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = AnalyzeItem(movie, "Movie", preferredLanguage, libraryGuid, folder.Name);
                    if (info is not null)
                    {
                        results.Add(info);
                    }
                }
            }
            else if (folder.CollectionType == CollectionTypeOptions.tvshows)
            {
                var series = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Series],
                    AncestorIds = [libraryGuid],
                    Recursive = true
                })
                .OfType<Series>()
                .ToList();

                _logger.LogDebug("[SubOrDub] Scanning TV library '{Name}' ({Count} series).", folder.Name, series.Count);

                foreach (var show in series)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = AnalyzeSeries(show, preferredLanguage, libraryGuid, folder.Name);
                    if (info is not null)
                    {
                        results.Add(info);
                    }
                }
            }
        }

        var scanResults = new ScanResults
        {
            LastScanTime = DateTime.UtcNow,
            ScannedLanguage = preferredLanguage,
            Results = results
        };

        PersistResults(scanResults);

        _logger.LogInformation("[SubOrDub] Scan complete. {Count} foreign-language items found.", results.Count);
        await Task.CompletedTask;
    }

    private MediaLanguageInfo? AnalyzeItem(
        Video item,
        string mediaType,
        string preferredLanguage,
        Guid libraryId,
        string libraryName)
    {
        var streams = GetStreams(item);
        if (streams.Count == 0)
        {
            return null;
        }

        var originalLanguage = DetectOriginalLanguage(streams);
        if (string.IsNullOrEmpty(originalLanguage))
        {
            return null;
        }

        if (string.Equals(originalLanguage, preferredLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var status = DetermineStatus(streams, preferredLanguage);

        return new MediaLanguageInfo
        {
            Id = item.Id,
            Name = item.Name ?? string.Empty,
            MediaType = mediaType,
            OriginalLanguage = originalLanguage,
            Status = status,
            LibraryId = libraryId,
            LibraryName = libraryName
        };
    }

    private MediaLanguageInfo? AnalyzeSeries(
        Series show,
        string preferredLanguage,
        Guid libraryId,
        string libraryName)
    {
        var firstEpisode = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            AncestorIds = [show.Id],
            Recursive = true
        })
        .OfType<Episode>()
        .Where(e => e.ParentIndexNumber > 0)
        .OrderBy(e => e.ParentIndexNumber)
        .ThenBy(e => e.IndexNumber)
        .FirstOrDefault();

        if (firstEpisode is null)
        {
            _logger.LogDebug("[SubOrDub] No non-special episodes found for series '{Name}', skipping.", show.Name);
            return null;
        }

        var probeResult = AnalyzeItem(firstEpisode, "Series", preferredLanguage, libraryId, libraryName);
        if (probeResult is null)
        {
            return null;
        }

        // Use the series ID/name, not the probe episode's
        return new MediaLanguageInfo
        {
            Id = show.Id,
            Name = show.Name ?? string.Empty,
            MediaType = "Series",
            OriginalLanguage = probeResult.OriginalLanguage,
            Status = probeResult.Status,
            LibraryId = libraryId,
            LibraryName = libraryName
        };
    }

    private IReadOnlyList<MediaStream> GetStreams(Video item)
    {
        try
        {
            // Reload via GetItemById to ensure streams are fully hydrated
            var hydrated = _libraryManager.GetItemById(item.Id) as Video ?? item;
            var sources = hydrated.GetMediaSources(false);
            return sources.FirstOrDefault()?.MediaStreams ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SubOrDub] Could not get streams for item '{Name}'.", item.Name);
            return [];
        }
    }

    private static string DetectOriginalLanguage(IReadOnlyList<MediaStream> streams)
    {
        var audioStreams = streams.Where(s => s.Type == MediaStreamType.Audio).ToList();
        if (audioStreams.Count == 0)
        {
            return string.Empty;
        }

        var primary = audioStreams.FirstOrDefault(s => s.IsDefault) ?? audioStreams[0];
        return primary.Language ?? string.Empty;
    }

    private static string DetermineStatus(IReadOnlyList<MediaStream> streams, string preferredLanguage)
    {
        var isDubbed = streams.Any(s =>
            s.Type == MediaStreamType.Audio &&
            string.Equals(s.Language, preferredLanguage, StringComparison.OrdinalIgnoreCase));

        var isSubbed = streams.Any(s =>
            s.Type == MediaStreamType.Subtitle &&
            string.Equals(s.Language, preferredLanguage, StringComparison.OrdinalIgnoreCase));

        return (isDubbed, isSubbed) switch
        {
            (true, true) => "Both",
            (true, false) => "Dubbed",
            (false, true) => "Subbed",
            _ => "NotAvailable"
        };
    }

    private void PersistResults(ScanResults results)
    {
        var path = CachePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(results, JsonOptions));
            _logger.LogDebug("[SubOrDub] Cache written to {Path}.", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SubOrDub] Failed to write cache to {Path}.", path);
        }
    }
}
