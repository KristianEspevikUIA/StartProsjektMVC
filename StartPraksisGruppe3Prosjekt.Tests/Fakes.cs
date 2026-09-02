using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The two ambient services the code under test asks for and that a unit test has to supply
/// itself: where the content root is, and what the current request is.
/// </summary>
internal sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public FakeWebHostEnvironment(string contentRootPath, string? environmentName = null)
    {
        ContentRootPath = contentRootPath;
        WebRootPath = contentRootPath;
        EnvironmentName = environmentName ?? Environments.Production;
        ApplicationName = "StartPraksisGruppe3Prosjekt.Tests";
        ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
        WebRootFileProvider = ContentRootFileProvider;
    }

    public string ApplicationName { get; set; }

    public string ContentRootPath { get; set; }

    public IFileProvider ContentRootFileProvider { get; set; }

    public string EnvironmentName { get; set; }

    public string WebRootPath { get; set; }

    public IFileProvider WebRootFileProvider { get; set; }
}

/// <summary>
/// One request, with readable cookies. PeriodSelection both reads the request's cookies and
/// writes to the response's, so a test needs a real HttpContext rather than a null one.
/// </summary>
internal sealed class FakeHttpContextAccessor : IHttpContextAccessor
{
    public FakeHttpContextAccessor(params (string Name, string Value)[] cookies)
    {
        var context = new DefaultHttpContext();

        if (cookies.Length > 0)
        {
            context.Request.Headers.Cookie =
                string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        }

        HttpContext = context;
    }

    public HttpContext? HttpContext { get; set; }

    /// <summary>The cookies the code under test set on the response, by name.</summary>
    public IReadOnlyDictionary<string, string> ResponseSetCookies =>
        HttpContext!.Response.Headers.SetCookie
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!.Split(';')[0].Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty);
}

/// <summary>
/// An access log that records nothing and has nothing to report.
///
/// FiveCFeedbackBuilder reads the log to show the player who has looked at them. Tests that
/// are about something else -- redaction, staging -- want that list empty, and an empty log
/// is a real state rather than a contrived one: it is what a player sees before any coach
/// has opened them.
///
/// PlayerAccessLogTests covers the log itself, against a real database.
/// </summary>
internal sealed class FakePlayerAccessLog : IPlayerAccessLog
{
    public Task RecordAsync(
        ClaimsPrincipal user,
        int playerId,
        string context,
        int? roundId = null,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<PlayerAccessEvent>> GetForPlayerAsync(
        int playerId,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlayerAccessEvent>>(Array.Empty<PlayerAccessEvent>());
}
