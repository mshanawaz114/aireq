// JobContentHash — stable content fingerprint for cross-source dedup.
//
// Per memory.md §7: hash of (company, title, location, jd_first_500_chars).
// Normalization (lowercase, trim, collapse whitespace) makes "Sr. Engineer "
// and "sr.  engineer" from two different sources collide as intended.
//
// Refs: AIRMVP1-203

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Aireq.Worker.Jobs;

public static partial class JobContentHash
{
    private const int DescriptionPrefix = 500;

    public static string Compute(string company, string title, string? location, string? description)
    {
        var desc = Normalize(description);
        if (desc.Length > DescriptionPrefix) desc = desc[..DescriptionPrefix];

        var key = $"{Normalize(company)}|{Normalize(title)}|{Normalize(location)}|{desc}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Normalize(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? ""
            : Whitespace().Replace(s.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
