using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Net;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Commands;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Security;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using StartPraksisGruppe3Prosjekt.Services.Identity;
using StartPraksisGruppe3Prosjekt.Services.Succession;

var builder = WebApplication.CreateBuilder(args);

// Serverhodet forteller ellers hvilken webserver og hvilken versjon som kjører.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// ---------------------------------------------------------------------------
// Database: PostgreSQL. Tilkoblingsstrengen står ikke i appsettings.json. I utvikling peker
// appsettings.Development.json på en lokal database, og passordet ligger i user-secrets
// (Database:Password). I drift kommer hele strengen fra miljøvariabelen
// ConnectionStrings__DefaultConnection. Mangler noe, stopper appen med en forklaring
// lenger ned. Se DatabaseConnection og docs/database.md.
//
// Tabeller og kolonner får små bokstaver og understrek (players.birth_date), så ingenting
// må skrives i anførselstegn i SQL.
// ---------------------------------------------------------------------------
var connectionString = DatabaseConnection.Resolve(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention());

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ---------------------------------------------------------------------------
// Identity med de fire rollene: Player, Coach, Guardian, Admin.
// ---------------------------------------------------------------------------
builder.Services
    .AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;

        // Systemet håndterer opplysninger om mindreårige. Passordkravene er strengere
        // enn standardoppsettet, og kontoer låses ved gjentatte forsøk.
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// Rolleendringer ligger i cookien til den valideres på nytt. Standard er 30 minutter;
// her skal en trener som mister et lag, eller en konto som låses, miste tilgangen fort.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});

// ---------------------------------------------------------------------------
// Cookies. Sesjonscookien er nøkkelen til alt en bruker får se, og behandles deretter.
//
// Utenfor utvikling er kravet https, uten unntak. I utvikling følger cookiene
// forespørselen: antiforgery-systemet kaster en exception hvis det er satt til
// Always og forespørselen kommer over http, og launchSettings har fortsatt en
// http-profil. Dev-databasen inneholder bare oppdiktede data.
// ---------------------------------------------------------------------------
var cookieSecurePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Speilet.Auth";
    options.Cookie.HttpOnly = true;                 // ikke lesbar fra JavaScript
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;  // følger ikke med fra andre nettsteder

    // Delte maskiner: en glemt fane skal ikke være innlogget i morgen.
    options.ExpireTimeSpan = TimeSpan.FromHours(2);
    options.SlidingExpiration = true;

    // Et avslag svares med 403 på adressen det ble spurt etter, ikke med en omdirigering
    // til Identity-pakkens /Account/AccessDenied. Den siden er pakkens egen og ligner ikke
    // resten av appen; statuskodesiden lenger ned gjør 403 til en side som gjør det.
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Antiforgery-cookien herdes på samme måte.
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "Speilet.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.HeaderName = "RequestVerificationToken";
});

// ---------------------------------------------------------------------------
// Data Protection-nøklene krypterer innloggingscookien og antiforgery-tokenene. Uten et
// fast sted å ligge lages de på nytt når appen starter i en ny container eller under en
// annen bruker -- og da er alle logget ut, og hvert åpent skjema avvises. I drift ligger
// de derfor i mappa DataProtection:KeysPath, som er påkrevd der (sjekkes lenger ned).
//
// Navnet holder nøklene gyldige selv om appen flyttes til en annen mappe; standarden er
// å utlede det av stien appen ligger i.
// ---------------------------------------------------------------------------
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("StartCompass");

if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

// Forwarded headers are trusted only from explicitly configured proxy addresses.
// This must be configured before HTTPS redirection and rate limiting, otherwise the
// application sees the proxy as the client and can accept spoofed scheme/IP headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    var configuredProxies = builder.Configuration
        .GetSection("ForwardedHeaders:KnownProxies")
        .Get<string[]>() ?? Array.Empty<string>();

    foreach (var address in configuredProxies)
    {
        if (IPAddress.TryParse(address, out var ipAddress))
        {
            options.KnownProxies.Add(ipAddress);
        }
    }
});

