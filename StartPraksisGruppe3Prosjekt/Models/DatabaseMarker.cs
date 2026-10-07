using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// Hva databasen er til: utvikling eller drift. Én rad, skrevet av appen første gang den
/// starter mot en tom database, og aldri endret etterpå.
///
/// Markeringen ligger i databasen og ikke i konfigurasjonen fordi det er databasen som skal
/// vernes. En utviklingsbase har demokontoer med et passord som står i kildekoden, og
/// Development migrerer og seeder ved oppstart; ingen av delene skal noen gang møte ekte
/// spillerdata. En liste over tillatte verter kan ikke skille de to: appen og databasen kan
/// stå på samme server, og da er verten localhost i begge miljøer.
///
/// Se <see cref="Data.DatabaseGuard"/> for hvem som nektes hva.
/// </summary>
public class DatabaseMarker
{
    /// <summary>Den ene raden. En CHECK i databasen holder tabellen på én.</summary>
    public const int SingleRowId = 1;

    public const string Development = "Development";
    public const string Production = "Production";

    public int Id { get; set; } = SingleRowId;

    /// <summary><see cref="Development"/> eller <see cref="Production"/>.</summary>
    [Required]
    [StringLength(20)]
    public string Environment { get; set; } = string.Empty;

    public DateTimeOffset MarkedAt { get; set; }
}
