using FluentAssertions;
using ModulePlatform.Core.Domain;

namespace ModulePlatform.Tests;

/// <summary>
/// Parsing of the generic <c>?sort=</c> parameter.
/// </summary>
/// <remarks>
/// Pure, so the 400 paths are exercised without standing up HTTP — which is the
/// whole reason validation lives in a value type rather than inline in the
/// endpoint.
/// </remarks>
public sealed class ModuleRecordSortTests
{
    [Fact]
    public void Absent_sort_is_created_at_ascending()
    {
        ModuleRecordSort.TryParse(null, out var sort, out var error).Should().BeTrue();

        error.Should().BeNull();
        sort.Should().Be(ModuleRecordSort.Default);
        sort.Field.Should().Be(ModuleRecordSortField.CreatedAt);
        sort.Descending.Should().BeFalse(
            "a module's UI jumps to the last page after creating a record, which only works "
            + "while the default ordering puts new records last");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_sort_is_the_default_not_an_error(string raw)
    {
        ModuleRecordSort.TryParse(raw, out var sort, out var error).Should().BeTrue();

        error.Should().BeNull();
        sort.Should().Be(ModuleRecordSort.Default);
    }

    [Theory]
    [InlineData("createdAt", ModuleRecordSortField.CreatedAt)]
    [InlineData("updatedAt", ModuleRecordSortField.UpdatedAt)]
    [InlineData("status", ModuleRecordSortField.Status)]
    [InlineData("createdBy", ModuleRecordSortField.CreatedBy)]
    [InlineData("externalId", ModuleRecordSortField.ExternalId)]
    public void Every_real_column_is_sortable(string raw, ModuleRecordSortField expected)
    {
        ModuleRecordSort.TryParse(raw, out var sort, out _).Should().BeTrue();

        sort.Field.Should().Be(expected);
        sort.Descending.Should().BeFalse();
    }

    [Fact]
    public void Id_is_accepted_as_an_alias_for_external_id()
    {
        ModuleRecordSort.TryParse("id", out var sort, out _).Should().BeTrue();

        sort.Field.Should().Be(ModuleRecordSortField.ExternalId,
            "'id' is the name the DTO uses, so it is the name a caller sees and will reach for");
    }

    [Theory]
    [InlineData("CreatedAt")]
    [InlineData("CREATEDAT")]
    [InlineData("createdat")]
    public void Field_names_are_case_insensitive(string raw)
    {
        ModuleRecordSort.TryParse(raw, out var sort, out _).Should().BeTrue();

        sort.Field.Should().Be(ModuleRecordSortField.CreatedAt);
    }

    [Fact]
    public void A_leading_minus_means_descending()
    {
        ModuleRecordSort.TryParse("-updatedAt", out var sort, out _).Should().BeTrue();

        sort.Field.Should().Be(ModuleRecordSortField.UpdatedAt);
        sort.Descending.Should().BeTrue();
    }

    [Theory]
    [InlineData("title")]
    [InlineData("notes")]
    [InlineData("data.title")]
    [InlineData("items[0].name")]
    public void Fields_inside_the_opaque_payload_are_rejected(string raw)
    {
        ModuleRecordSort.TryParse(raw, out var sort, out var error).Should().BeFalse(
            "the platform stores a module's record as an opaque blob, so sorting by something "
            + "inside it cannot be done in SQL — and quietly ignoring the request would look "
            + "like it worked");

        error.Should().NotBeNull();
        sort.Should().Be(ModuleRecordSort.Default);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("-nope")]
    [InlineData("-")]
    [InlineData("createdAt; drop table ModuleRecords")]
    [InlineData("createdAt desc")]
    public void Unknown_sort_fields_are_rejected(string raw)
    {
        ModuleRecordSort.TryParse(raw, out _, out var error).Should().BeFalse();

        error.Should().NotBeNull();
    }

    [Fact]
    public void The_error_names_the_fields_that_would_have_worked()
    {
        ModuleRecordSort.TryParse("title", out _, out var error);

        error.Should().Contain("createdAt").And.Contain("status").And.Contain("externalId");
    }
}
