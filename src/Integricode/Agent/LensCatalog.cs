namespace Integricode.Agent;

/// <summary>
/// The review lenses embedded in the tool, adapted from the dev-flow <c>pr-review</c> skill: each one is the hunting list
/// of a stack, chosen by the pull request's file paths. The security lens applies to every change that is not only docs.
/// </summary>
public static class LensCatalog
{
    public const string CSharp = "csharp";
    public const string Python = "python";
    public const string React = "react";
    public const string GitHubActions = "github-actions";
    public const string Containers = "containers";
    public const string Contracts = "contracts-and-dependencies";
    public const string Docs = "docs";
    public const string Security = "security";

    private static readonly string[] Order = [CSharp, Python, React, GitHubActions, Containers, Contracts, Docs, Security];

    private static readonly HashSet<string> CSharpExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cs", ".csproj", ".razor", ".cshtml" };
    private static readonly HashSet<string> ReactExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".css", ".scss" };
    private static readonly HashSet<string> ContractExtensions = new(StringComparer.OrdinalIgnoreCase) { ".proto", ".graphql", ".gql" };
    private static readonly HashSet<string> DocsExtensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".mdx" };

    private static readonly HashSet<string> DependencyManifests = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json", "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "packages.lock.json", "Directory.Packages.props",
        "pyproject.toml", "poetry.lock", "Pipfile", "Pipfile.lock", "go.mod", "go.sum", "Cargo.toml", "Cargo.lock", "pom.xml",
        "build.gradle", "build.gradle.kts", "Gemfile", "Gemfile.lock", "composer.json", "composer.lock",
    };

    private static readonly HashSet<string> PythonManifests = new(StringComparer.OrdinalIgnoreCase) { "pyproject.toml", "Pipfile", "Pipfile.lock" };

    private static readonly HashSet<string> NodeManifests = new(StringComparer.OrdinalIgnoreCase) { "package.json", "package-lock.json", "yarn.lock", "pnpm-lock.yaml" };

    /// <summary>
    /// The lenses the pull request's files call for, in a fixed order; each follows the file patterns of its lens's
    /// "Applies to" line. Code that only touches a documented surface does not select the docs lens, since a path cannot
    /// show that.
    /// </summary>
    /// <param name="paths">The pull request's file paths, removed files included.</param>
    /// <returns>The lens names; empty when there are no files.</returns>
    public static IReadOnlyList<string> Select(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            foreach (var lens in LensesOf(path.Replace('\\', '/')))
            {
                selected.Add(lens);
            }
        }

        return [.. Order.Where(selected.Contains)];
    }

    /// <summary>The text of an embedded lens.</summary>
    /// <param name="name">A lens name from <see cref="Select"/>.</param>
    /// <returns>The lens as Markdown.</returns>
    /// <exception cref="InvalidOperationException">The lens is not embedded.</exception>
    public static string Load(string name)
    {
        using var stream = typeof(LensCatalog).Assembly.GetManifestResourceStream($"Integricode.Agent.Lenses.{name}.md")
            ?? throw new InvalidOperationException($"The embedded lens '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().TrimEnd();
    }

    private static IEnumerable<string> LensesOf(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var extension = Path.GetExtension(name);
        if (DocsExtensions.Contains(extension))
        {
            yield return Docs;
            yield break;
        }

        yield return Security;
        if (CSharpExtensions.Contains(extension))
        {
            yield return CSharp;
        }

        if (extension.Equals(".py", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pyi", StringComparison.OrdinalIgnoreCase)
            || PythonManifests.Contains(name)
            || IsRequirementsFile(name, extension))
        {
            yield return Python;
        }

        if (ReactExtensions.Contains(extension) || NodeManifests.Contains(name))
        {
            yield return React;
        }

        if (path.StartsWith(".github/workflows/", StringComparison.Ordinal) || path.StartsWith(".github/actions/", StringComparison.Ordinal))
        {
            yield return GitHubActions;
        }

        if (name.StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Containerfile", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".dockerfile", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".dockerignore", StringComparison.OrdinalIgnoreCase)
            || IsComposeFile(name))
        {
            yield return Containers;
        }

        // A .csproj holds its PackageReference versions when the repository has no central package management.
        if (DependencyManifests.Contains(name)
            || ContractExtensions.Contains(extension)
            || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || name.Equals("packages.config", StringComparison.OrdinalIgnoreCase)
            || IsRequirementsFile(name, extension)
            || name.StartsWith("openapi", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("swagger", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("asyncapi", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)
            || IsController(path, name))
        {
            yield return Contracts;
        }
    }

    // Controllers back a code-first API contract, so a route or DTO change there gets the contract checks.
    private static bool IsController(string path, string name) =>
        path.Contains("/Controllers/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("Controllers/", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Controller.cs", StringComparison.Ordinal);

    private static bool IsRequirementsFile(string name, string extension) =>
        name.StartsWith("requirements", StringComparison.OrdinalIgnoreCase) && extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

    private static bool IsComposeFile(string name) =>
        (name.StartsWith("compose", StringComparison.OrdinalIgnoreCase) || name.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase))
        && (name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));
}
