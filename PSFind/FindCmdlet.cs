using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Management.Automation;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using System.Text;
using RegExpr = System.Text.RegularExpressions;

namespace PSFind;

[Cmdlet(VerbsCommon.Find, "Files", DefaultParameterSetName = "default")]
[OutputType(typeof(string))]
[Alias("find")]
public class FindCmdlet : Cmdlet
{
    [Parameter(Mandatory = true, Position = 0, HelpMessage = "Name of the file to search for. Supports the glob pattern.")]
    public string Name;

    [Parameter(ParameterSetName = "regex", HelpMessage = "If specified, considers the name as a regex pattern")]
    public SwitchParameter Regex;

    [Parameter(HelpMessage = "If specified, searches for folders instead of files")]
    public SwitchParameter Folders;

    [Parameter(HelpMessage = "If specified, restricts the search to the volume with the given letter.")]
    [ArgumentCompleter(typeof(DriveArgumentCompleter))]
    public char Volume;

    [Parameter(ParameterSetName = "text", HelpMessage = "If specified, performs a fuzzy search using the Levenshtein distance, returning all files where the distance between the file name and the searched name is less or equal than the given max distance.")]
    public byte Distance;

    [Parameter(HelpMessage = "If specified, search statistics are not shown at the end of the operation. Specify this parameter if you want to pipe results to another command.")]
    public SwitchParameter NoStats;

    char[] _drives;
    bool _gotPrivileges;

    protected override void BeginProcessing()
    {
        if (IsAdmin())
        {
            _gotPrivileges = true;
            _drives = Volume == '\0' ? GetValidDrives().ToArray() : GetValidDrives().Where(d => d == Volume).ToArray();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("This program requires administrator privileges to run");
        }
    }

    protected override void ProcessRecord()
    {
        if (!_gotPrivileges)
        {
            return;
        }

        uint searchedRecords = 0;
        int found = 0;
        Predicate<string> match = CreateMatchPredicate();
        long startTimestamp = Stopwatch.GetTimestamp();

        Parallel.ForEach(_drives, drive =>
        {
            using MftSearcher searcher = new(drive);

            foreach (string result in searcher.Search(match, Folders))
            {
                lock (_drives)
                {
                    if (Distance == 0)
                    {
                        WritePattern(result, Name, Regex);
                    }
                    else
                    {
                        WriteName(result, Name);
                    }
                }

                Interlocked.Increment(ref found);
            }

            Interlocked.Add(ref searchedRecords, searcher.SearchedRecords);
        });

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

        if (!NoStats)
        {
            Console.WriteLine($"\nSearched {searchedRecords} records on {_drives.Length} volume{(_drives.Length != 1 ? "s" : "")} in {elapsed.TotalSeconds:0.##}s." +
                              $" Found {found} result{(found != 1 ? "s" : "")}");
        }
    }

    Predicate<string> CreateMatchPredicate()
    {
        Predicate<string> match;

        if (Distance > 0)
        {
            match = s => LevenshteinDistance.GetDistance(s, Name) <= Distance;
        }
        else if (Regex)
        {
            Regex regex = new(Name, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
            match = s => regex.IsMatch(s);
        }
        else
        {
            string pattern = $"^{RegExpr.Regex.Escape(Name).Replace(@"\*", ".*").Replace(@"\?", ".")}$";
            Regex regex = new(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
            match = s => regex.IsMatch(s);
        }

        return match;
    }

    static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    // Returns a list of all internal volumes in this machine that use the NTFS filesystem.
    static IEnumerable<char> GetValidDrives() => DriveInfo.GetDrives().Where(d => d is { IsReady: true, DriveFormat: "NTFS" }).Select(d => d.Name[0]);

    static void WritePattern(string path, string word, bool isRegex)
    {
        string fileName = Path.GetFileName(path);
        int index = path.IndexOf(fileName);
        Console.Write(path[..index]);

        // Build a bitmask indicating where a char-by-char match is present; those letters will be colored differently in the output
        bool[] mask = new bool[fileName.Length];

        // Generate a regex that finds all locations in the fileName where there's a char-by-char match, and marks those locations in the mask
        word = AddCaptureGroups(word);

        // If word is not a regex, it could contain some special regex characters that need to be escaped
        if (!isRegex)
        {
            word = RegExpr.Regex.Escape(word);
        }

        // Convert AddCaptureGroups symbols to parenthesis (they can't be returned directly by that method because they would get escaped by Regex.Escape)
        string r = $"{word.Replace("<", "(").Replace(">", ")").Replace(@"\*", ".*").Replace(@"\?", ".")}";
        var regex = new Regex(r, RegexOptions.IgnoreCase);

        // Skip the first capture group since it contains the entire file name
        foreach (var group in regex.Match(fileName).Groups.Values.Skip(1))
        {
            for (int i = group.Index; i < group.Index + group.Length; i++)
            {
                mask[i] = true;
            }
        }

        for (int i = 0; i < fileName.Length; i++)
        {
            Console.ForegroundColor = mask[i] ? ConsoleColor.Blue : ConsoleColor.Yellow;
            Console.Write(fileName[i]);
        }

        Console.ResetColor();
        Console.WriteLine();
    }

    static string AddCaptureGroups(string pattern)
    {
        int index = 0;

        return pattern.Aggregate(new StringBuilder(), (result, c) => result.Append(c switch
        {
            '*' or '?' when index++ == 0            => c + "<",
            '*' or '?' when index == pattern.Length => ">" + c, // Check should be with pattern.Length - 1, but index has been incremented by the previous condition
            '*' or '?'                              => ">" + c + "<",
            _ when index++ == 0                     => "<" + c,
            _ when index == pattern.Length          => c + ">", // Check should be with pattern.Length - 1, but index has been incremented by the previous condition
            _                                       => c
        })).ToString();
    }

    static void WriteName(string path, string word)
    {
        string fileName = Path.GetFileName(path);
        int index = path.IndexOf(fileName);
        Console.Write(path[..index]);

        for (int i = 0; i < fileName.Length; i++)
        {
            Console.ForegroundColor = fileName[i] == word[i] ? ConsoleColor.Blue : ConsoleColor.Yellow;
            Console.Write(fileName[i]);
        }

        Console.ResetColor();
        Console.WriteLine();
    }


    class DriveArgumentCompleter : IArgumentCompleter
    {
        IEnumerable<CompletionResult> IArgumentCompleter.CompleteArgument(string commandName, string parameterName, string wordToComplete, System.Management.Automation.Language.CommandAst commandAst, System.Collections.IDictionary fakeBoundParameters)
        {
            return GetValidDrives().Select(d => new CompletionResult(d.ToString()));
        }
    }
}
