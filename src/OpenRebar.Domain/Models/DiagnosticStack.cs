using System.Security.Cryptography;
using System.Text;

namespace OpenRebar.Domain.Models;

/// <summary>Turns a diagnostic stack into the hash stored on a report.</summary>
public static class DiagnosticStack
{
  public static string? Sha256(string? stack)
  {
    if (string.IsNullOrEmpty(stack))
      return null;

    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(stack));
    return Convert.ToHexString(hash).ToLowerInvariant();
  }
}
