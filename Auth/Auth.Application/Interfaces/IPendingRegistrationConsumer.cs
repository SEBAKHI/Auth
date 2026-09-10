namespace Auth.Application.Interfaces;

/// <summary>
/// One rule, written once: every path that brings a Users row into existence
/// for an address consumes any verify-first registration pending for that
/// address, in the same request. Google sign-in, an invitation, an
/// administrator creating the account — each is its own proof of the address
/// (or its own policy), and once the account exists the pending row's code
/// must not be able to create a second one. The completion step needs no
/// call here: it consumes the row inside the transaction that inserts the
/// account.
/// </summary>
public interface IPendingRegistrationConsumer
{
    /// <summary>
    /// Stamps the address's pending row consumed, if there is one. Best
    /// effort: a failure is logged and never fails the request that just
    /// created the account — the unique index on Users is what makes a second
    /// account impossible, this only keeps the pending table honest.
    /// </summary>
    /// <param name="email">The address the account was created for, in any case.</param>
    Task ConsumeAsync(string email, CancellationToken cancellationToken);
}