// ---------------------------------------------------------------------------
// Ressursbasert autorisasjon.
//
// Rolle alene avgjør ingenting her. Begge policyene vurderer en konkret ressurs og
// kalles fra controllerne med IAuthorizationService.AuthorizeAsync(User, ressurs, policy).
// Handlerne er scoped fordi de slår opp i databasen.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IAuthorizationHandler, CanViewPlayerHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanViewTeamAggregateHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanViewTeamHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.CanViewPlayer, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new CanViewPlayerRequirement());
    });

    options.AddPolicy(Policies.CanViewTeamAggregate, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new CanViewTeamAggregateRequirement());
    });

    options.AddPolicy(Policies.CanViewTeam, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new CanViewTeamRequirement());
    });

    // Nekt som standard: en ny controller eller action uten [Authorize] krever
    // likevel innlogging. Glemt attributt skal gi en innloggingsside, ikke en åpen
    // side med spillerdata. Det som faktisk skal være åpent, merkes [AllowAnonymous]
    // — se HomeController.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ---------------------------------------------------------------------------
// Sikkerhetshoder (CSP med nonce) og rate limiting. Se Security/.
// ---------------------------------------------------------------------------
builder.Services.AddSecurityHeaders(builder.Configuration, builder.Environment);
builder.Services.AddSpeiletRateLimiting();

// ---------------------------------------------------------------------------
// Tjenester.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IConsentService, ConsentService>();

// Revisjonsloggen. Trenere trenger ikke lenger samtykke for å åpne en enkeltspiller, og
// denne loggen er det som står igjen i stedet: hvem så på hvem, når. Slutter den å skrives,
// står regelen i CanViewPlayerHandler uten motvekt.
builder.Services.AddScoped<IPlayerAccessLog, PlayerAccessLog>();

// Trenerens frigivelse av egne svar til spilleren — samtalen i punkt 6.
builder.Services.AddScoped<IFeedbackReleaseService, FeedbackReleaseService>();

// Måleperioder. Både admin-siden og seedingen går gjennom denne, slik at reglene for
// hva som er en brukbar periode bor ett sted.
builder.Services.AddScoped<IPeriodService, PeriodService>();

// Valgt periode huskes i en cookie, slik at valget overlever et menyklikk. Uten dette
// måtte roundId tres gjennom hver eneste lenke i appen.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPeriodSelection, PeriodSelection>();

// Velkomsten når en spiller logger inn: fornavn og bilde, lagt inn av admin. Det eneste stedet
// et navn lagres om en spiller -- se PlayerPersonalDetails og docs/player-welcome.md.
builder.Services.AddScoped<IPlayerWelcomeService, PlayerWelcomeService>();

// Spiller- og foresattsiden. Bygger begge, slik at avgjørelsen om hva som skal skjules
// før treneren har frigitt, tas ett sted og ikke i to views.
builder.Services.AddScoped<IFiveCFeedbackBuilder, FiveCFeedbackBuilder>();

// ---------------------------------------------------------------------------
// 5C-spørreskjemaet.
//
// Spørsmålene er innhold, ikke tilstand: de leses én gang fra
// Data/Questions/five-c-questions.json og ligger i minnet. En feil i fila stopper
// oppstarten med en melding som sier hva som er galt — den skal ikke dukke opp som
// et halvtomt skjema midt i en runde.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IQuestionCatalog, QuestionCatalog>();

// Rekkefølgen utsagnene vises i. Singleton fordi den ikke har noen tilstand: den samme
// spilleren og den samme perioden gir den samme rekkefølgen hver gang, og den regnes ut
// fra de to ID-ene i stedet for å lagres. Se IQuestionOrder for hvorfor den er stokket.
builder.Services.AddSingleton<IQuestionOrder, QuestionOrder>();

