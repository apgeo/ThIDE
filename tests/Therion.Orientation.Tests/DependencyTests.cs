// The library's one structural promise: it references nothing but the base class library, so an
// application that may not take a survey-format dependency can still use it.

using System;
using System.Linq;
using Therion.Orientation;
using Xunit;

namespace Therion.Orientation.Tests;

public class DependencyTests
{
    [Fact]
    public void The_library_references_only_the_base_class_library()
    {
        var foreign = typeof(Azimuth).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name != "System" && name != "mscorlib" && name != "netstandard"
                && !name.StartsWith("System.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(foreign);
    }
}
