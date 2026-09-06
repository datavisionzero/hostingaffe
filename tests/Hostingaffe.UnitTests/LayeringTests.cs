using System.Xml.Linq;

namespace Hostingaffe.UnitTests;

/// <summary>
/// ADR 0002 has the dependencies point inward and only inward, and
/// docs/codebase.md calls Domain's emptiness the cheapest check that nothing has
/// leaked into it. A convention nobody can run is not a check, so this reads the
/// project files and turns the layering into a failing build.
/// </summary>
public sealed class LayeringTests
{
    [Fact]
    public void Domain_depends_on_nothing_at_all()
    {
        var domain = Layer.Read("Hostingaffe.Domain");

        Assert.Empty(domain.ProjectReferences);
        Assert.Empty(domain.PackageReferences);
    }

    [Fact]
    public void Application_depends_on_domain_only()
    {
        Assert.Equal(["Hostingaffe.Domain"], Layer.Read("Hostingaffe.Application").ProjectReferences);
    }

    [Fact]
    public void Infrastructure_depends_on_application_only()
    {
        Assert.Equal(
            ["Hostingaffe.Application"],
            Layer.Read("Hostingaffe.Infrastructure").ProjectReferences);
    }

    [Fact]
    public void Api_is_the_composition_root_and_depends_on_the_two_outer_layers()
    {
        Assert.Equal(
            ["Hostingaffe.Application", "Hostingaffe.Infrastructure"],
            Layer.Read("Hostingaffe.Api").ProjectReferences);
    }

    private sealed record Layer(
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> PackageReferences)
    {
        public static Layer Read(string project)
        {
            var document = XDocument.Load(
                Path.Combine(RepositoryRoot(), "src", project, $"{project}.csproj"));

            return new Layer(References(document, "ProjectReference"), References(document, "PackageReference"));
        }

        private static IReadOnlyList<string> References(XDocument project, string element) =>
            [.. project.Descendants(element)
                .Select(reference => Path.GetFileNameWithoutExtension(
                    reference.Attribute("Include")?.Value ?? string.Empty))
                .Order(StringComparer.Ordinal)];

        // The test binary sits under tests/<project>/bin/…, and the solution
        // file is what says where the repository begins.
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Hostingaffe.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("No Hostingaffe.slnx above the test binary.");
        }
    }
}
