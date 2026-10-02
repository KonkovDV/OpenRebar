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
    builder.AppendLine("Table 6.14 of Amendment 1, from the TECHNO NICOL copy accessed 2026-10-02: Rs is A240 210, A400 340, A500 435, B500 415 MPa. Rsc is A240 210, A400 350, A500 435 (400), B500 415 (380) MPa. Parenthetical Rsc is for short-term load only. A second public transcription prints A400 Rsc as 340. The official order text was not opened. Anchorage uses Rs.");
    builder.AppendLine();
    builder.AppendLine("The clear distance between adjacent laps in the amendment to clause 10.3.30 is not enforced. That amendment text is not in this repository.");
    builder.AppendLine();
    builder.AppendLine("Additional bars extend past a zone boundary by the calculated anchorage length, and only inside the working area. The SP 63 clause on curtailment past the theoretical cutoff is not in this repository, so no distance beyond that anchorage length is added.");
    builder.AppendLine();
    builder.AppendLine("Bent ends use the mandrel diameter from clause 10.3.33. The cut length adds the centerline arc and does not add a straight tail, because that tail is not in the clause. Shape codes stay internal: 00, H, L, and U. GOST 21.501 does not number bar shapes.");
    builder.AppendLine();
    return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
  }
}
