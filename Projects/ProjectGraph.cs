using SharpTS.Configuration;

namespace SharpTS.Projects;

/// <summary>A validated, dependency-first graph of referenced tsconfig projects.</summary>
public sealed class ProjectGraph
{
    private ProjectGraph(IReadOnlyList<TsConfigResult> projects)
    {
        Projects = projects;
    }

    public IReadOnlyList<TsConfigResult> Projects { get; }

    public static ProjectGraph Load(IEnumerable<string> rootConfigPaths)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var visited = new HashSet<string>(comparer);
        var loaded = new Dictionary<string, TsConfigResult>(comparer);
        var stack = new List<string>();
        var projects = new List<TsConfigResult>();

        void Visit(string configPath, bool isReference)
        {
            string full = Path.GetFullPath(configPath);
            if (visited.Contains(full))
            {
                if (isReference && loaded[full].Composite != true)
                {
                    throw new Exception(
                        $"Error: referenced project '{full}' must set compilerOptions.composite to true.");
                }
                return;
            }

            int cycleStart = stack.FindIndex(path => comparer.Equals(path, full));
            if (cycleStart >= 0)
            {
                string cycle = string.Join(
                    " -> ",
                    stack.Skip(cycleStart).Append(full));
                throw new Exception($"Error: circular project reference: {cycle}");
            }

            stack.Add(full);
            var project = TsConfigLoader.Load(full);
            loaded[full] = project;
            if (isReference && project.Composite != true)
            {
                throw new Exception(
                    $"Error: referenced project '{project.ConfigPath}' must set compilerOptions.composite to true.");
            }
            foreach (string reference in project.ProjectReferences)
                Visit(reference, isReference: true);
            stack.RemoveAt(stack.Count - 1);

            visited.Add(full);
            projects.Add(project);
        }

        foreach (string root in rootConfigPaths)
            Visit(root, isReference: false);

        return new ProjectGraph(projects);
    }
}
