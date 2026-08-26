using Microsoft.AspNetCore.Http;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <inheritdoc cref="IPeriodSelection" />
public sealed class PeriodSelection : IPeriodSelection
{
    /// <summary>
    /// Cookie name. Prefixed like the other cookies in the app so they group together in
    /// a browser's storage list.
    /// </summary>
    public const string CookieName = "StartCompass.Period";

    private readonly IPeriodService _periods;
    private readonly IHttpContextAccessor _http;
    private readonly IWebHostEnvironment _environment;

    public PeriodSelection(
        IPeriodService periods,
        IHttpContextAccessor http,
        IWebHostEnvironment environment)
    {
        _periods = periods;
        _http = http;
        _environment = environment;
    }

    /// <inheritdoc />
    public async Task<SurveyRound?> ResolveAsync(
        int? requestedRoundId,
        CancellationToken cancellationToken = default)
    {
        var rounds = await _periods.GetAllAsync(cancellationToken);

        if (rounds.Count == 0)
        {
            return null;
        }

        // 1. A period named in the URL wins. A shared link has to mean what it says, and
        //    following one becomes the new remembered choice.
        if (requestedRoundId is { } requested)
        {
            var asked = rounds.FirstOrDefault(r => r.Id == requested);
            if (asked is not null)
            {
                Remember(asked.Id);
                return asked;
            }
        }

        // 2. What was remembered last time -- but only if it still exists. A period that has
        //    since been deleted must not turn into a 404 on a page nobody asked for.
        if (TryReadRemembered() is { } rememberedId)
        {
            var remembered = rounds.FirstOrDefault(r => r.Id == rememberedId);
            if (remembered is not null)
            {
                return remembered;
            }

            Forget();
        }

        // 3. The current period: the open one closing furthest out, otherwise the newest.
        return await _periods.GetCurrentAsync(cancellationToken);
    }

    private int? TryReadRemembered()
    {
        var raw = _http.HttpContext?.Request.Cookies[CookieName];

        return int.TryParse(raw, out var id) ? id : null;
    }

    private void Remember(int roundId)
    {
        var response = _http.HttpContext?.Response;
        if (response is null || response.HasStarted)
        {
            return;
        }

        response.Cookies.Append(CookieName, roundId.ToString(), BuildCookieOptions());
    }

    private void Forget()
    {
        var response = _http.HttpContext?.Response;
        if (response is null || response.HasStarted)
        {
            return;
        }

        response.Cookies.Delete(CookieName, BuildCookieOptions());
    }

    /// <summary>
    /// Hardened the same way as the session and antiforgery cookies. A period id is not
    /// sensitive, but there is no reason for this one to be the loose one.
    /// </summary>
    private CookieOptions BuildCookieOptions() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = !_environment.IsDevelopment(),
        Expires = DateTimeOffset.UtcNow.AddDays(30),
        IsEssential = true // it is a UI preference the user set, not tracking
    };
}
