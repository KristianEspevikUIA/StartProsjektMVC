using System.Security.Cryptography;

namespace StartPraksisGruppe3Prosjekt.Security;

/// <summary>
/// Kontoene: det ene passordet som aldri skal virke utenfor utvikling, og det midlertidige
/// passordet en administrator gir ut.
///
/// Appen har ingen e-post. Et passord kan derfor ikke tilbakestilles med en lenke; det gis ut av
/// en administrator, vises én gang, og må byttes av eieren ved første innlogging. Det siste er
/// hele poenget: passordet administratoren har sett, er ikke lenger passordet til kontoen.
/// </summary>
public static class AccountRules
{
    /// <summary>
    /// Passordet seedingen gir demokontoene i Development. DET STÅR I ET OFFENTLIG REPO, og i
    /// historikken til det, og er derfor ikke en hemmelighet og blir det aldri igjen. Utenfor
    /// Development avvises det ved innlogging (<c>LoginModel</c>) og som nytt passord
    /// (<see cref="PublishedPasswordValidator"/>), så en konto som fortsatt har det -- fordi
    /// produksjonsbasen ble kopiert fra utviklingsbasen -- ikke kan åpnes med det.
    /// </summary>
    public const string PublishedDevPassword = "Dev!passord1";

    /// <summary>
    /// Kravet på en konto som har fått et midlertidig passord: den slipper ingen steder før
    /// passordet er byttet. Ligger som et claim i AspNetUserClaims, så det følger kontoen og
    /// ikke nettleseren. Se <see cref="MustChangePasswordExtensions"/>.
    /// </summary>
    public const string MustChangePasswordClaim = "startcompass:must-change-password";

    // Uten I, O, l, o, 0 og 1: passordet leses opp eller skrives av fra en skjerm.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";

    /// <summary>
    /// Et midlertidig passord, f.eks. «Kq7m-Xp4w-Nd9t-Rz2h»: fire grupper med stor bokstav,
    /// liten, siffer og liten, så det alltid oppfyller passordkravene i Program.cs. Rundt 67
    /// biter -- rikelig for et passord som må byttes ved første innlogging, på en konto som
    /// låses etter fem feil.
    /// </summary>
    public static string NewTemporaryPassword()
    {
        var groups = new string[4];

        for (var index = 0; index < groups.Length; index++)
        {
            groups[index] = string.Concat(Pick(Upper), Pick(Lower), Pick(Digits), Pick(Lower));
        }

        return string.Join('-', groups);

        static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
    }
}
