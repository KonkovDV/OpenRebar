using OpenRebar.Domain.Exceptions;
using OpenRebar.Infrastructure.Profiles;

namespace OpenRebar.Cli;

/// <summary>
/// profile init, profile validate, and profile diff.
/// init writes the generic defaults. It does not ask questions.
/// </summary>
public static class ProfileCommands
{
  public static int Run(string[] args)
  {
    if (args.Length < 2)
    {
      PrintUsage();
      return 1;
    }

    try
    {
      return args[1].ToLowerInvariant() switch
      {
        "init" => Init(args),
        "validate" => Validate(args),
        "diff" => Diff(args),
        _ => Unknown(args[1])
      };
    }
    catch (CompanyProfileLoadException ex)
    {
      Console.Error.WriteLine(ex.Message);
      return 1;
    }
  }

  private static int Init(string[] args)
  {
    string? output = Get(args, "--out") ?? Get(args, "--output");
    if (string.IsNullOrWhiteSpace(output))
    {
      Console.Error.WriteLine("profile init needs --out <file>.");
      return 1;
    }

    CompanyProfileLoader.WriteInit(output, Get(args, "--id"));
    Console.WriteLine($"Wrote {output} from generic defaults.");
    return 0;
  }

  private static int Validate(string[] args)
  {
    string? path = args.Skip(2).FirstOrDefault(arg => !arg.StartsWith('-'));
    if (string.IsNullOrWhiteSpace(path))
    {
      Console.Error.WriteLine("profile validate needs a profile path.");
      return 1;
    }

    var loaded = CompanyProfileLoader.Load(path);
    Console.WriteLine($"{loaded.Profile.Id} {loaded.Profile.Version} ok");
    return 0;
  }

  private static int Diff(string[] args)
  {
    var files = args.Skip(2).Where(arg => !arg.StartsWith('-')).Take(2).ToArray();
    if (files.Length != 2)
    {
      Console.Error.WriteLine("profile diff needs two profile paths.");
      return 1;
    }

    foreach (string line in CompanyProfileLoader.Diff(files[0], files[1]))
      Console.WriteLine(line);
    return 0;
  }

  private static int Unknown(string command)
  {
    Console.Error.WriteLine($"Unknown profile command '{command}'.");
    PrintUsage();
    return 1;
  }

  private static void PrintUsage()
  {
    Console.WriteLine("""
      profile init --out <file> [--id <id>]
      profile validate <file>
      profile diff <left.json> <right.json>
      """);
  }

  private static string? Get(string[] args, string name)
  {
    for (int i = 0; i < args.Length - 1; i++)
    {
      if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
        return args[i + 1];
    }

    return null;
  }
}
