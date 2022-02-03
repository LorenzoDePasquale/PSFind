using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PSFind.Tests;

[TestClass]
public class MFT_Searcher_Tests
{
    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory("Test");
    }

    [TestMethod]
    public void SearchFile()
    {
        // Create a uniquely named file, to avoid finding other already existing files
        string fileName = Guid.NewGuid().ToString();
        File.Create(Path.Combine("Test", fileName)).Close();

        // Find the file
        char drive = Environment.CurrentDirectory[0];
        using var searcher = new MFT_Searcher(drive);
        var results = searcher.Search(fileName)?.ToArray(); // Conversion to array to avoid enumerating twice the IEnumerable result (thus performing the search twice!)

        // Assert that the new file, and only that, is found by the searcher
        Assert.IsNotNull(results);
        Assert.AreEqual(1, results.Length);
        Assert.AreEqual(fileName, Path.GetFileName(results[0]));
    }

    [TestMethod]
    public void SearchFilePattern()
    {
        // Create a uniquely named file, to avoid finding other already existing files
        string fileName = Guid.NewGuid().ToString();
        File.Create(Path.Combine("Test", fileName)).Close();

        // Find the file using a regex pattern
        char drive = Environment.CurrentDirectory[0];
        using var searcher = new MFT_Searcher(drive);
        var results = searcher.SearchPattern($"^{fileName}$")?.ToArray(); // Conversion to array to avoid enumerating twice the IEnumerable result (thus performing the search twice!)

        // Assert that the new file, and only that, is found by the searcher
        Assert.IsNotNull(results);
        Assert.AreEqual(1, results.Length);
        Assert.AreEqual(fileName, Path.GetFileName(results[0]));
    }

    [TestMethod]
    public void SearchDirectory()
    {
        // Create a uniquely named directory, to avoid finding other already existing files
        string directoryName = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Combine("Test", directoryName));

        // Find the file
        char drive = Environment.CurrentDirectory[0];
        using var searcher = new MFT_Searcher(drive);
        var results = searcher.Search(directoryName, true)?.ToArray(); // Conversion to array to avoid enumerating twice the IEnumerable result (thus performing the search twice!)

        // Assert that the new directory, and only that, is found by the searcher
        Assert.IsNotNull(results);
        Assert.AreEqual(1, results.Length);
        Assert.AreEqual(directoryName, new DirectoryInfo(results[0]).Name);
    }

    [TestMethod]
    public void SearchDirectoryPattern()
    {
        // Create a uniquely named directory, to avoid finding other already existing files
        string directoryName = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Combine("Test", directoryName));

        // Find the file using a regex pattern
        char drive = Environment.CurrentDirectory[0];
        using var searcher = new MFT_Searcher(drive);
        var results = searcher.SearchPattern($"^{directoryName}$", true)?.ToArray(); // Conversion to array to avoid enumerating twice the IEnumerable result (thus performing the search twice!)

        // Assert that the new directory, and only that, is found by the searcher
        Assert.IsNotNull(results);
        Assert.AreEqual(1, results.Length);
        Assert.AreEqual(directoryName, new DirectoryInfo(results[0]).Name);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(Path.Combine(Environment.CurrentDirectory, "Test"), true);
    }
}
