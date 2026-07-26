---
name: documentation-standards
description: Load this skill when writing XML doc comments, creating README files, or documenting architectural decisions (ADRs). Covers code documentation format, README template, and Architecture Decision Record template.
user-invocable: true
---

# Documentation Standards

## Code Documentation

```csharp
/// <summary>
/// Authenticates a user and generates access tokens.
/// </summary>
/// <remarks>
/// This method performs the following steps:
/// 1. Validates the provided credentials
/// 2. Checks for account lockout
/// 3. Verifies two-factor authentication if enabled
/// 4. Generates JWT access and refresh tokens
/// 5. Records the login attempt for audit
/// </remarks>
/// <param name="request">The login credentials</param>
/// <param name="ipAddress">The client's IP address for audit logging</param>
/// <param name="cancellationToken">Token to cancel the operation</param>
/// <returns>Authentication result containing tokens and user info</returns>
/// <exception cref="AuthenticationException">
/// Thrown when credentials are invalid or account is locked
/// </exception>
/// <exception cref="TwoFactorRequiredException">
/// Thrown when 2FA verification is required
/// </exception>
/// <example>
/// <code>
/// var result = await authService.LoginAsync(
///     new LoginRequest { Email = "user@example.com", Password = "secret" },
///     "192.168.1.1",
///     cancellationToken);
/// </code>
/// </example>
public async Task<AuthResult> LoginAsync(
    LoginRequest request,
    string ipAddress,
    CancellationToken cancellationToken = default)
```

## README Template

```markdown
# [Project Name]

[One-paragraph description of what this project does]

## Quick Start

```bash
# Clone and install
git clone [repo-url]
cd [project-name]
[install commands]

# Run
[run command]
```

## Prerequisites

- [Requirement 1] (version X.X+)
- [Requirement 2] (version X.X+)

## Configuration

| Variable | Description | Default | Required |
|----------|-------------|---------|----------|
| `DATABASE_URL` | Connection string | - | Yes |
| `LOG_LEVEL` | Logging verbosity | `INFO` | No |

## Architecture

[Brief description with diagram if helpful]

## API Reference

[Link to API docs or brief overview]

## Development

```bash
# Run tests
[test command]

# Run linting
[lint command]

# Build for production
[build command]
```

## Deployment

[Deployment instructions or link to deployment guide]

## Contributing

[Contribution guidelines or link]

## License

[License type]
```

## Architecture Decision Record (ADR) Template

```markdown
# ADR [NUMBER]: [TITLE]

## Status
[Proposed | Accepted | Deprecated | Superseded by ADR-XXX]

## Context
[What is the issue that we're seeing that is motivating this decision?]

## Decision
[What is the change that we're proposing and/or doing?]

## Consequences

### Positive
- [Benefit 1]
- [Benefit 2]

### Negative
- [Drawback 1]
- [Drawback 2]

### Neutral
- [Side effect 1]

## Alternatives Considered

### Alternative A: [Name]
[Description, pros, cons, reason for rejection]

### Alternative B: [Name]
[Description, pros, cons, reason for rejection]

## References
- [Link 1]
- [Link 2]
```
