namespace OpenRebar.TestCorpus;

public static class Program
{
  public static int Main(string[] args)
  {
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
      Console.WriteLine("Usage: OpenRebar.TestCorpus generate <directory> [scenarioId]");
      return args.Length == 0 ? 1 : 0;
    }

    if (args[0] != "generate" || args.Length < 2)
    {
      Console.Error.WriteLine("Usage: OpenRebar.TestCorpus generate <directory> [scenarioId]");
      return 1;
    }

    string directory = args[1];
    if (args.Length >= 3)
      CorpusGenerator.Write(args[2], directory);
    else
      CorpusGenerator.WriteAll(directory);

    Console.WriteLine(Path.GetFullPath(directory));
    return 0;
  }
}
