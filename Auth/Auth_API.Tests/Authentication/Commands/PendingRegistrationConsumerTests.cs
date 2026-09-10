using System.Text.RegularExpressions;
using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// One rule, written once and enforced in two ways: the consumer itself, and a
/// scan of every door that creates a user to see that it runs the consumer
/// after the row exists.
/// </summary>
public class PendingRegistrationConsumerTests
{
    private readonly Mock<IPendingRegistrationRepository> _pendingRegistrations = new();
    private readonly PendingRegistrationConsumer _consumer;

    public PendingRegistrationConsumerTests()
    {
        _consumer = new PendingRegistrationConsumer(
            _pendingRegistrations.Object,
            new Mock<ILogger<PendingRegistrationConsumer>>().Object);
    }

    [Fact]
    public async Task ConsumesByTheKeyTheRowIsStoredUnder()
    {
        _pendingRegistrations
            .Setup(r => r.ConsumeByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _consumer.ConsumeAsync(" Jane.Doe@Example.COM ", CancellationToken.None);

        _pendingRegistrations.Verify(
            r => r.ConsumeByEmailAsync("JANE.DOE@EXAMPLE.COM", It.IsAny<CancellationToken>()), Times.Once,
            "the same derivation the pending row was stored under, whatever case the door had the address in");
    }

    [Fact]
    public async Task AFailure_IsSwallowed_TheAccountAlreadyExists()
    {
        _pendingRegistrations
            .Setup(r => r.ConsumeByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("pending table unavailable"));

        var act = () => _consumer.ConsumeAsync("jane.doe@example.com", CancellationToken.None);

        await act.Should().NotThrowAsync("the door that called this has already committed the account; a stale pending row is hygiene");
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        _pendingRegistrations
            .Setup(r => r.ConsumeByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => _consumer.ConsumeAsync("jane.doe@example.com", CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void EveryDoorThatCreatesAUser_ConsumesThePendingRegistration()
    {
        // Every handler that writes a Users row through CreateAsync must run
        // the consumer afterwards, in the same request. The completion step is
        // the one exception: it inserts and consumes inside one transaction
        // through CreateVerifiedAsync and must NOT also run the consumer.
        // Every .cs file in the Application layer, not only command handlers:
        // a door written as a service class must not be invisible. A door is
        // recognised by the TYPE it injects — any field declared IUserRepository
        // that is then asked to CreateAsync — not by the field's name.
        var application = Path.Combine(SolutionDirectory(), "Auth.Application");
        var doors = new List<string>();
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(application, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            var repositoryFields = Regex.Matches(source, @"IUserRepository\s+(?<field>\w+)\s*(;|=)")
                .Select(match => match.Groups["field"].Value)
                .Distinct()
                .ToList();

            foreach (var field in repositoryFields)
            {
                var create = source.IndexOf($"{field}.CreateAsync(", StringComparison.Ordinal);
                if (create < 0) continue;

                doors.Add(name);
                var consumerFields = Regex.Matches(source, @"IPendingRegistrationConsumer\s+(?<field>\w+)\s*(;|=)")
                    .Select(match => match.Groups["field"].Value)
                    .Distinct()
                    .ToList();
                var consume = consumerFields
                    .Select(consumer => source.IndexOf($"{consumer}.ConsumeAsync(", StringComparison.Ordinal))
                    .Where(index => index >= 0)
                    .DefaultIfEmpty(-1)
                    .Max();

                if (consume < 0)
                {
                    offenders.Add($"{name}: creates a user and never consumes the pending registration");
                }
                else if (consume < create)
                {
                    offenders.Add($"{name}: consumes before the Users row exists");
                }
            }

            if (source.Contains("CreateVerifiedAsync(", StringComparison.Ordinal)
                && source.Contains("IPendingRegistrationConsumer", StringComparison.Ordinal))
            {
                offenders.Add($"{name}: consumes twice — CreateVerifiedAsync already consumed in its transaction");
            }
        }

        doors.Should().BeEquivalentTo(
            ["ExternalLoginCommandHandler.cs", "RegisterWithInvitationCommandHandler.cs", "CreateUserCommandHandler.cs"],
            "these are the three doors beside the verify-first completion; a fourth must be added here deliberately, with its consumer");
        offenders.Should().BeEmpty();
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }
}
