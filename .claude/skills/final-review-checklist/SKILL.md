---
name: final-review-checklist
description: Load this skill before marking any implementation as complete. Run through all 10 review sections: code quality, security, testing, frontend, backend, documentation, operational readiness, runtime stability, failure mode verification, and architectural principles compliance.
user-invocable: true
---

# Final Review Checklist

## Before Marking Implementation Complete

**Perform this comprehensive review:**

### 1. Code Quality Review
- [ ] All code follows SOLID principles
- [ ] DRY principle applied (no significant duplication)
- [ ] Consistent naming conventions throughout
- [ ] All public methods have XML documentation
- [ ] All async methods accept and propagate CancellationToken
- [ ] No hardcoded values (use configuration)
- [ ] Error handling is comprehensive

### 2. Security Review
- [ ] All inputs validated and sanitized
- [ ] Authentication implemented correctly
- [ ] Passwords and secrets hashed with Argon2id ONLY (no other algorithms)
- [ ] Authorization checks on all protected resources
- [ ] Sensitive data encrypted at rest and in transit
- [ ] No secrets in code or logs
- [ ] SQL injection prevention verified
- [ ] XSS prevention in place
- [ ] CSRF protection implemented

### 3. Testing Review
- [ ] Unit test coverage meets 90% minimum for ALL layers
- [ ] Integration tests passing
- [ ] Edge cases tested
- [ ] Error scenarios tested
- [ ] Performance tests for critical paths
- [ ] Coverage report generated and verified

### 4. Frontend Review (if applicable)
- [ ] Responsive design tested across devices
- [ ] Accessibility audit passed (WCAG 2.1 AA)
- [ ] Loading states implemented
- [ ] Error states handled gracefully
- [ ] Keyboard navigation works
- [ ] Performance metrics acceptable (LCP, FID, CLS)

### 5. Backend Review (if applicable)
- [ ] API follows REST conventions
- [ ] Error responses follow standard format
- [ ] Rate limiting configured
- [ ] Database queries optimized
- [ ] Caching strategy implemented
- [ ] Health checks operational

### 6. Documentation Review
- [ ] README complete and accurate
- [ ] API documentation up to date
- [ ] Architecture diagrams current
- [ ] Deployment guide tested
- [ ] Configuration documented

### 7. Operational Readiness
- [ ] Logging sufficient for debugging
- [ ] Monitoring and alerting configured
- [ ] Backup and recovery tested
- [ ] Rollback procedure documented
- [ ] Performance under load verified

### 8. Runtime Stability
- [ ] All code paths tested for runtime errors
- [ ] Null checks comprehensive
- [ ] Exception handling covers all scenarios
- [ ] No known memory leaks
- [ ] Resource cleanup verified

### 9. Failure Mode Verification
- [ ] Graceful degradation tested
- [ ] Circuit breakers configured
- [ ] Retry logic implemented
- [ ] Timeout values appropriate
- [ ] Fallback behaviors verified

### 10. Architectural Principles Compliance
- [ ] Clean Architecture: no inward dependency violations
- [ ] DDD: entities have behavior, no anemic models, value objects used where appropriate
- [ ] SOLID: each class single responsibility, interfaces segregated, dependencies inverted
- [ ] OOP: all four pillars enforced — encapsulation (private fields, controlled access, no public setters on domain entities, state changes through behavior methods), abstraction (complexity hidden behind interfaces, program to abstractions not implementations), inheritance (used judiciously, prefer composition, leverage base classes for shared behavior), polymorphism (interfaces and virtual methods for varying behavior, no if/switch on type — use polymorphic dispatch)
- [ ] CQRS: commands and queries separated, no mixed read/write handlers
- [ ] MediatR: endpoints use ISender, side effects use INotification, cross-cutting concerns use IPipelineBehavior
- [ ] ErrorOr: all handlers return ErrorOr<T>, no exceptions for business rules
- [ ] Strategy Pattern: no if/switch on type strings for polymorphic behavior, interface + factory pattern used
- [ ] Event-Driven: events are immutable records with clear contracts, handlers are idempotent, correct ordering maintained
- [ ] DRY: no duplicated logic across handlers or services, shared logic extracted into base classes/extensions/shared services

---

## Implementation Reminder

**Read this before EVERY implementation session:**

You are building software that real users will depend on. Every decision you make affects:

- **Security** of user data and systems
- **Reliability** that teams and businesses count on
- **User experience** that shapes perception
- **Maintainability** for future developers (including yourself)

**When in doubt:**

- **Security** over convenience
- **Correctness** over speed
- **Reliability** over features
- **Clarity** over cleverness
- **Explicit** over implicit

**Remember**: You are not just writing code—you are building trust. Every line of code is a promise to users that the system will work as expected, protect their data, and respect their time.

> **Think deeply, plan thoroughly, implement carefully, and validate relentlessly.**
