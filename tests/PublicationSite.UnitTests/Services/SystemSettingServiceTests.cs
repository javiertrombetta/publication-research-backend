using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

/// <summary>
/// The settings that decide who gets in, and the combinations that must never be storable.
/// </summary>
public class SystemSettingServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<IAuditService> _auditService = new();

    /// <summary>
    /// A real row, not a loose Guid: every setting records who last changed it, and that column
    /// is a foreign key the database enforces.
    /// </summary>
    private readonly Guid _admin;

    public SystemSettingServiceTests() => _admin = TestDataBuilder.User(_fixture.Context).Id;

    /// <param name="demoData">
    /// Whether this deployment seeds the demonstration dataset, which is how it says its data is
    /// disposable. Null leaves it unset, so the environment name decides as it always did.
    /// </param>
    private SystemSettingService CreateService(
        string environmentName, string? azureTenantId = null, bool? demoData = null)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:TenantId"] = azureTenantId,
                ["Seed:DemoData"] = demoData?.ToString()
            })
            .Build();

        // A fresh cache per service, so one test's writes never leak into another's reads.
        var provider = new SystemSettingsProvider(_fixture.Context, new MemoryCache(new MemoryCacheOptions()));

        // Storage is only reached when a destination is being saved or tested, and none of these
        // tests do either, so a stub is honest here in a way it would not be for the settings
        // provider above.
        return new SystemSettingService(_fixture.Context, provider, _auditService.Object,
            environment.Object, configuration, new Mock<IFileStorageService>().Object,
            new Mock<IStorageMigrationService>().Object);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Registration_defaults_to_open_in_development()
    {
        var result = await CreateService(Environments.Development).GetAccessSettingsAsync();

        result.RegistrationMode.Should().Be(SettingKeys.RegistrationModeOpen);
        result.IsEnvironmentDefault.Should().BeTrue();
    }

    /// <summary>
    /// The one that matters: an unconfigured production deployment must not be handing out
    /// accounts to anyone who guesses the email domain.
    /// </summary>
    [Fact]
    public async Task Registration_defaults_to_invite_only_where_the_data_is_real()
    {
        var result = await CreateService(Environments.Production).GetAccessSettingsAsync();

        result.RegistrationMode.Should().Be(SettingKeys.RegistrationModeInviteOnly);
        result.IsEnvironmentDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Open_registration_cannot_be_chosen_where_the_data_is_real()
    {
        var service = CreateService(Environments.Production);

        var act = () => service.UpdateAccessSettingsAsync(
            new UpdateAccessSettingsRequest(SettingKeys.RegistrationModeOpen, false, 14, 30, 14), _admin);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("disposable");
    }

    /// <summary>
    /// A hosted testing deployment runs with the environment set to Production, because it is a
    /// real server rather than somebody's laptop. What makes it safe to open is that its data is
    /// disposable, which is what seeding the demonstration dataset says.
    /// </summary>
    [Fact]
    public async Task Open_registration_can_be_chosen_where_the_data_is_disposable()
    {
        var service = CreateService(Environments.Production, demoData: true);

        var result = await service.UpdateAccessSettingsAsync(
            new UpdateAccessSettingsRequest(SettingKeys.RegistrationModeOpen, false, 14, 30, 14), _admin);

        result.RegistrationMode.Should().Be(SettingKeys.RegistrationModeOpen);
        result.CanOpenRegistration.Should().BeTrue();
    }

    /// <summary>
    /// And a production deployment that does not seed sample data says so, so the screen greys the
    /// choice out rather than offering something the API would refuse.
    /// </summary>
    [Fact]
    public async Task Access_settings_say_whether_open_registration_is_available()
    {
        (await CreateService(Environments.Production).GetAccessSettingsAsync())
            .CanOpenRegistration.Should().BeFalse();

        (await CreateService(Environments.Production, demoData: true).GetAccessSettingsAsync())
            .CanOpenRegistration.Should().BeTrue();
    }

    [Fact]
    public async Task Azure_sso_reports_whether_a_tenant_is_actually_configured()
    {
        var withoutTenant = await CreateService(Environments.Production).GetAccessSettingsAsync();
        withoutTenant.AzureSsoConfigured.Should().BeFalse();

        var withTenant = await CreateService(Environments.Production, "a-tenant-id").GetAccessSettingsAsync();
        withTenant.AzureSsoConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task A_committee_cannot_need_more_approvals_than_it_has_members()
    {
        var service = CreateService(Environments.Development);

        var act = () => service.UpdateCommitteeSettingsAsync(new UpdateCommitteeSettingsRequest(2, 1, 5), _admin);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("cannot be asked for 5 approvals");
    }

    /// <summary>
    /// The same address cannot mean both a student and a member of staff. Whichever check ran first
    /// would decide, which is a coin toss dressed up as a rule.
    /// </summary>
    [Fact]
    public async Task Students_and_staff_cannot_share_an_email_domain()
    {
        var service = CreateService(Environments.Development);

        var act = () => service.UpdateInstitutionSettingsAsync(
            new UpdateInstitutionSettingsRequest("AIS", "@ais.ac.nz", "@ais.ac.nz", null, null, null, null), _admin);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Email_domains_are_stored_with_a_leading_at_sign_however_they_are_typed()
    {
        var service = CreateService(Environments.Development);

        var result = await service.UpdateInstitutionSettingsAsync(
            new UpdateInstitutionSettingsRequest("AIS", "students.ais.ac.nz", "@AIS.ac.nz", null, null, null, null),
            _admin);

        result.StudentEmailDomain.Should().Be("@students.ais.ac.nz");
        result.StaffEmailDomain.Should().Be("@ais.ac.nz");
    }

    [Fact]
    public async Task Upload_extensions_are_accepted_however_they_are_typed()
    {
        var service = CreateService(Environments.Development);

        var result = await service.UpdateUploadSettingsAsync(
            new UpdateUploadSettingsRequest(25, "pdf, .DOCX; zip"), _admin);

        result.AllowedExtensions.Should().Be(".pdf,.docx,.zip");
        result.MaxMegabytes.Should().Be(25);
    }

    [Fact]
    public async Task Email_notifications_cannot_be_switched_on_without_somewhere_to_send_them()
    {
        var service = CreateService(Environments.Development);

        var act = () => service.UpdateNotificationSettingsAsync(
            new UpdateNotificationSettingsRequest(true, null, 587, null, null, true, null, null), _admin);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("SMTP server");
    }
}