builder.Services.AddScoped<ISurveyAssignmentService, SurveyAssignmentService>();
builder.Services.AddScoped<IFiveCAnalysisService, FiveCAnalysisService>();

// ---------------------------------------------------------------------------
// Identity Benchmarking: lagenes kamptall mot IK Starts Gold Standard.
//
// Samme ordning som spørsmålskatalogen: Data/Identity/gold-standard.json leses én gang og
// valideres ved oppstart. Kampdataene leses fra IdentityBenchmark:MatchDataPath, som er
// git-ignorert fordi rapportene navngir spillere. Mangler en fil, sier siden det; er fila der
// men ikke henger sammen, stopper oppstarten. Se docs/identity-benchmarking.md.
// ---------------------------------------------------------------------------
builder.Services.Configure<IdentityBenchmarkOptions>(
    builder.Configuration.GetSection(IdentityBenchmarkOptions.SectionName));
builder.Services.AddSingleton<IIdentityCatalog, IdentityCatalog>();
builder.Services.AddSingleton<IIdentityBenchmarkBuilder, IdentityBenchmarkBuilder>();

// ---------------------------------------------------------------------------
// Succession planning: trenernes vurderinger per spiller hver åttende uke, side om side.
//
// Samme ordning som de to over: Data/Succession/succession-planning.json (posisjoner,
// formasjoner, kategorier, terskler) leses én gang og valideres ved oppstart. Vurderingene
// ligger i databasen. Se docs/succession-planning.md.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<ISuccessionCatalog, SuccessionCatalog>();
builder.Services.AddScoped<ISuccessionPlanningService, SuccessionPlanningService>();

