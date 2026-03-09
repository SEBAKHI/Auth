---
name: implementation-strategy
description: Load this skill before writing ANY code. Use it to plan architecture, identify risks, define component approaches, and set acceptance criteria. Invoke whenever starting a new feature, major change, or when asked to plan before implementing.
user-invocable: true
---

# Implementation Strategy

## MANDATORY: Plan Before Coding

**Before writing ANY code**, you MUST complete these steps:

## Step 1: Architecture Map

Create a visual or textual representation showing:

```
┌─────────────────────────────────────────────────────────────┐
│                    ARCHITECTURE MAP                         │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Components:           Dependencies:        Data Flow:      │
│  ┌─────────┐           A ──depends──► B    User ──► API    │
│  │ Service │           B ──depends──► C         ──► DB     │
│  │    A    │           C ──depends──► D         ──► Cache  │
│  └─────────┘                                                │
│                                                             │
│  Implementation Order:                                      │
│  1. Foundation (D, C)                                       │
│  2. Core Logic (B)                                          │
│  3. Interface (A)                                           │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Include:**
- All major components and their responsibilities
- Dependencies between components
- External integrations
- Data flow paths
- Suggested implementation order

## Step 2: Risk Identification

Identify the **top 5 risks** most likely to cause problems:

| Risk | Likelihood | Impact | Mitigation Strategy |
|------|------------|--------|---------------------|
| Example: Database schema changes mid-project | Medium | High | Design flexible schema, use migrations |
| Example: Third-party API rate limits | High | Medium | Implement caching, queue requests |
| ... | ... | ... | ... |

**Consider risks in these categories:**
- Security vulnerabilities
- Performance bottlenecks
- Integration failures
- Data consistency issues
- Deployment complications
- User experience problems
- Scalability limitations

## Step 3: Approach Definition

For each major component, document:

```markdown
## Component: [Name]

### What
[Brief description of what it does]

### Why This Approach
[Reasoning for chosen approach over alternatives]

### Alternatives Considered
1. [Alternative A] - Rejected because [reason]
2. [Alternative B] - Rejected because [reason]

### Trade-offs Accepted
- [Trade-off 1]: Accepting [downside] in exchange for [benefit]

### Dependencies
- Requires: [list]
- Required by: [list]
```

## Step 4: Checkpoint Plan

Define what "done" looks like for each phase:

```markdown
## Phase [N]: [Name]

### Scope
[What will be built in this phase]

### Acceptance Criteria
- [ ] Criterion 1: [Specific, measurable outcome]
- [ ] Criterion 2: [Specific, measurable outcome]
- [ ] Criterion 3: [Specific, measurable outcome]

### Test Scenarios
1. [Scenario]: Expected [outcome]
2. [Scenario]: Expected [outcome]

### Integration Points
- Connects to: [Phase N-1 components]
- Enables: [Phase N+1 components]

### Definition of Done
- [ ] All acceptance criteria met
- [ ] Tests passing
- [ ] Documentation updated
- [ ] Code reviewed
- [ ] No known bugs
```

**Present this plan and wait for approval before proceeding with implementation.**
