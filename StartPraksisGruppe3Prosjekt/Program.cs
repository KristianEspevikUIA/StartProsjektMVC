using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Database. SQLite i utvikling; filen ligger i prosjektmappa og er git-ignorert.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=speilet.db";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

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

// ---------------------------------------------------------------------------
// Ressursbasert autorisasjon.
//
// Rolle alene avgjør ingenting her. Begge policyene vurderer en konkret ressurs og
// kalles fra controllerne med IAuthorizationService.AuthorizeAsync(User, ressurs, policy).
// Handlerne er scoped fordi de slår opp i databasen.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IAuthorizationHandler, CanViewPlayerHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanViewTeamAggregateHandler>();

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
});

// ---------------------------------------------------------------------------
// Tjenester.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IConsentService, ConsentService>();
builder.Services.AddScoped<IScoringService, ScoringService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // Identity UI (innlogging, passord) ligger som Razor Pages

var app = builder.Build();

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
