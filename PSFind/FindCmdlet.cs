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
using RegExpr = System.Text.RegularExpressions;

namespace PSFind;

[Cmdlet(VerbsCommon.Find, "File", DefaultParameterSetName = "default")]
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

    [Parameter(HelpMessage = "If specified, search statistics are shown at the end of the operation")]
    public SwitchParameter Stats;


    IEnumerable<char> _drives;
    bool _gotPrivileges;

    protected override void BeginProcessing()
    {
        if (IsAdmin())
        {
            _gotPrivileges = true;
            _drives = GetValidDrives();

            if (Volume != '\0')
            {
                _drives = _drives.Where(d => d == Volume);
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("This program requires administrator privileges to run");
        }
    }

    protected override void ProcessRecord()
    {
        if (_gotPrivileges)
        {
            uint searchedRecords = 0;
            int volumes = 0, found = 0;
            var stopwatch = Stopwatch.StartNew();

            Parallel.ForEach(_drives, drive =>
            {
                Interlocked.Increment(ref volumes);

                using var searcher = new MFT_Searcher(drive);

                Predicate<string> match;

                if (Distance > 0)
                {
                    match = s => LevenshteinDistance.GetDistance(s, Name) <= Distance;
                }
                else if (Regex)
                {
                    var regex = new Regex(Name, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    match = s => regex.IsMatch(s);
                }
                else
                {
                    string pattern = $"^{RegExpr.Regex.Escape(Name).Replace(@"\*", ".*").Replace(@"\?", ".")}$";
                    var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    match = s => regex.IsMatch(s);
                }

                var results = searcher.Search(match, Folders);

                foreach (var result in results)
                {
                    lock(stopwatch)
                    {
                        if (Distance == 0)
                        {
                            WritePattern(result, Name, Regex);
                        }
                        else
                        {
                            WriteName(result, Name);
                        }

                        ++found;
                    }
                }

                Interlocked.Add(ref searchedRecords, searcher.SearchedRecords);
            });

            stopwatch.Stop();

            if (Stats)
            {
                Console.WriteLine($"\nSearched {searchedRecords} records on {volumes} volume{(volumes != 1 ? "s" : "")} in {stopwatch.Elapsed.TotalSeconds:0.##}s." +
                                  $" Found {found} result{(found != 1 ? "s" : "")}");
                WriteVerbose($"\nSearched {searchedRecords} records on {volumes} volume{(volumes != 1 ? "s" : "")} in {stopwatch.Elapsed.TotalSeconds:0.##}s." +
                                  $" Found {found} result{(found != 1 ? "s" : "")}");
            }
        }
    }

    private static bool IsAdmin()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    // Returns a list of all internal volumes in this machine that use the NTFS filesystem.
    private static IEnumerable<char> GetValidDrives() => from drive in DriveInfo.GetDrives()
                                                         where drive.IsReady && drive.DriveFormat == "NTFS"
                                                         select drive.Name[0];

    private static void WritePattern(string path, string word, bool isRegex)
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

        // Convert AddCaptureGroups symbols to parenthesis (they can't be returned directly by that method because the would get escaped by Regex.Escape)
        string r = $"{word.Replace("<", "(").Replace(">", ")").Replace(@"\*", ".*").Replace(@"\?", ".")}";
        var regex = new Regex(r, RegexOptions.IgnoreCase);

        // Skip first capture group since it contains the entire file name
        foreach (var group in regex.Match(fileName).Groups.Values.Skip(1))
        {
            for (int i = group.Index; i < group.Index + group.Length; i++)
            {
                mask[i] = true;
            }
        }

        for (int i = 0; i < fileName.Length; i++)
        {
            if (mask[i])
            {
                Console.ForegroundColor = ConsoleColor.Blue;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }

            Console.Write(fileName[i]);
        }

        Console.ResetColor();
        Console.WriteLine();


        static string AddCaptureGroups(string pattern)
        {
            string output = "";

            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] == '*' || pattern[i] == '?')
                {
                    if (i == 0)
                    {
                        output += pattern[i] + "<";
                    }
                    else if (i == pattern.Length - 1)
                    {
                        output += ">" + pattern[i];
                    }
                    else
                    {
                        output += ">" + pattern[i] + "<";
                    }
                }
                else
                {
                    if (i == 0)
                    {
                        output += "<" + pattern[i];
                    }
                    else if (i == pattern.Length - 1)
                    {
                        output += pattern[i] + ">";
                    }
                    else
                    {
                        output += pattern[i];
                    }
                }
            }

            return output;
        }
    }

    private static void WriteName(string path, string word)
    {
        string fileName = Path.GetFileName(path);
        int index = path.IndexOf(fileName);
        Console.Write(path[..index]);
        
        for (int i = 0; i < fileName.Length; i++)
        {
            if (fileName[i] == word[i])
            {
                Console.ForegroundColor = ConsoleColor.Blue;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }

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
