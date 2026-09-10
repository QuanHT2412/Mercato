using System.Globalization;
using System.Text;

namespace Mercato.Catalog.Domain.Categories;

public static class Slug
{
    // "Điện thoại & Phụ kiện" -> "dien-thoai-phu-kien". Vietnamese diacritics are
    // decomposed and dropped; đ/Đ have no decomposition so they are mapped by hand.
    public static string From(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingDash = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(c);
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return builder.ToString();
    }
}
