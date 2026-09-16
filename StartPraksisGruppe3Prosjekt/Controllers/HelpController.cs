using Microsoft.AspNetCore.Mvc;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// How to read the numbers: what a score, a difference and a follow-up flag mean, and why a
/// number is sometimes missing.
///
/// The explanations used to sit on the pages themselves, a paragraph over every table and
/// chart, and the coaches said the pages were mostly text. They live here now, and each page
/// links to its own section.
///
/// Not on <see cref="HomeController"/>: that one is [AllowAnonymous], and nothing but the
/// front page, the privacy notice and the error page is meant to be. There is nothing
/// secret here, but it describes pages only a signed-in user can reach, so it asks for the
/// same sign-in -- the fallback policy in Program.cs, without anything to remember.
/// </summary>
public class HelpController : Controller
{
    public IActionResult Index() => View();
}
