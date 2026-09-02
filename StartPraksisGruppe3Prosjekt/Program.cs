using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Security;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

var builder = WebApplication.CreateBuilder(args);

// Serverhodet forteller ellers hvilken webserver og hvilken versjon som kjører.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// ---------------------------------------------------------------------------
// Database. SQLite i utvikling; filen ligger i prosjektmappa og er git-ignorert.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=speilet.db";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ---------------------------------------------------------------------------
// Identity emed de fire rollene: Player, Coach, Guardian, Admin.
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

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
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
builder.Services.AddScoped<IScoringService, ScoringService>();

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
builder.Services.AddScoped<ISurveyAssignmentService, SurveyAssignmentService>();
builder.Services.AddScoped<IFiveCAnalysisService, FiveCAnalysisService>();

builder.Services.Configure<SupabaseOptions>(
    builder.Configuration.GetSection(SupabaseOptions.SectionName));

// Hvor 5C-svarene lagres.
//
// Standard er appens egen database — som etter overgangen til Npgsql ER Supabase. Det var
// tidligere et minnelager, fordi appen kjørte på lokal SQLite og Supabase var noe man måtte
// nå over HTTP. Det stemmer ikke lenger, og et minnelager betydde at hvert svar forsvant
// ved omstart.
//
// De to andre er unntak, ikke alternativer:
//   FiveC:Supabase:Url + ApiKey  — et GENUINT separat Supabase-prosjekt, nådd over PostgREST.
//   FiveC:Store = "InMemory"     — kjøring uten å skrive noe, f.eks. i en demo.
var supabaseOptions = builder.Configuration
    .GetSection(SupabaseOptions.SectionName)
    .Get<SupabaseOptions>() ?? new SupabaseOptions();

var storeSetting = builder.Configuration["FiveC:Store"];

if (string.Equals(storeSetting, "InMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ISurveySubmissionStore, InMemorySurveySubmissionStore>();
}
else if (supabaseOptions.IsConfigured)
{
    builder.Services.AddHttpClient<ISurveySubmissionStore, SupabaseSurveySubmissionStore>();
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
// Databasen: feil tidlig og forståelig.
//
// Tilkoblingsstrengen i appsettings.json peker på Supabase, men uten passord —
// passordet er en hemmelighet og skal ikke ligge i repoet. Uten det kaster Npgsql
// en stacktrace som ikke sier hva man skal gjøre. Denne sjekken gjør det.
//
// Sjekken ligger etter builder.Build() med vilje: `dotnet ef` stopper appen der,
// så migrasjoner kan fortsatt genereres på en maskin uten passordet.
// ---------------------------------------------------------------------------
if (string.IsNullOrEmpty(new NpgsqlConnectionStringBuilder(connectionString).Password))
{
    app.Logger.LogCritical(
        "Databasepassordet mangler. Tilkoblingsstrengen har ingen Password, og Postgres " +
        "krever et. Hent «Database password» i Supabase (Project Settings -> Database) og " +
        "legg det i user-secrets — ikke i appsettings.json:\n\n" +
        "    dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" " +
        "\"<hele strengen fra appsettings.json med ;Password=...>\" " +
        "--project StartPraksisGruppe3Prosjekt\n\n" +
        "Merk at «publishable key» / anon-nøkkelen IKKE er databasepassordet — den gjelder " +
        "REST-API-et, ikke en direkte Postgres-tilkobling.");

    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection mangler Password. Se meldingen over.");
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

// Migrering og oppdiktede demodata. Kjører bare i utvikling — se SeedData.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();

    try
    {
        await SeedData.InitializeAsync(scope.ServiceProvider);
    }
    catch (NpgsqlException ex)
    {
        // Passordet finnes, men databasen svarer ikke som forventet. De to vanlige
        // årsakene er verdt å nevne ved navn, ellers blir feilsøkingen gjetting.
        app.Logger.LogCritical(
            ex,
            "Kom ikke gjennom migrering/seeding mot Postgres. Vanlige årsaker:\n" +
            "  * Feil passord, eller feil port. Bruk session-pooleren (5432); " +
            "transaction-pooleren (6543) fungerer ikke med EF-migrasjoner.\n" +
            "  * Databasen har allerede tabeller fra et tidligere forsøk. Migrasjonen ble " +
            "generert på nytt for Postgres 2026-08-26, så et skjema laget før det stemmer " +
            "ikke med __EFMigrationsHistory. Tøm public-skjemaet i Supabase og kjør igjen.");

        throw;
    }
}

// Si tydelig hvor 5C-svarene havner. «Lagret det seg egentlig?» skal ikke være noe
// man må gjette på.
using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<ISurveySubmissionStore>();

    app.Logger.LogInformation("5C submissions are stored in: {Store}.", store.Description);

    if (store is InMemorySurveySubmissionStore && !app.Environment.IsDevelopment())
    {
        app.Logger.LogWarning(
            "Supabase is not configured ({Section}:Url / :ApiKey). Submitted 5C answers " +
            "are kept in memory only and are lost when the application stops.",
            SupabaseOptions.SectionName);
    }
}

app.Run();

/// <summary>
/// Gjør den genererte Program-klassen synlig for testprosjektet.
///
/// Med top-level statements er den internal, og WebApplicationFactory&lt;Program&gt; kommer
/// ikke til den. Dette er hele grunnen til at linjen står her — den endrer ingen oppførsel.
/// </summary>
public partial class Program { }
