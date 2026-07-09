using System;
using System.IO;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

// PathScope backs both the status-bar breadcrumb (workspace vs. OS file manager) and the audit's
// orphan-scan exclusions, so it is tested once here.
public class PathScopeTests
{
    private static string P(params string[] parts) => Path.GetFullPath(Path.Combine(parts));

    [Fact]
    public void A_file_inside_the_directory_is_under_it()
        => Assert.True(PathScope.IsUnder(P("/p/cave/th/a.th"), P("/p/cave")));

    [Fact]
    public void A_directory_is_under_itself()
        => Assert.True(PathScope.IsUnder(P("/p/cave"), P("/p/cave")));

    [Fact]
    public void A_sibling_directory_is_not_under_it()
        => Assert.False(PathScope.IsUnder(P("/p/other/a.th"), P("/p/cave")));

    [Fact]
    public void A_parent_directory_is_not_under_its_child()
        => Assert.False(PathScope.IsUnder(P("/p"), P("/p/cave")));

    // "/p/cave-backup" must not be swallowed by the "/p/cave" prefix.
    [Fact]
    public void A_directory_sharing_a_name_prefix_is_not_under_it()
        => Assert.False(PathScope.IsUnder(P("/p/cave-backup/a.th"), P("/p/cave")));

    [Fact]
    public void Null_or_empty_arguments_are_not_under_anything()
    {
        Assert.False(PathScope.IsUnder(null, P("/p")));
        Assert.False(PathScope.IsUnder(P("/p/a.th"), null));
        Assert.False(PathScope.IsUnder(string.Empty, string.Empty));
    }
}

public class AuditExclusionTests
{
    private static string P(params string[] parts) => Path.GetFullPath(Path.Combine(parts));

    [Fact]
    public void A_file_under_an_excluded_directory_is_excluded()
        => Assert.True(ProjectAuditViewModel.IsExcluded(P("/p/backup/old.th"), new[] { P("/p/backup") }));

    [Fact]
    public void A_file_outside_every_excluded_directory_is_kept()
        => Assert.False(ProjectAuditViewModel.IsExcluded(P("/p/th/a.th"), new[] { P("/p/backup"), P("/p/archive") }));

    [Fact]
    public void Any_matching_exclusion_is_enough()
        => Assert.True(ProjectAuditViewModel.IsExcluded(P("/p/archive/deep/x.th2"),
            new[] { P("/p/backup"), P("/p/archive") }));

    [Fact]
    public void With_no_exclusions_nothing_is_excluded()
        => Assert.False(ProjectAuditViewModel.IsExcluded(P("/p/th/a.th"), Array.Empty<string>()));
}
