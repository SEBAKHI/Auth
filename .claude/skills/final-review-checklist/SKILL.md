---
name: final-review-checklist
description: "Load this skill before marking any implementation as complete. Run through all 10 review sections — code quality, security, testing, frontend, backend, documentation, operational readiness, runtime stability, failure mode verification, and architectural principles compliance (section 10 is C#/.NET only)."
user-invocable: true
---

# Final Review Checklist

## Before Marking Implementation Complete

**Perform this comprehensive review.** Where an item names a skill, that skill owns the rule; check against the owner's text.

### 1. Code Quality Review
- [ ] All code follows SOLID principles
- [ ] No duplicated knowledge (C#/.NET: P10 of `/dotnet-architecture`)
- [ ] Consistent naming conventions throughout
- [ ] Public APIs documented in the language's doc-comment format (C#: XML docs)
- [ ] Async operations accept and propagate a cancellation signal wherever the language provides one (C#/.NET: `/backend-development` → Async and Cancellation)
- [ ] No hardcoded environment-specific or operator-tunable values: they come from configuration. This item does not cover fixed domain constants (C#/.NET: where they live is P10). No unnamed magic literals.
- [ ] Error handling meets the Section 8 exception-handling item

### 2. Security Review
- [ ] Inputs validated server-side and injection prevented per `/security-mindset` → Input Validation, Injection
- [ ] Authentication meets `/security-mindset` → Authentication
- [ ] Secrets and credentials handled per `/security-mindset` → Secrets and Credential Storage
- [ ] Authorization checks on all protected resources (`/security-mindset` → Authorization)
- [ ] Data protected at rest and in transit per `/security-mindset` → Data Protection
- [ ] Cookies, tokens and CSRF per `/security-mindset` → Cookies, Tokens and CSRF
- [ ] Logs and error responses disclose nothing sensitive per `/security-mindset` → Logging Content, Error Disclosure

### 3. Testing Review
- [ ] Coverage gate met (`/quality-assurance` → Coverage Gate); coverage report generated and verified
- [ ] Integration tests follow `/quality-assurance` → Integration Tests, and pass
- [ ] Edge cases tested
- [ ] Error scenarios tested
- [ ] Performance tests for critical paths

### 4. Frontend Review (if applicable)
- [ ] Responsive design tested across devices
- [ ] Accessibility audit passed (WCAG 2.2 AA)
- [ ] Loading states implemented
- [ ] Error states handled gracefully (`/frontend-playbook` → rules/api-and-state.md)
- [ ] Keyboard navigation works
- [ ] Performance metrics acceptable (Core Web Vitals: LCP, INP, CLS)

### 5. Backend Review (if applicable)
- [ ] API follows REST conventions (C#/.NET: `/backend-development` → API Design)
- [ ] Every 4xx and 5xx response follows the project's single error contract, with a machine-readable code always present, framework-generated errors included (C#/.NET: `/backend-development` → Response Format). Verified by triggering each path and comparing the bodies
- [ ] Rate limiting per `/security-mindset` → Rate Limiting
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
- [ ] Exception handling follows `/failure-mode-design` → Exception-Handling Boundaries
- [ ] No known memory leaks
- [ ] Resource cleanup verified

### 9. Failure Mode Verification
- [ ] Every dependency has the failure response that `/failure-mode-design` → Failure Responses defines, and security controls fail closed (Classify Every Dependency Use)
- [ ] Retries, timeouts and circuit breakers follow `/failure-mode-design` → Retries, Timeouts and Circuit Breakers
- [ ] Graceful degradation tested (`/failure-mode-design` → Graceful Degradation)
- [ ] Fallback behaviors verified

### 10. Architectural Principles Compliance (C#/.NET only — skip entirely for other stacks)

Open `/dotnet-architecture` and read its scope note. For each principle, confirm every clause of that row's Enforcement Rule that the scope note makes applicable to this project, reading the policy table itself, not a copy or paraphrase. Any unmet applicable clause blocks completion.

- [ ] P1 Clean Architecture
- [ ] P2 Domain-Driven Design
- [ ] P3 SOLID
- [ ] P4 OOP Fundamentals
- [ ] P5 CQRS
- [ ] P6 MediatR Pipeline
- [ ] P7 ErrorOr Pattern
- [ ] P8 Strategy Pattern
- [ ] P9 Event-Driven Architecture
- [ ] P10 DRY
- [ ] Notification handler naming (`/event-driven-architecture` §4)

---

Trade-off priorities: see `core-agent-behavior`.
