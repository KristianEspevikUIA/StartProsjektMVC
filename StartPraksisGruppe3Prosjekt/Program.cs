using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Security;
using StartPraksisGruppe3Prosjekt.Services;

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

// Migrering og oppdiktede demodata. Kjører bare i utvikling — se SeedData.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await SeedData.InitializeAsync(scope.ServiceProvider);
}

app.Run();
