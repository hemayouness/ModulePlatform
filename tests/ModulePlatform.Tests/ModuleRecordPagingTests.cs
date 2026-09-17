using FluentAssertions;
using ModulePlatform.Core.Domain;

namespace ModulePlatform.Tests;

/// <summary>
/// Validation of the generic <c>?page=</c>/<c>?pageSize=</c> pair.
/// </summary>
public sealed class ModuleRecordPagingTests
{
    private const int Max = 200;
    private const int Default = 50;

    private static bool Parse(int? page, int? pageSize, out ModuleRecordPaging paging, out string? error) =>
        ModuleRecordPaging.TryParse(page, pageSize, Max, Default, out paging, out error);

    [Fact]
    public void Paging_is_opt_in()
    {
        Parse(null, null, out var paging, out var error).Should().BeTrue();

        error.Should().BeNull();
        paging.Should().Be(ModuleRecordPaging.None);
        paging.Take.Should().BeNull(
            "omitting both parameters returned every matching row before pagination existed, "
            + "and existing callers still rely on that");
    }

    [Fact]
    public void Both_parameters_produce_a_skip_and_take()
    {
        Parse(3, 5, out var paging, out _).Should().BeTrue();

        paging.Skip.Should().Be(10, "pages are 1-based, so page 3 of 5 skips the first two pages");
        paging.Take.Should().Be(5);
    }

    [Fact]
    public void A_page_size_at_the_cap_is_accepted()
    {
        Parse(1, Max, out var paging, out var error).Should().BeTrue();

        error.Should().BeNull();
        paging.Take.Should().Be(Max);
    }

    [Fact]
    public void A_page_size_above_the_cap_is_refused_rather_than_trimmed()
    {
        Parse(1, Max + 1, out var paging, out var error).Should().BeFalse(
            "silently returning fewer rows than asked for would leave the caller computing its "
            + "page count from the size it requested, so it would believe it had shown everything");

        error.Should().Contain(Max.ToString());
        paging.Should().Be(ModuleRecordPaging.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_page_is_refused(int page)
    {
        Parse(page, 5, out _, out var error).Should().BeFalse();

        error.Should().Contain("page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_page_size_is_refused(int pageSize)
    {
        Parse(1, pageSize, out _, out var error).Should().BeFalse();

        error.Should().Contain("pageSize");
    }

    [Fact]
    public void A_page_without_a_page_size_falls_back_to_the_configured_default()
    {
        Parse(2, null, out var paging, out _).Should().BeTrue();

        paging.Take.Should().Be(Default,
            "asking for 'page 2' and receiving the entire collection is never what was meant");
        paging.Skip.Should().Be(Default);
    }

    [Fact]
    public void A_page_size_without_a_page_does_not_paginate()
    {
        Parse(null, 1, out var paging, out var error).Should().BeTrue();

        error.Should().BeNull();
        paging.Should().Be(ModuleRecordPaging.None,
            "a caller passing only a size is reading X-Total-Count, and that behaviour predates "
            + "this validation");
    }

    [Fact]
    public void A_page_size_without_a_page_is_still_bounds_checked()
    {
        Parse(null, Max + 1, out _, out var error).Should().BeFalse();

        error.Should().NotBeNull();
    }

    [Fact]
    public void A_misconfigured_cap_falls_back_to_a_sane_one_instead_of_disabling_the_guard()
    {
        ModuleRecordPaging.TryParse(1, 500, maxPageSize: 0, defaultPageSize: 0, out _, out var error)
            .Should().BeFalse("a zero or negative configured cap must not read as 'no limit'");

        error.Should().NotBeNull();
    }
}