// Hvor 5C-svarene lagres: i appens egen database. Unntaket er FiveC:Store = "InMemory", for
// en kjøring som ikke skal skrive noe, f.eks. en demo. Da forsvinner svarene ved omstart.
if (string.Equals(builder.Configuration["FiveC:Store"], "InMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ISurveySubmissionStore, InMemorySurveySubmissionStore>();
}
else
{
    builder.Services.AddScoped<ISurveySubmissionStore, EfSurveySubmissionStore>();
}

builder.Services.AddControllersWithViews(options =>
{
    // Antiforgery på alle POST/PUT/DELETE uten at noen må huske attributtet.
    // Trenger du å slippe unna på én action, må det være et bevisst
    // [IgnoreAntiforgeryToken] som synes i kodegjennomgang.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddRazorPages(); // Identity UI (innlogging, passord) ligger som Razor Pages

var app = builder.Build();

// Først i pipelinen: da følger hodene med på alt, også statiske filer og feilsvar.
app.UseSecurityHeaders();

// Et tomt 4xx- eller 5xx-svar kjøres om igjen som /Home/Status/{kode}: en adresse som ikke
// finnes, eller en side kontoen ikke får åpne, blir en side med en vei videre i stedet for
// nettleserens egen feilside. Statuskoden beholdes.
//
// Ligger før UseClosedSelfRegistration, slik at 404-en derfra ser ut som enhver annen ukjent
// adresse. Svar som allerede har innhold røres ikke, så 429 fra rate limiteren og feilsiden
// fra UseExceptionHandler er som før.
app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");

// Selvregistrering er stengt — kontoer opprettes av klubben. Se Security/.
app.UseClosedSelfRegistration();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Etter UseRouting, slik at [EnableRateLimiting] på en action blir sett.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

// ---------------------------------------------------------------------------
// Oppsettet: feil tidlig og forståelig.
//
// Uten passord kaster Npgsql en stacktrace som ikke sier hva man skal gjøre. Disse sjekkene
// gjør det. De ligger etter builder.Build() med vilje: `dotnet ef` stopper appen der, så
// migrasjoner kan fortsatt genereres på en maskin uten passord og uten database.
// ---------------------------------------------------------------------------
var setupProblems = DatabaseConnection.Problems(connectionString, app.Environment).ToList();

if (!app.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
    {
        setupProblems.Add(
            "DataProtection:KeysPath mangler. Utenfor Development må nøklene som krypterer " +
            "innloggingscookien ligge i en fast mappe, ellers logges alle ut når appen starter på " +
            "nytt. Sett miljøvariabelen DataProtection__KeysPath til en mappe bare appen kan lese.");
    }
    else
    {
        try
        {
            // Opprettes og prøveskrives nå. En mappe appen ikke får skrive i, skal stoppe
            // oppstarten -- ikke dukke opp som en feil første gang noen logger inn.
            Directory.CreateDirectory(dataProtectionKeysPath);

            var probe = Path.Combine(dataProtectionKeysPath, $".write-test-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            setupProblems.Add(
                $"Appen kan ikke skrive i DataProtection:KeysPath ({dataProtectionKeysPath}): {ex.Message}");
        }
    }
}

if (setupProblems.Count > 0)
{
    await RefuseToStartAsync(setupProblems);
    return;
}

// ---------------------------------------------------------------------------
// AllowedHosts. «*» tar imot hvilket som helst Host-hode, og et Host-hode fra utsiden
// havner rett i lenkene Identity sender ut — blant annet i en tilbakestillingslenke
// for passord. Standarden i appsettings.json er derfor bare lokale navn, og produksjon
// setter det faktiske vertsnavnet i miljøet. Står den likevel på «*», skal det høres.
// ---------------------------------------------------------------------------
var allowedHosts = app.Configuration["AllowedHosts"];

if (!app.Environment.IsDevelopment() && allowedHosts is null or "" or "*")
{
    app.Logger.LogWarning(
        "AllowedHosts er «{AllowedHosts}» utenfor utvikling. Appen svarer da på et hvilket " +
        "som helst Host-hode. Sett det faktiske vertsnavnet i miljøet, f.eks. " +
        "AllowedHosts=startcompass.example.no.",
        allowedHosts ?? "(ikke satt)");
}

// ---------------------------------------------------------------------------
// Engangskommandoer (create-admin, export-players, import-players). Se Commands/OneOffCommands.
//
// Ligger FØR oppstartssteget under med vilje. Eksporten skal kunne kjøres mot en database
// uten at den endres av at noen leste fra den, og importen skal ikke få demodata med på
// kjøpet. En kommando gjør det ene den er til for, og avslutter uten å starte webserveren.
// ---------------------------------------------------------------------------
if (await OneOffCommands.TryRunAsync(app, args) is { } exitCode)
{
    // Loggen skrives fra en kø. Uten dette kan prosessen avslutte før de siste linjene --
    // de som sier hvor mange spillere som ble flyttet -- er kommet ut.
    await app.DisposeAsync();

    Environment.ExitCode = exitCode;
    return;
}

// ---------------------------------------------------------------------------
// Databasen ved oppstart. Rekkefølgen er hele poenget:
//
//   1. Vernet. Er databasen markert for et annet miljø, eller har den tabeller uten å være
//      markert, stopper appen FØR noe er migrert eller skrevet. Se DatabaseGuard.
//   2. Skjemaet. I utvikling migrerer appen selv. I drift gjør den det ikke: appen kobler
//      til med en rolle som ikke får endre skjemaet, og migrasjonene kjøres av
//      databaseeieren før en ny versjon startes. Mangler en migrasjon, stopper appen.
//   3. Markeringen. En tom database får skrevet inn hvilket miljø den hører til.
//   4. Grunnoppsettet, i alle miljøer: rollene og lagene. Se BaseSetup.
//   5. Demodata, bare i utvikling. Se SeedData.
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();

    try
    {
        await DatabaseGuard.EnsureBelongsHereAsync(db, app.Environment);

        if (app.Environment.IsDevelopment())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await DatabaseGuard.EnsureMigratedAsync(db, app.Logger);
        }

        await DatabaseGuard.MarkIfNewAsync(db, app.Environment, app.Logger);

        await BaseSetup.EnsureAsync(services, app.Logger);

        if (app.Environment.IsDevelopment())
        {
            await SeedData.InitializeAsync(services);
        }
    }
    catch (DatabaseGuardException ex)
    {
        // Vernet har nektet. Det er et svar, ikke en feil: meldingen sier hva som er galt og
        // hva som skal gjøres, og en stacktrace under den ville bare gjemt den.
        await RefuseToStartAsync(new[] { ex.Message });
        return;
    }
    catch (NpgsqlException ex)
    {
        // Oppsettet er i orden, men databasen svarer ikke som forventet. De vanlige
        // årsakene er verdt å nevne ved navn, ellers blir feilsøkingen gjetting.
        app.Logger.LogCritical(
            ex,
            "Kom ikke gjennom oppstarten mot PostgreSQL. Vanlige årsaker:\n" +
            "  * Databasen kjører ikke, eller står på en annen port. I utvikling: «docker compose up -d».\n" +
            "  * Feil passord, eller databasen i tilkoblingsstrengen finnes ikke.\n" +
            "  * I drift: app-rollen mangler rettigheter på en tabell som kom med siste migrasjon. " +
            "scripts/database/02-grants.sql gir dem, og kjøres etter hver migrasjon; se docs/database.md.");

        throw;
    }
}

// Si tydelig hvor 5C-svarene havner. «Lagret det seg egentlig?» skal ikke være noe
// man må gjette på.
using (var scope = app.Services.CreateScope())
{
    // Tving fram spørsmålskatalogen her, ikke ved første forespørsel.
    //
    // QuestionCatalog validerer fila i konstruktøren, og hele poenget er at en feil i den
    // stopper oppstarten framfor å dukke opp som et halvtomt skjema midt i en runde. Den er
    // en singleton, så den bygges først når noen ber om den — og i drift, uten seeding, er
    // det ingen som gjør det før den første forespørselen.
    // Resultatet brukes ikke med vilje: det er selve oppslaget som er poenget, fordi
    // konstruktøren validerer og logger. Ikke fjern linjen fordi den ser ubrukt ut.
    _ = scope.ServiceProvider.GetRequiredService<IQuestionCatalog>();

    // Av samme grunn: Gold Standard og kampdataene valideres i konstruktøren.
    _ = scope.ServiceProvider.GetRequiredService<IIdentityCatalog>();

    // Og succession-fila: en formasjon med ti spillere skal stoppe oppstarten, ikke vise en
    // bane med et hull i.
    _ = scope.ServiceProvider.GetRequiredService<ISuccessionCatalog>();

    var store = scope.ServiceProvider.GetRequiredService<ISurveySubmissionStore>();

    app.Logger.LogInformation("5C submissions are stored in: {Store}.", store.Description);

    if (store is InMemorySurveySubmissionStore && !app.Environment.IsDevelopment())
    {
        app.Logger.LogWarning(
            "FiveC:Store is \"InMemory\" outside development. Submitted 5C answers are kept " +
            "in memory only and are lost when the application stops.");
    }
}

app.Run();

// Stopper oppstarten med en forklaring i stedet for en stacktrace. Meldingene er skrevet for
// den som står med en app som ikke starter, og skal være det siste som står i loggen.
async Task RefuseToStartAsync(IEnumerable<string> reasons)
{
    foreach (var reason in reasons)
    {
        app.Logger.LogCritical("{Reason}", reason);
    }

    app.Logger.LogCritical("Appen starter ikke. Se meldingen over, og docs/database.md.");

    // Loggen skrives fra en kø; uten dette kan prosessen avslutte før meldingene er ute.
    await app.DisposeAsync();

    Environment.ExitCode = 1;
}
