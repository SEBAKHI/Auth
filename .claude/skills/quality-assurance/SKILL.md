---
name: quality-assurance
description: Load this skill when writing tests, reviewing test coverage, setting up testing strategies, or ensuring code quality. Covers the testing pyramid, unit/integration/e2e test standards, naming conventions, and mandatory 90% coverage requirements.
user-invocable: true
---

# Quality Assurance

## Testing Pyramid

```
┌─────────────────────────────────────────────────────────────┐
│                    TESTING PYRAMID                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│                         /\                                  │
│                        /  \                                 │
│                       / E2E\        Few, slow, expensive    │
│                      /______\                               │
│                     /        \                              │
│                    /Integration\   Some, medium speed       │
│                   /______________\                          │
│                  /                \                         │
│                 /    Unit Tests    \  Many, fast, cheap     │
│                /____________________\                       │
│                                                             │
│  Recommended Ratios:                                        │
│  • Unit Tests: 70%                                          │
│  • Integration Tests: 20%                                   │
│  • E2E Tests: 10%                                          │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Test Quality Standards

```csharp
// ✅ GOOD: Clear, focused, well-named test
[Fact]
public async Task CreateUser_WithValidData_ReturnsCreatedUser()
{
    // Arrange
    var request = new CreateUserRequest
    {
        Email = "test@example.com",
        Name = "Test User"
    };

    _mockRepository
        .Setup(r => r.GetByEmailAsync(request.Email, It.IsAny<CancellationToken>()))
        .ReturnsAsync((User?)null);

    // Act
    var result = await _sut.CreateUserAsync(request, CancellationToken.None);

    // Assert
    result.Should().NotBeNull();
    result.Email.Should().Be(request.Email);
    result.Name.Should().Be(request.Name);
    _mockRepository.Verify(
        r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
        Times.Once);
}

[Fact]
public async Task CreateUser_WithDuplicateEmail_ThrowsDuplicateEmailException()
{
    // Arrange
    var existingUser = new User("test@example.com", "Existing User");
    _mockRepository
        .Setup(r => r.GetByEmailAsync("test@example.com", It.IsAny<CancellationToken>()))
        .ReturnsAsync(existingUser);

    var request = new CreateUserRequest { Email = "test@example.com", Name = "New User" };

    // Act
    var act = () => _sut.CreateUserAsync(request, CancellationToken.None);

    // Assert
    await act.Should().ThrowAsync<DuplicateEmailException>()
        .WithMessage("*test@example.com*");
}

// Test Naming Convention
// Pattern: [Method]_[Scenario]_[ExpectedBehavior]
// Examples:
// - CreateUser_WithValidData_ReturnsCreatedUser
// - CreateUser_WithDuplicateEmail_ThrowsDuplicateEmailException
// - GetUser_WithNonExistentId_ReturnsNull
// - Login_WithCorrectCredentials_ReturnsToken
// - Login_WithWrongPassword_ThrowsAuthenticationException
```

## Code Coverage Requirements

**Minimum 90% code coverage is MANDATORY for ALL layers.**

```
┌─────────────────────────────────────────────────────────────┐
│              CODE COVERAGE REQUIREMENTS                     │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Layer              │ Minimum Coverage │ Focus Areas        │
│  ───────────────────┼──────────────────┼─────────────────── │
│  Domain             │      90%         │ Business logic,    │
│                     │                  │ validations,       │
│                     │                  │ calculations       │
│  ───────────────────┼──────────────────┼─────────────────── │
│  Application        │      90%         │ Use cases,         │
│                     │                  │ service            │
│                     │                  │ orchestration      │
│  ───────────────────┼──────────────────┼─────────────────── │
│  Infrastructure     │      90%         │ Repository         │
│                     │                  │ implementations,   │
│                     │                  │ integrations       │
│  ───────────────────┼──────────────────┼─────────────────── │
│  Presentation       │      90%         │ Controllers,       │
│                     │                  │ input validation,  │
│                     │                  │ error handling     │
│  ───────────────────┼──────────────────┼─────────────────── │
│  Frontend           │      90%         │ Components,        │
│  (if applicable)    │                  │ hooks, utilities,  │
│                     │                  │ state management   │
│                                                             │
│  NO EXCEPTIONS. If coverage drops below 90%, the build     │
│  should fail and deployment must be blocked.               │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**What to Test:**
- All public methods
- All code branches (if/else, switch)
- All edge cases and boundary conditions
- Error handling paths
- Integration points

**Coverage Exclusions (if justified and documented):**
- Auto-generated code
- DTOs/POCOs with no logic
- Startup/configuration boilerplate
- Third-party library wrappers (test integration instead)
