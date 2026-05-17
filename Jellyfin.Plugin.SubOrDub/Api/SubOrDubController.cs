using Jellyfin.Plugin.SubOrDub.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubOrDub.Api;

/// <summary>
/// Library info returned to the configuration UI.
/// </summary>
public class LibraryInfo
{
    /// <summary>Gets or sets the library item ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the library display name.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Request/response body for plugin configuration.
/// </summary>
public class SubOrDubConfigDto
{
    /// <summary>Gets or sets the preferred language ISO 639-2 code.</summary>
    public string DefaultLanguage { get; set; } = "eng";
}

/// <summary>
/// Response wrapper for scan results with optional re-scan warning.
/// </summary>
public class ScanResultsResponse
{
    /// <summary>Gets or sets the (optionally filtered) scan results.</summary>
    public ScanResults? Data { get; set; }

    /// <summary>Gets or sets whether the cached language differs from the current configuration.</summary>
    public bool LanguageMismatch { get; set; }

    /// <summary>Gets or sets the currently configured language.</summary>
    public string ConfiguredLanguage { get; set; } = string.Empty;
}

/// <summary>
/// API controller for the Sub or Dub plugin.
/// </summary>
[ApiController]
[Route("SubOrDub")]
[Authorize(Policy = "RequiresElevation")]
public class SubOrDubController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly SubOrDubScanService _scanService;
    private readonly ILogger<SubOrDubController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubOrDubController"/> class.
    /// </summary>
    public SubOrDubController(
        ILibraryManager libraryManager,
        SubOrDubScanService scanService,
        ILogger<SubOrDubController> logger)
    {
        _libraryManager = libraryManager;
        _scanService = scanService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all movie and TV show libraries.
    /// </summary>
    [HttpGet("Libraries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<LibraryInfo>> GetLibraries()
    {
        var libraries = _libraryManager.GetVirtualFolders()
            .Where(f => f.CollectionType == CollectionTypeOptions.movies
                     || f.CollectionType == CollectionTypeOptions.tvshows)
            .Select(f => new LibraryInfo { Id = f.ItemId, Name = f.Name })
            .ToList();

        return Ok(libraries);
    }

    /// <summary>
    /// Gets cached scan results, with optional server-side filtering.
    /// </summary>
    /// <param name="status">Filter by status: Dubbed, Subbed, Both, NotAvailable.</param>
    /// <param name="libraryId">Filter by library ID.</param>
    /// <param name="mediaType">Filter by type: Movie, Series.</param>
    /// <param name="search">Filter by name substring (case-insensitive).</param>
    [HttpGet("Results")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ScanResultsResponse> GetResults(
        [FromQuery] string? status = null,
        [FromQuery] string? libraryId = null,
        [FromQuery] string? mediaType = null,
        [FromQuery] string? search = null)
    {
        var config = Plugin.Instance!.Configuration;
        var cached = _scanService.LoadCachedResults();

        var response = new ScanResultsResponse
        {
            ConfiguredLanguage = config.DefaultLanguage,
            LanguageMismatch = cached is not null
                && !string.Equals(cached.ScannedLanguage, config.DefaultLanguage, StringComparison.OrdinalIgnoreCase)
        };

        if (cached is null)
        {
            response.Data = null;
            return Ok(response);
        }

        var filtered = cached.Results.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            filtered = filtered.Where(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(libraryId) && Guid.TryParse(libraryId, out var libGuid))
        {
            filtered = filtered.Where(r => r.LibraryId == libGuid);
        }

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            filtered = filtered.Where(r => string.Equals(r.MediaType, mediaType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(r => r.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        response.Data = new ScanResults
        {
            LastScanTime = cached.LastScanTime,
            ScannedLanguage = cached.ScannedLanguage,
            Results = filtered.OrderBy(r => r.Name).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Gets the current plugin configuration.
    /// </summary>
    [HttpGet("Config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<SubOrDubConfigDto> GetConfig()
    {
        var config = Plugin.Instance!.Configuration;
        return Ok(new SubOrDubConfigDto { DefaultLanguage = config.DefaultLanguage });
    }

    /// <summary>
    /// Saves the plugin configuration.
    /// </summary>
    [HttpPost("Config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult SaveConfig([FromBody] SubOrDubConfigDto request)
    {
        if (request is null)
        {
            return BadRequest();
        }

        var config = Plugin.Instance!.Configuration;
        config.DefaultLanguage = request.DefaultLanguage ?? "eng";
        Plugin.Instance.SaveConfiguration();

        _logger.LogInformation("[SubOrDub] Configuration saved. DefaultLanguage='{Lang}'.", config.DefaultLanguage);
        return NoContent();
    }

    /// <summary>
    /// Triggers a full library scan asynchronously. Returns 200 immediately; scan runs in background.
    /// </summary>
    [HttpPost("Scan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult TriggerScan()
    {
        _logger.LogInformation("[SubOrDub] Manual scan triggered via API.");
        _ = Task.Run(() => _scanService.RunScanAsync(CancellationToken.None));
        return Ok(new { message = "Scan started." });
    }
}
