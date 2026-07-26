---
name: product-development
description: Load this skill when managing product requirements, writing user stories, planning sprints, prioritizing features, handling technical debt, communicating with stakeholders, or collaborating across design and engineering teams.
user-invocable: true
---

# Product Development Management

## Role Definition

As a Product Development Manager, you bridge the gap between business requirements and technical implementation. Your decisions affect:

- **User Experience**: How users interact with and perceive the product
- **Technical Architecture**: How the system is built and maintained
- **Team Velocity**: How efficiently the team can deliver
- **Business Outcomes**: How well the product meets its goals

## Strategic Thinking Framework

### 1. Feature Prioritization Matrix

```
┌─────────────────────────────────────────────────────────────┐
│              IMPACT vs EFFORT MATRIX                        │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  HIGH    │  Quick Wins       │  Major Projects             │
│  IMPACT  │  DO FIRST         │  PLAN CAREFULLY             │
│          │  ★★★★★            │  ★★★★☆                      │
│          │                   │                             │
│  ────────┼───────────────────┼─────────────────────────    │
│          │                   │                             │
│  LOW     │  Fill-ins         │  Avoid / Deprioritize       │
│  IMPACT  │  DO IF TIME       │  DON'T DO                   │
│          │  ★★☆☆☆            │  ★☆☆☆☆                      │
│          │                   │                             │
│          │     LOW EFFORT    │     HIGH EFFORT             │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 2. User Story Standards

```markdown
## User Story Template

### Title
[Action-oriented, specific title]

### User Story
As a [specific user type],
I want to [action/capability],
So that [business value/outcome].

### Acceptance Criteria
Given [precondition]
When [action]
Then [expected result]

### Technical Notes
- Dependencies: [list any blockers or dependencies]
- Risks: [known risks or unknowns]
- Estimations: [complexity points or time estimates]

### Out of Scope
- [Explicitly list what this story does NOT include]

### Definition of Done
- [ ] Acceptance criteria met
- [ ] Unit tests written and passing
- [ ] Integration tests passing
- [ ] Code reviewed
- [ ] Documentation updated
- [ ] Deployed to staging
- [ ] Product owner approved
```

### 3. Requirements Analysis Checklist

Before approving any requirement, verify:

```
┌─────────────────────────────────────────────────────────────┐
│              REQUIREMENTS CHECKLIST                         │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  CLARITY                                                    │
│  □ Is the requirement specific and unambiguous?             │
│  □ Are all terms defined?                                   │
│  □ Are acceptance criteria measurable?                      │
│                                                             │
│  COMPLETENESS                                               │
│  □ Are all user scenarios covered?                          │
│  □ Are error cases defined?                                 │
│  □ Are edge cases considered?                               │
│  □ Are performance requirements specified?                  │
│                                                             │
│  CONSISTENCY                                                │
│  □ Does it align with existing features?                    │
│  □ Does it follow established patterns?                     │
│  □ Are there conflicts with other requirements?             │
│                                                             │
│  FEASIBILITY                                                │
│  □ Is it technically achievable?                            │
│  □ Is the timeline realistic?                               │
│  □ Are resources available?                                 │
│                                                             │
│  VALUE                                                      │
│  □ Does it solve a real user problem?                       │
│  □ Is the ROI justified?                                    │
│  □ Does it align with product strategy?                     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 4. Sprint Planning Guidelines

```markdown
## Sprint Planning Checklist

### Before Sprint
- [ ] Backlog is groomed and prioritized
- [ ] Top items have clear acceptance criteria
- [ ] Dependencies are identified
- [ ] Risks are documented
- [ ] Capacity is calculated (accounting for meetings, PTO, etc.)

### During Planning
- [ ] Team understands sprint goal
- [ ] Stories are broken down to <3 days of work
- [ ] Technical approach is discussed
- [ ] Assumptions are validated
- [ ] Commitment is realistic (70-80% capacity)

### Sprint Goal Template
"By the end of this sprint, users will be able to [capability],
which enables [business outcome]. We will know we succeeded when [metric]."
```

### 5. Technical Debt Management

```
┌─────────────────────────────────────────────────────────────┐
│              TECHNICAL DEBT QUADRANT                        │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│              │  DELIBERATE         │  INADVERTENT          │
│  ────────────┼─────────────────────┼─────────────────────  │
│  PRUDENT     │  "We must ship      │  "Now we know how     │
│              │  now and deal with  │  we should have       │
│              │  consequences"      │  done it"             │
│              │  → Document & plan  │  → Refactor when      │
│              │    to address       │    touching code      │
│  ────────────┼─────────────────────┼─────────────────────  │
│  RECKLESS    │  "We don't have     │  "What's layered      │
│              │  time for design"   │  architecture?"       │
│              │  → AVOID - high     │  → Training needed    │
│              │    future cost      │    before more work   │
│                                                             │
└─────────────────────────────────────────────────────────────┘

Technical Debt Budget: Allocate 15-20% of sprint capacity to
addressing technical debt. Track and report on debt reduction.
```

### 6. Stakeholder Communication

```markdown
## Status Report Template

### Executive Summary
[2-3 sentences: What's the headline?]

### Progress
| Milestone | Status | Target Date | Notes |
|-----------|--------|-------------|-------|
| Phase 1   | ✅ Done | 2024-01-15 | Completed on time |
| Phase 2   | 🟡 At Risk | 2024-02-01 | Blocked by API dependency |
| Phase 3   | ⬜ Not Started | 2024-02-15 | - |

### Key Metrics
- Velocity: [X] points/sprint (target: [Y])
- Bug Escape Rate: [X]% (target: <[Y]%)
- Customer Satisfaction: [X]/5 (target: >[Y])

### Risks & Blockers
| Risk | Impact | Mitigation | Owner |
|------|--------|------------|-------|
| [Description] | High | [Action] | [Name] |

### Decisions Needed
1. [Decision]: [Options A vs B, Recommendation]

### Next Steps
- [Action item]: [Owner] by [Date]
```

## Cross-Functional Collaboration

### Working with Design

```
┌─────────────────────────────────────────────────────────────┐
│              DESIGN HANDOFF CHECKLIST                       │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  □ All states defined (default, hover, active, disabled)    │
│  □ Error states and messages specified                      │
│  □ Loading states designed                                  │
│  □ Empty states covered                                     │
│  □ Responsive breakpoints defined                           │
│  □ Animation specifications included                        │
│  □ Accessibility requirements noted                         │
│  □ Edge cases addressed (long text, missing data, etc.)     │
│  □ Design tokens/variables documented                       │
│  □ Assets exported in correct formats                       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Working with Engineering

```
┌─────────────────────────────────────────────────────────────┐
│              TECHNICAL REVIEW CHECKLIST                     │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  □ Requirements are technically feasible                    │
│  □ Edge cases are identified                                │
│  □ Performance implications discussed                       │
│  □ Security considerations addressed                        │
│  □ Data migration needs identified (if applicable)          │
│  □ API contracts agreed upon                                │
│  □ Testing strategy defined                                 │
│  □ Rollback plan exists                                     │
│  □ Monitoring/alerting planned                              │
│  □ Documentation needs identified                           │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```
