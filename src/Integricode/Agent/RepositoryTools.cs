using System.ComponentModel;
using System.Globalization;
using System.Text;
using Integricode.Domain;
using Microsoft.Extensions.AI;

namespace Integricode.Agent;

/// <summary>
/// The review agent's read-only view of the head-SHA checkout (ADR-002): <c>read_file</c>, <c>search</c>, and
/// <c>list_dir</c>, guarded by <see cref="PathGuard"/>, bounded by <see cref="ContextBudget"/>, and logged chunk by chunk
/// for the context snapshot.
/// </summary>
public sealed class RepositoryTools(PathGuard guard, ContextBudget budget)
{
    public const int MaxSearchResults = 100;
    private const int MaxSearchLineLength = 300;
    private const long MaxSearchFileBytes = 1_000_000;

    private readonly List<ContextChunk> chunks = [];
    private readonly Lock gate = new();

    public ContextBudget Budget => budget;

    /// <summary>Every chunk returned so far, in order, with path, line range, and content hash.</summary>
    public IReadOnlyList<ContextChunk> Chunks
    {
        get
        {
            lock (gate)
            {
                return [.. chunks];
            }
        }
    }

    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(ReadFile, "read_file", "Reads a line range of a file in the pull request's head-SHA checkout. Lines are returned as 'N: text'."),
        AIFunctionFactory.Create(Search, "search", "Searches the checkout for a case-insensitive text and returns up to 100 matches as 'path:line: text'."),
        AIFunctionFactory.Create(ListDir, "list_dir", "Lists a directory of the checkout; directories end with '/'."),
    ];

    public string ReadFile(
        [Description("File path relative to the repository root.")] string path,
        [Description("First line to read, 1-based; omit to start at line 1.")] int? from = null,
        [Description("Last line to read, inclusive; omit to read to the end.")] int? to = null)
    {
        if (!budget.TryStartCall())
        {
            return BudgetRefusal();
        }

        var check = guard.Check(path);
        if (!check.Allowed)
        {
            return Refusal(check.Refusal!);
        }

        if (!File.Exists(check.FullPath))
        {
            return Refusal("file not found");
        }

        if (!TextFile.TryRead(check.FullPath, out var text))
        {
            return Refusal("binary file");
        }

        var lines = TextFile.SplitLines(text);
        var first = Math.Max(1, from ?? 1);
        var last = Math.Min(lines.Count, to ?? lines.Count);
        if (first > last)
        {
            return Refusal($"line range outside the file ({lines.Count.ToString(CultureInfo.InvariantCulture)} lines)");
        }

        var output = Numbered(lines, first, last);
        return Return("read_file", check.RelativePath, first, last, output);
    }

    public string Search([Description("Text to find, matched case-insensitively.")] string pattern)
    {
        if (!budget.TryStartCall())
        {
            return BudgetRefusal();
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Refusal("the search text is empty");
        }

        var matches = new List<(string Path, int Line, string Text)>();
        foreach (var file in EnumerateFiles(string.Empty))
        {
            if (matches.Count >= MaxSearchResults)
            {
                break;
            }

            var fullPath = Path.Combine(guard.Root, file);
            if (new FileInfo(fullPath).Length > MaxSearchFileBytes || !TextFile.TryRead(fullPath, out var text))
            {
                continue;
            }

            var lines = TextFile.SplitLines(text);
            for (var i = 0; i < lines.Count && matches.Count < MaxSearchResults; i++)
            {
                if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var line = lines[i].Length > MaxSearchLineLength ? lines[i][..MaxSearchLineLength] : lines[i];
                    matches.Add((file, i + 1, line));
                }
            }
        }

        if (matches.Count == 0)
        {
            return "no matches";
        }

        var output = string.Join('\n', matches.Select(m => $"{m.Path}:{m.Line.ToString(CultureInfo.InvariantCulture)}: {m.Text}"));
        if (!budget.TryConsume(output.Length))
        {
            return BudgetRefusal();
        }

        lock (gate)
        {
            chunks.AddRange(matches.Select(m => ContextChunk.Create("search", m.Path, m.Line, m.Line, m.Text)));
        }

        return output;
    }

    public string ListDir([Description("Directory path relative to the repository root; '.' for the root.")] string path)
    {
        if (!budget.TryStartCall())
        {
            return BudgetRefusal();
        }

        var check = guard.Check(path);
        if (!check.Allowed)
        {
            return Refusal(check.Refusal!);
        }

        if (!Directory.Exists(check.FullPath))
        {
            return Refusal("directory not found");
        }

        var entries = new DirectoryInfo(check.FullPath)
            .EnumerateFileSystemInfos()
            .Select(entry => (Entry: entry, Relative: Join(check.RelativePath, entry.Name)))
            .Where(item => !guard.IsExcluded(item.Relative))
            .Select(item => item.Entry is DirectoryInfo ? item.Entry.Name + "/" : item.Entry.Name)
            .Order(StringComparer.Ordinal);
        var output = string.Join('\n', entries);
        return Return("list_dir", check.RelativePath.Length == 0 ? "." : check.RelativePath, 0, 0, output);
    }

    /// <summary>
    /// Reads a changed file for the fixed input: refused paths, missing or binary files return null; the text counts
    /// against the character budget but not against the tool calls.
    /// </summary>
    public string? ReadChangedFile(string path)
    {
        var check = guard.Check(path);
        if (!check.Allowed || !File.Exists(check.FullPath) || !TextFile.TryRead(check.FullPath, out var text))
        {
            return null;
        }

        var lines = TextFile.SplitLines(text);
        var output = Numbered(lines, 1, lines.Count);
        if (!budget.TryConsume(output.Length))
        {
            return null;
        }

        lock (gate)
        {
            chunks.Add(ContextChunk.Create("changed_file", check.RelativePath, 1, lines.Count, output));
        }

        return output;
    }

    private string Return(string source, string path, int from, int to, string output)
    {
        if (!budget.TryConsume(output.Length))
        {
            return BudgetRefusal();
        }

        lock (gate)
        {
            chunks.Add(ContextChunk.Create(source, path, from, to, output));
        }

        return output;
    }

    private IEnumerable<string> EnumerateFiles(string relativeDirectory)
    {
        var directory = new DirectoryInfo(relativeDirectory.Length == 0 ? guard.Root : Path.Combine(guard.Root, relativeDirectory));
        foreach (var entry in directory.EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            var relative = Join(relativeDirectory, entry.Name);

            // Links are not followed: their target may leave the checkout.
            if (entry.LinkTarget is not null || guard.IsExcluded(relative))
            {
                continue;
            }

            if (entry is DirectoryInfo)
            {
                foreach (var file in EnumerateFiles(relative))
                {
                    yield return file;
                }
            }
            else
            {
                yield return relative;
            }
        }
    }

    private static string Join(string directory, string name) => directory.Length == 0 ? name : directory + "/" + name;

    private static string Numbered(IReadOnlyList<string> lines, int first, int last)
    {
        var builder = new StringBuilder();
        for (var i = first; i <= last; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(lines[i - 1]);
            if (i < last)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string Refusal(string reason) => "refused: " + reason;

    private string BudgetRefusal() =>
        Refusal($"{budget.ReachedReason}; finish the review with the context you already have");
}

/// <summary>Reads checkout files as text, the same way for the tools and for validation.</summary>
public static class TextFile
{
    private const int BinaryProbeBytes = 8000;

    /// <summary>Reads a UTF-8 text file; returns false for a file with a NUL byte in its first 8,000 bytes.</summary>
    public static bool TryRead(string fullPath, out string text)
    {
        var bytes = File.ReadAllBytes(fullPath);
        if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, BinaryProbeBytes)) >= 0)
        {
            text = string.Empty;
            return false;
        }

        text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes).TrimStart('﻿');
        return true;
    }

    /// <summary>Splits text into lines; a final line ending does not start another line.</summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (text.EndsWith('\n'))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
