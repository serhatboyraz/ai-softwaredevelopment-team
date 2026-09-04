namespace AiDevAgent.Application.ProjectType;

public sealed record ProjectBuildProfile(
    string Kind,
    string? BuildCommand,
    string? TestCommand);

public static class ProjectTypeDetector
{
    public static ProjectBuildProfile Detect(IEnumerable<string> relativeFiles)
    {
        var files = relativeFiles
            .Select(f => f.Replace('\\', '/'))
            .ToList();

        bool HasSuffix(string suffix) =>
            files.Any(f => f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        bool HasName(string name) =>
            files.Any(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));

        if (HasSuffix(".sln") || HasSuffix(".csproj"))
            return new("dotnet", "dotnet build --nologo", "dotnet test --nologo --no-build --verbosity minimal");

        if (HasName("pom.xml"))
            return new("maven", "mvn -q -DskipTests compile", "mvn -q test");

        if (HasName("build.gradle") || HasName("build.gradle.kts"))
            return new("gradle", "gradle build -x test", "gradle test");

        if (HasName("package.json"))
            return new("node", "npm install --ignore-scripts", "npm test --if-present");

        if (HasName("go.mod"))
            return new("go", "go build ./...", "go test ./...");

        if (HasName("pyproject.toml") || HasName("requirements.txt") || HasName("pytest.ini"))
            return new("python", null, "pytest -q");

        return new("unknown", null, null);
    }
}
