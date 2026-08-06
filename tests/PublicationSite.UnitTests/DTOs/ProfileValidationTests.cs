using FluentAssertions;
using PublicationSite.Api.DTOs.Auth;
using PublicationSite.Api.DTOs.Users;
using Xunit;

namespace PublicationSite.UnitTests.DTOs;

/// <summary>
/// What a person may write into their own profile, and into an invitation.
///
/// Both write to columns with a length, and neither was checked. A name of five hundred characters
/// reached the database and came back to the caller as "an unexpected error occurred", which names
/// no field and suggests the fault is the server's. An empty name went through and left an account
/// that reads as a blank space wherever it is listed.
/// </summary>
public class ProfileValidationTests
{
    private static readonly UpdateMyProfileRequestValidator Profile = new();
    private static readonly CreateInvitationRequestValidator Invitation = new();

    private static UpdateMyProfileRequest AProfile(
        string first = "Alex", string last = "Moreau", string? programme = null,
        string? cohort = null, string? orcid = null) =>
        new(first, last, programme, cohort, null, orcid, null, null, null);

    [Theory]
    [InlineData("", "Moreau")]
    [InlineData("   ", "Moreau")]
    [InlineData("Alex", "")]
    [InlineData("Alex", "   ")]
    public void A_person_cannot_leave_themselves_without_a_name(string first, string last) =>
        Profile.Validate(AProfile(first, last)).IsValid.Should().BeFalse();

    [Fact]
    public void A_name_longer_than_the_column_is_refused_rather_than_stored() =>
        Profile.Validate(AProfile(first: new string('A', 151))).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(201, 50, 50)]
    [InlineData(200, 51, 50)]
    [InlineData(200, 50, 51)]
    public void The_optional_fields_are_held_to_their_columns_too(int programme, int cohort, int orcid) =>
        Profile.Validate(AProfile(
            programme: new string('P', programme),
            cohort: new string('C', cohort),
            orcid: new string('O', orcid))).IsValid.Should().BeFalse();

    /// <summary>
    /// Omitted rather than emptied. The service reads null as "leave it as it is", which is what a
    /// supervisor updating their interests without touching their programme is doing.
    /// </summary>
    [Fact]
    public void Leaving_the_optional_fields_out_is_allowed() =>
        Profile.Validate(AProfile()).IsValid.Should().BeTrue();

    [Fact]
    public void An_ordinary_profile_passes() =>
        Profile.Validate(AProfile(programme: "MSc Information Technology", cohort: "2026 Semester 1",
            orcid: "0000-0002-4517-8231")).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("", "Alex", "Moreau")]
    [InlineData("not-an-address", "Alex", "Moreau")]
    [InlineData("alex@ais.ac.nz", "", "Moreau")]
    [InlineData("alex@ais.ac.nz", "Alex", "")]
    public void An_invitation_needs_an_address_and_a_name(string email, string first, string last) =>
        Invitation.Validate(new CreateInvitationRequest(email, "Supervisor", first, last, null))
            .IsValid.Should().BeFalse();

    [Fact]
    public void An_invitation_name_longer_than_the_column_is_refused() =>
        Invitation.Validate(new CreateInvitationRequest(
            "alex@ais.ac.nz", "Supervisor", new string('A', 101), "Moreau", null))
            .IsValid.Should().BeFalse();

    [Fact]
    public void An_ordinary_invitation_passes() =>
        Invitation.Validate(new CreateInvitationRequest(
            "alex@ais.ac.nz", "Supervisor", "Alex", "Moreau", null)).IsValid.Should().BeTrue();
}
