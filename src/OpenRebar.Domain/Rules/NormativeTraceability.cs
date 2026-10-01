using System.Text;

namespace OpenRebar.Domain.Rules;

/// <summary>
/// Renders the traceability note from the embedded profile. The markdown file is a copy of this text.
/// </summary>
public static class NormativeTraceability
{
  public static string Render()
  {
    var profile = NormativeProfiles.Sp63_2018;
    var builder = new StringBuilder();
    builder.AppendLine("# Normative traceability");
    builder.AppendLine();
    builder.AppendLine($"Generated from `{profile.TablesVersion}.json`. Edit the table file, then regenerate this note.");
    builder.AppendLine();
    builder.AppendLine("| Clause | Method | Test | Quote |");
    builder.AppendLine("| --- | --- | --- | --- |");
    foreach (var row in profile.Traceability)
    {
      builder.Append("| ").Append(row.ClauseId);
      builder.Append(" | ").Append(row.Method);
      builder.Append(" | ").Append(row.Test);
      builder.Append(" | ").Append(row.SourceQuote.Replace("|", "/", StringComparison.Ordinal));
      builder.AppendLine(" |");
    }

    builder.AppendLine();
    builder.AppendLine("Design strengths of A400 (355 MPa) and B500 (435 MPa) are unchanged. Table 6.14 was not available as a citable extract.");
    builder.AppendLine();
    builder.AppendLine("The clear distance between adjacent laps in the amendment to clause 10.3.30 is not enforced. That amendment text is not in this repository.");
    builder.AppendLine();
    return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
  }
}
