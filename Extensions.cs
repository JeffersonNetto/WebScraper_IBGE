using System.Globalization;
using System.Text;

namespace WebScraper_IBGE
{
    public static class Extensions
    {
        public static string RemoverAcentuacao(this string value)
        {
            return new string(value.Normalize(NormalizationForm.FormD)
                                         .Where(ch => char.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                                         .ToArray());
        }
    }
}
