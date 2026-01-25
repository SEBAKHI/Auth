# AI Agent Programming Instructions
## Universal Guidelines for Software Development Excellence

> **Think deeply, plan thoroughly, implement carefully, and validate relentlessly—you have the full capability to build production-grade systems, so use your complete reasoning ability on every decision, no matter how small.**

---
Each time you read the file, type the following words: "I am reading the instructions contained in file [file name] from folder [folder path]"
---

## Table of Contents

1. [Core Agent Behavior](#core-agent-behavior)
2. [Implementation Strategy](#implementation-strategy)
3. [Frontend Development](#frontend-development)
4. [Backend Development](#backend-development)
5. [Product Development Management](#product-development-management)
6. [Security Mindset](#security-mindset)
7. [Failure Mode Design](#failure-mode-design)
8. [Quality Assurance](#quality-assurance)
9. [Documentation Standards](#documentation-standards)
10. [Final Review Checklist](#final-review-checklist)

---

## Core Agent Behavior

### Primary Directive

You are implementing production systems that real users will depend on. Your code will run in environments where failures have consequences—financial, reputational, and operational.

**Before coding**: Plan the architecture and identify dependencies.
**While coding**: Think about security, failures, and edge cases for every line.
**After coding**: Validate against requirements and test your assumptions.

### Agent Autonomy and Communication

You have permission and are **strongly encouraged** to:

| Action | When to Do It |
|--------|---------------|
| **Challenge requirements** | When you see conflicts, inefficiencies, better approaches, or potential issues |
| **Ask clarifying questions** | Before implementing anything ambiguous—never assume |
| **Propose improvements** | When you identify opportunities for better security, performance, UX, or maintainability |
| **Stop and report** | When you discover that previous work needs revision based on new understanding |
| **Think out loud** | For complex decisions—show your reasoning process |
| **Raise concerns** | When something feels wrong, risky, or unclear |

### Communication Rules

```
✅ DO: "I notice the requirement says X, but this might conflict with Y. 
       Should I proceed with X, or would you prefer Z approach which addresses both?"

✅ DO: "Before I implement this, I want to confirm: Are we optimizing for 
       performance or readability here? The approaches differ significantly."

✅ DO: "I've completed Phase 1. Here's what's working, what I'm uncertain about, 
       and what I recommend for Phase 2."

❌ DON'T: Silently make assumptions about ambiguous requirements
❌ DON'T: Hide uncertainty behind generic implementations  
❌ DON'T: Proceed when something feels wrong without raising it
❌ DON'T: Skip validation steps to move faster
```

### Quality Principles

When facing trade-offs, prioritize in this order:

1. **Security** over convenience
2. **Correctness** over speed
3. **Reliability** over features
4. **Clarity** over cleverness
5. **Explicit** over implicit
6. **Tested** over assumed
7. **Documented** over tribal knowledge

---

## Implementation Strategy

### MANDATORY: Plan Before Coding

**Before writing ANY code**, you MUST complete these steps:

### Step 1: Architecture Map

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

### Step 2: Risk Identification

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

### Step 3: Approach Definition

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

### Step 4: Checkpoint Plan

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

---

## Frontend Development

### Design Philosophy: Beauty with Trust

Great frontend development creates interfaces that are both **visually stunning** and **trustworthy**. Users should feel confident, delighted, and in control.

### Visual Design Principles

#### 1. Brand Identity Compliance (MANDATORY)

**Before creating ANY visual element, you MUST:**

```
┌────────────────────────────────────────────────────────┐
│  BRAND IDENTITY CHECKLIST                              │
├────────────────────────────────────────────────────────┤
│                                                        │
│  □ Follow company logo guidelines (spacing, placement) │
│  □ Use ONLY the font families declared in the project  │
│  □ Apply brand color palette consistently              │
│  □ Respect logo minimum size and clear space rules     │
│  □ Use approved icon style (outline, filled, etc.)     │
│  □ Match brand tone (formal, friendly, technical)      │
│  □ Follow existing design system if available          │
│                                                        │
│  FONT USAGE:                                           │
│  • Primary font: [Use project's declared primary]      │
│  • Secondary font: [Use project's declared secondary]  │
│  • Monospace: [Use project's declared code font]       │
│  • NEVER introduce new fonts without approval          │
│                                                        │
│  If brand guidelines are not provided, ASK before      │
│  making assumptions about visual identity.             │
│                                                        │
└────────────────────────────────────────────────────────┘
```

#### 2. Hierarchy and Clarity

```
┌────────────────────────────────────────────────────────┐
│  VISUAL HIERARCHY CHECKLIST                            │
├────────────────────────────────────────────────────────┤
│                                                        │
│  □ Primary action is immediately obvious               │
│  □ Information flows logically (F-pattern or Z-pattern)│
│  □ White space guides the eye, not clutters           │
│  □ Typography scale creates clear hierarchy            │
│  □ Color draws attention to what matters              │
│  □ Icons support text, never replace it ambiguously   │
│                                                        │
└────────────────────────────────────────────────────────┘
```

#### 2. Trust Signals

Users trust interfaces that feel:

| Quality | How to Achieve It |
|---------|-------------------|
| **Professional** | Consistent spacing, aligned elements, polished typography |
| **Stable** | Smooth animations, no layout shifts, predictable behavior |
| **Secure** | Clear feedback, confirmation dialogs, visible security indicators |
| **Responsive** | Immediate feedback, loading states, progress indicators |
| **Accessible** | Works for everyone, regardless of ability or device |

### User Experience (UX) Principles

#### 1. UX Design Fundamentals

**Every interface decision should be grounded in UX principles:**

```
┌─────────────────────────────────────────────────────────────┐
│              UX DESIGN PRINCIPLES                           │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  USABILITY                                                  │
│  □ Learnability: New users can accomplish tasks quickly     │
│  □ Efficiency: Experienced users can work rapidly           │
│  □ Memorability: Returning users can re-establish proficiency│
│  □ Error Prevention: Design minimizes user mistakes         │
│  □ Error Recovery: Easy to recover from errors              │
│  □ Satisfaction: Pleasant and engaging to use               │
│                                                             │
│  COGNITIVE LOAD                                             │
│  □ Minimize choices at each step (Hick's Law)               │
│  □ Group related items together (Gestalt principles)        │
│  □ Use progressive disclosure for complex features          │
│  □ Maintain consistency to reduce learning curve            │
│  □ Provide clear feedback for every action                  │
│                                                             │
│  USER FLOW                                                  │
│  □ Primary tasks require minimum steps                      │
│  □ Navigation is intuitive and predictable                  │
│  □ Users always know where they are                         │
│  □ Back/undo is always available                            │
│  □ Progress indicators for multi-step processes             │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

#### 2. UX Research Questions (Ask Before Designing)

Before implementing any UI, consider:

| Question | Why It Matters |
|----------|----------------|
| **Who is the user?** | Different users have different needs, technical levels, and contexts |
| **What is their goal?** | The UI should optimize for the primary task |
| **What context are they in?** | Mobile on-the-go vs. desktop focused work changes everything |
| **What do they already know?** | Leverage existing mental models |
| **What might go wrong?** | Anticipate errors and edge cases |
| **How will they feel?** | Emotional design impacts engagement and trust |

#### 3. UX Heuristics Checklist (Nielsen's 10)

```markdown
□ 1. Visibility of system status
     → Always keep users informed about what's happening

□ 2. Match between system and real world
     → Use familiar language and concepts

□ 3. User control and freedom
     → Provide undo, redo, cancel, and escape routes

□ 4. Consistency and standards
     → Follow platform conventions and internal patterns

□ 5. Error prevention
     → Prevent problems before they occur

□ 6. Recognition rather than recall
     → Make options visible, don't rely on memory

□ 7. Flexibility and efficiency of use
     → Provide shortcuts for expert users

□ 8. Aesthetic and minimalist design
     → Remove unnecessary elements

□ 9. Help users recognize, diagnose, and recover from errors
     → Clear error messages with solutions

□ 10. Help and documentation
      → Provide searchable, task-focused help
```

#### 4. Interaction Design Patterns

```
┌─────────────────────────────────────────────────────────────┐
│              INTERACTION PATTERNS                           │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  FORMS                                                      │
│  • Label above or beside input (never placeholder only)     │
│  • Inline validation with helpful messages                  │
│  • Smart defaults to reduce input                           │
│  • Logical tab order                                        │
│  • Clear required vs optional indicators                    │
│  • Autofocus on first field                                 │
│                                                             │
│  NAVIGATION                                                 │
│  • Maximum 7±2 top-level items                              │
│  • Current location always visible                          │
│  • Breadcrumbs for deep hierarchies                         │
│  • Search available for large content sets                  │
│                                                             │
│  DATA DISPLAY                                               │
│  • Tables for comparison, cards for browsing                │
│  • Sorting and filtering for large datasets                 │
│  • Pagination or infinite scroll (choose based on context)  │
│  • Empty states with guidance                               │
│                                                             │
│  FEEDBACK                                                   │
│  • Immediate response to actions (<100ms)                   │
│  • Loading indicators for longer operations                 │
│  • Success confirmations for important actions              │
│  • Toast/snackbar for non-blocking notifications            │
│  • Modal for critical confirmations only                    │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### UI Tool Selection (CRITICAL)

**When asked to design or implement a UI, THINK DEEPLY before choosing a tool or approach.**

```
┌─────────────────────────────────────────────────────────────┐
│              UI TOOL SELECTION DECISION TREE                │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  What is the PRIMARY purpose of this UI?                    │
│                                                             │
│  ├── Data Entry / Forms                                     │
│  │   → Consider: Form libraries, validation frameworks      │
│  │   → Prioritize: Accessibility, error handling            │
│  │                                                          │
│  ├── Data Display / Tables                                  │
│  │   → Consider: Data grid libraries, virtualization        │
│  │   → Prioritize: Performance, sorting, filtering          │
│  │                                                          │
│  ├── Data Visualization / Charts                            │
│  │   → Consider: D3.js, Chart.js, Recharts, Plotly          │
│  │   → Prioritize: Interactivity, responsiveness            │
│  │                                                          │
│  ├── Rich Text / Content Editing                            │
│  │   → Consider: Slate, TipTap, Quill, ProseMirror          │
│  │   → Prioritize: Feature set, output format               │
│  │                                                          │
│  ├── File Upload / Media                                    │
│  │   → Consider: Dropzone, Uppy, FilePond                   │
│  │   → Prioritize: UX, progress, validation                 │
│  │                                                          │
│  ├── Real-time / Collaborative                              │
│  │   → Consider: WebSocket libraries, CRDT frameworks       │
│  │   → Prioritize: Conflict resolution, latency             │
│  │                                                          │
│  └── Complex State / Workflows                              │
│      → Consider: State machines (XState), wizards           │
│      → Prioritize: Clarity, error recovery                  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Before Selecting ANY UI Tool:**

1. **Analyze the requirement deeply** - What problem are we really solving?
2. **Consider the user** - What will provide the best experience for them?
3. **Evaluate options** - List at least 2-3 alternatives
4. **Check constraints** - Bundle size, browser support, accessibility, licensing
5. **Think long-term** - Maintenance, community support, learning curve
6. **Justify your choice** - Document WHY this tool over alternatives

```markdown
## UI Tool Selection Template

### Requirement
[What UI capability is needed?]

### User Context
[Who will use this? What's their technical level? What device/context?]

### Options Considered

| Tool | Pros | Cons | Bundle Size |
|------|------|------|-------------|
| [A]  | ...  | ...  | XX KB       |
| [B]  | ...  | ...  | XX KB       |
| [C]  | ...  | ...  | XX KB       |

### Selected Tool: [Name]

### Justification
[Why this tool provides the best UX for this specific use case]
```

#### 4. Color Psychology

```
┌─────────────────────────────────────────────────────────────┐
│  COLOR USAGE GUIDE                                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Primary Action    → Brand color (prominent, confident)     │
│  Secondary Action  → Muted/outline (important but quieter)  │
│  Destructive       → Red family (danger, careful)           │
│  Success           → Green family (confirmation, positive)  │
│  Warning           → Amber/Yellow (attention, caution)      │
│  Information       → Blue family (neutral, informative)     │
│  Disabled          → Gray (unavailable, inactive)           │
│                                                             │
│  Background        → Light/Dark based on context            │
│  Text              → High contrast for readability          │
│  Borders           → Subtle, define space without distract  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

#### 4. Motion and Animation

```javascript
// Animation Principles

// ✅ DO: Purposeful animation that provides feedback
button.addEventListener('click', () => {
  button.classList.add('scale-95');  // Immediate tactile feedback
  setTimeout(() => button.classList.remove('scale-95'), 100);
});

// ✅ DO: Smooth transitions that guide attention
.modal-enter {
  opacity: 0;
  transform: scale(0.95);
  transition: all 200ms ease-out;
}

// ❌ DON'T: Animation for animation's sake
.logo {
  animation: spin 2s infinite;  // Distracting, purposeless
}

// ❌ DON'T: Slow animations that impede workflow
.dropdown {
  transition: all 800ms;  // Too slow, feels sluggish
}
```

**Animation Timing Guidelines:**
- **Micro-interactions**: 100-200ms (buttons, toggles, hover states)
- **Small transitions**: 200-300ms (dropdowns, tooltips, tabs)
- **Medium transitions**: 300-400ms (modals, sidebars, cards)
- **Large transitions**: 400-500ms (page transitions, complex reveals)

### Component Architecture

#### 1. Component Design Principles

```typescript
// ✅ GOOD: Single responsibility, composable, typed
interface ButtonProps {
  variant: 'primary' | 'secondary' | 'danger' | 'ghost';
  size: 'sm' | 'md' | 'lg';
  isLoading?: boolean;
  isDisabled?: boolean;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
  children: React.ReactNode;
  onClick?: (event: React.MouseEvent<HTMLButtonElement>) => void;
}

export const Button: React.FC<ButtonProps> = ({
  variant = 'primary',
  size = 'md',
  isLoading = false,
  isDisabled = false,
  leftIcon,
  rightIcon,
  children,
  onClick,
  ...props
}) => {
  // Implementation with proper loading states, 
  // accessibility attributes, and visual feedback
};
```

#### 2. State Management Philosophy

```
┌─────────────────────────────────────────────────────────────┐
│  STATE LOCATION DECISION TREE                               │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Is this state used by multiple components?                 │
│  ├── NO  → Local component state (useState)                 │
│  └── YES → Continue...                                      │
│                                                             │
│  Are the components in the same subtree?                    │
│  ├── YES → Lift state to common ancestor                    │
│  └── NO  → Continue...                                      │
│                                                             │
│  Is this server data that needs caching/syncing?            │
│  ├── YES → Server state library (React Query, SWR)          │
│  └── NO  → Continue...                                      │
│                                                             │
│  Is this global app state?                                  │
│  ├── YES → Global state (Context, Zustand, Redux)           │
│  └── NO  → Re-evaluate, probably local state                │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

#### 3. Performance Optimization

```typescript
// ✅ Lazy loading for route-based code splitting
const Dashboard = lazy(() => import('./pages/Dashboard'));
const Settings = lazy(() => import('./pages/Settings'));

// ✅ Memoization for expensive computations
const sortedItems = useMemo(() => 
  items.sort((a, b) => a.name.localeCompare(b.name)),
  [items]
);

// ✅ Virtualization for long lists
import { FixedSizeList } from 'react-window';
<FixedSizeList
  height={400}
  itemCount={10000}
  itemSize={35}
>
  {Row}
</FixedSizeList>

// ✅ Image optimization
<img
  src={thumbnailUrl}
  srcSet={`${smallUrl} 400w, ${mediumUrl} 800w, ${largeUrl} 1200w`}
  sizes="(max-width: 600px) 400px, (max-width: 1200px) 800px, 1200px"
  loading="lazy"
  alt="Descriptive alt text"
/>
```

### Accessibility (A11y) Requirements

**Every frontend implementation MUST include:**

```typescript
// ✅ Semantic HTML
<nav aria-label="Main navigation">
  <ul role="menubar">
    <li role="none">
      <a role="menuitem" href="/dashboard">Dashboard</a>
    </li>
  </ul>
</nav>

// ✅ Keyboard navigation
const handleKeyDown = (e: KeyboardEvent) => {
  switch (e.key) {
    case 'Enter':
    case ' ':
      handleSelect();
      break;
    case 'Escape':
      handleClose();
      break;
    case 'ArrowDown':
      focusNext();
      break;
    case 'ArrowUp':
      focusPrevious();
      break;
  }
};

// ✅ Focus management
useEffect(() => {
  if (isOpen) {
    modalRef.current?.focus();
    trapFocus(modalRef.current);
  }
  return () => releaseFocus();
}, [isOpen]);

// ✅ Screen reader announcements
<div role="status" aria-live="polite" aria-atomic="true">
  {statusMessage}
</div>
```

**Accessibility Checklist:**
- [ ] All interactive elements are keyboard accessible
- [ ] Focus states are visible and logical
- [ ] Color is not the only means of conveying information
- [ ] Text has sufficient contrast (4.5:1 for normal, 3:1 for large)
- [ ] Images have appropriate alt text
- [ ] Forms have associated labels
- [ ] Error messages are announced to screen readers
- [ ] Page has proper heading hierarchy
- [ ] Skip links are available for keyboard users

### Responsive Design

```css
/* Mobile-first breakpoint system */
:root {
  /* Breakpoints */
  --bp-sm: 640px;   /* Small devices */
  --bp-md: 768px;   /* Medium devices */
  --bp-lg: 1024px;  /* Large devices */
  --bp-xl: 1280px;  /* Extra large devices */
  --bp-2xl: 1536px; /* 2X large devices */
}

/* Container queries for component-level responsiveness */
.card-container {
  container-type: inline-size;
}

@container (min-width: 400px) {
  .card {
    flex-direction: row;
  }
}
```

### Error Handling & User Feedback

```typescript
// ✅ Comprehensive error boundary
class ErrorBoundary extends React.Component<Props, State> {
  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    // Log to error reporting service
    errorReportingService.log(error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return (
        <ErrorFallback 
          error={this.state.error}
          resetError={() => this.setState({ hasError: false })}
        />
      );
    }
    return this.props.children;
  }
}

// ✅ User-friendly error messages
const errorMessages: Record<string, string> = {
  'NETWORK_ERROR': "We couldn't connect to the server. Please check your internet connection.",
  'UNAUTHORIZED': "Your session has expired. Please log in again.",
  'NOT_FOUND': "We couldn't find what you were looking for.",
  'VALIDATION_ERROR': "Please check your input and try again.",
  'DEFAULT': "Something went wrong. Please try again later."
};

// ✅ Loading states that build trust
<Button isLoading={isSubmitting}>
  {isSubmitting ? 'Saving...' : 'Save Changes'}
</Button>

// ✅ Optimistic updates with rollback
const optimisticUpdate = async () => {
  const previousData = queryClient.getQueryData(['items']);
  queryClient.setQueryData(['items'], newData); // Optimistic
  
  try {
    await api.updateItems(newData);
  } catch (error) {
    queryClient.setQueryData(['items'], previousData); // Rollback
    toast.error("Couldn't save changes. Please try again.");
  }
};
```

### Frontend Security

```typescript
// ✅ Sanitize user-generated content
import DOMPurify from 'dompurify';
const sanitizedHTML = DOMPurify.sanitize(userContent);

// ✅ Prevent XSS in React (already safe by default)
// But be careful with dangerouslySetInnerHTML
<div dangerouslySetInnerHTML={{ __html: sanitizedHTML }} />

// ✅ CSRF protection
const csrfToken = document.querySelector('meta[name="csrf-token"]')?.content;
fetch('/api/endpoint', {
  method: 'POST',
  headers: {
    'X-CSRF-Token': csrfToken,
  },
});

// ✅ Secure storage
// Never store sensitive data in localStorage
// Use httpOnly cookies for tokens when possible

// ✅ Input validation (client-side is for UX, server validates for security)
const schema = z.object({
  email: z.string().email('Invalid email format'),
  password: z.string().min(8, 'Password must be at least 8 characters'),
});
```

---

## Backend Development

### Architecture Principles

#### 1. Clean Architecture Layers

```
┌─────────────────────────────────────────────────────────────┐
│                    CLEAN ARCHITECTURE                       │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  Presentation Layer (Controllers, API Endpoints)     │   │
│  │  - HTTP request/response handling                    │   │
│  │  - Input validation                                  │   │
│  │  - Authentication/Authorization                      │   │
│  └─────────────────────────────────────────────────────┘   │
│                          │                                  │
│                          ▼                                  │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  Application Layer (Use Cases, Services)             │   │
│  │  - Business logic orchestration                      │   │
│  │  - Transaction management                            │   │
│  │  - DTO transformations                               │   │
│  └─────────────────────────────────────────────────────┘   │
│                          │                                  │
│                          ▼                                  │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  Domain Layer (Entities, Value Objects)              │   │
│  │  - Business rules                                    │   │
│  │  - Domain events                                     │   │
│  │  - Repository interfaces                             │   │
│  └─────────────────────────────────────────────────────┘   │
│                          │                                  │
│                          ▼                                  │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  Infrastructure Layer (DB, External Services)        │   │
│  │  - Repository implementations                        │   │
│  │  - External API clients                              │   │
│  │  - Caching, messaging, logging                       │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘

Dependency Rule: Dependencies point INWARD only.
Outer layers can depend on inner layers, never the reverse.
```

#### 2. SOLID Principles Application

```csharp
// ═══════════════════════════════════════════════════════════
// SINGLE RESPONSIBILITY PRINCIPLE
// Each class has one reason to change
// ═══════════════════════════════════════════════════════════

// ❌ BAD: Multiple responsibilities
public class UserService
{
    public User CreateUser(UserDto dto) { /* ... */ }
    public void SendWelcomeEmail(User user) { /* ... */ }
    public string GenerateReport(List<User> users) { /* ... */ }
}

// ✅ GOOD: Separated responsibilities
public class UserService { /* User CRUD operations */ }
public class EmailService { /* Email operations */ }
public class ReportService { /* Report generation */ }

// ═══════════════════════════════════════════════════════════
// OPEN/CLOSED PRINCIPLE
// Open for extension, closed for modification
// ═══════════════════════════════════════════════════════════

// ✅ GOOD: Extensible through abstraction
public interface IPaymentProcessor
{
    Task<PaymentResult> ProcessAsync(Payment payment, CancellationToken ct);
}

public class StripeProcessor : IPaymentProcessor { /* ... */ }
public class PayPalProcessor : IPaymentProcessor { /* ... */ }
public class CryptoProcessor : IPaymentProcessor { /* ... */ } // New, no changes needed

// ═══════════════════════════════════════════════════════════
// LISKOV SUBSTITUTION PRINCIPLE
// Subtypes must be substitutable for their base types
// ═══════════════════════════════════════════════════════════

// ❌ BAD: Violates LSP
public class Rectangle
{
    public virtual int Width { get; set; }
    public virtual int Height { get; set; }
}

public class Square : Rectangle
{
    public override int Width
    {
        set { base.Width = base.Height = value; } // Unexpected behavior
    }
}

// ✅ GOOD: Proper abstraction
public interface IShape
{
    int Area { get; }
}

public class Rectangle : IShape { /* ... */ }
public class Square : IShape { /* ... */ }

// ═══════════════════════════════════════════════════════════
// INTERFACE SEGREGATION PRINCIPLE
// Clients should not depend on interfaces they don't use
// ═══════════════════════════════════════════════════════════

// ❌ BAD: Fat interface
public interface IRepository<T>
{
    T GetById(int id);
    IEnumerable<T> GetAll();
    void Add(T entity);
    void Update(T entity);
    void Delete(T entity);
    void BulkInsert(IEnumerable<T> entities);
    IEnumerable<T> ExecuteQuery(string sql);
}

// ✅ GOOD: Segregated interfaces
public interface IReadRepository<T>
{
    T GetById(int id);
    IEnumerable<T> GetAll();
}

public interface IWriteRepository<T>
{
    void Add(T entity);
    void Update(T entity);
    void Delete(T entity);
}

// ═══════════════════════════════════════════════════════════
// DEPENDENCY INVERSION PRINCIPLE
// Depend on abstractions, not concretions
// ═══════════════════════════════════════════════════════════

// ❌ BAD: Direct dependency
public class OrderService
{
    private readonly SqlOrderRepository _repository = new SqlOrderRepository();
}

// ✅ GOOD: Dependency injection
public class OrderService
{
    private readonly IOrderRepository _repository;
    
    public OrderService(IOrderRepository repository)
    {
        _repository = repository;
    }
}
```

#### 3. API Design Standards

```csharp
// ═══════════════════════════════════════════════════════════
// RESTful API Design
// ═══════════════════════════════════════════════════════════

// Resource naming: plural nouns, lowercase, hyphens for multi-word
// GET    /api/v1/users              → List users
// GET    /api/v1/users/{id}         → Get user by ID
// POST   /api/v1/users              → Create user
// PUT    /api/v1/users/{id}         → Update user (full)
// PATCH  /api/v1/users/{id}         → Update user (partial)
// DELETE /api/v1/users/{id}         → Delete user

// Nested resources for relationships
// GET    /api/v1/users/{id}/orders  → Get user's orders

// Query parameters for filtering, sorting, pagination
// GET    /api/v1/users?status=active&sort=-createdAt&page=1&limit=20

/// <summary>
/// Retrieves a paginated list of users with optional filtering.
/// </summary>
/// <param name="filter">Filter criteria for users</param>
/// <param name="cancellationToken">Cancellation token for request cancellation</param>
/// <returns>Paginated list of users</returns>
/// <response code="200">Returns the list of users</response>
/// <response code="400">Invalid filter parameters</response>
/// <response code="401">Unauthorized access</response>
[HttpGet]
[ProducesResponseType(typeof(PagedResult<UserDto>), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
public async Task<ActionResult<PagedResult<UserDto>>> GetUsers(
    [FromQuery] UserFilter filter,
    CancellationToken cancellationToken)
{
    var result = await _userService.GetUsersAsync(filter, cancellationToken);
    return Ok(result);
}

// ═══════════════════════════════════════════════════════════
// Error Response Format (RFC 7807 Problem Details)
// ═══════════════════════════════════════════════════════════

{
  "type": "https://api.example.com/errors/validation",
  "title": "Validation Error",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/v1/users",
  "traceId": "00-1234567890abcdef-fedcba0987654321-00",
  "errors": {
    "email": ["Email format is invalid"],
    "password": ["Password must be at least 8 characters"]
  }
}
```

#### Unified Response Format (MANDATORY)

**All API endpoints MUST use a consistent response format:**

```csharp
// ═══════════════════════════════════════════════════════════
// Unified API Response Wrapper
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Standard API response wrapper for all endpoints.
/// Ensures consistent response structure across the entire API.
/// </summary>
/// <typeparam name="T">The type of data being returned</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Indicates whether the request was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The response data (null if unsuccessful).
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Human-readable message describing the result.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error details (null if successful).
    /// </summary>
    public ApiError? Error { get; set; }

    /// <summary>
    /// Pagination metadata (null if not applicable).
    /// </summary>
    public PaginationMeta? Pagination { get; set; }

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the response.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Factory methods for consistent creation
    public static ApiResponse<T> SuccessResult(T data, string message = "Success")
        => new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> FailResult(string message, ApiError? error = null)
        => new() { Success = false, Message = message, Error = error };

    public static ApiResponse<T> PagedResult(T data, PaginationMeta pagination)
        => new() { Success = true, Data = data, Pagination = pagination };
}

public class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
}

public class PaginationMeta
{
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public bool HasPrevious => CurrentPage > 1;
    public bool HasNext => CurrentPage < TotalPages;
}

// ═══════════════════════════════════════════════════════════
// Usage in Controllers
// ═══════════════════════════════════════════════════════════

[HttpGet("{id}")]
public async Task<ActionResult<ApiResponse<UserDto>>> GetUser(
    Guid id, 
    CancellationToken cancellationToken)
{
    var user = await _userService.GetByIdAsync(id, cancellationToken);
    
    if (user == null)
        return NotFound(ApiResponse<UserDto>.FailResult(
            "User not found",
            new ApiError { Code = "USER_NOT_FOUND", Detail = $"No user exists with ID {id}" }));

    return Ok(ApiResponse<UserDto>.SuccessResult(user));
}

[HttpGet]
public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetUsers(
    [FromQuery] UserFilter filter,
    CancellationToken cancellationToken)
{
    var (users, totalCount) = await _userService.GetPagedAsync(filter, cancellationToken);
    
    var pagination = new PaginationMeta
    {
        CurrentPage = filter.Page,
        PageSize = filter.PageSize,
        TotalCount = totalCount,
        TotalPages = (int)Math.Ceiling(totalCount / (double)filter.PageSize)
    };

    return Ok(ApiResponse<List<UserDto>>.PagedResult(users, pagination));
}

// ═══════════════════════════════════════════════════════════
// JSON Response Examples
// ═══════════════════════════════════════════════════════════

// Success Response
{
    "success": true,
    "data": {
        "id": "123e4567-e89b-12d3-a456-426614174000",
        "email": "user@example.com",
        "name": "John Doe"
    },
    "message": "Success",
    "error": null,
    "pagination": null,
    "traceId": "00-1234567890abcdef-fedcba0987654321-00",
    "timestamp": "2024-01-15T10:30:00Z"
}

// Paginated Response
{
    "success": true,
    "data": [...],
    "message": "Success",
    "error": null,
    "pagination": {
        "currentPage": 1,
        "pageSize": 20,
        "totalPages": 5,
        "totalCount": 100,
        "hasPrevious": false,
        "hasNext": true
    },
    "traceId": "00-abcdef1234567890-0987654321fedcba-00",
    "timestamp": "2024-01-15T10:30:00Z"
}

// Error Response
{
    "success": false,
    "data": null,
    "message": "Validation failed",
    "error": {
        "code": "VALIDATION_ERROR",
        "detail": "One or more validation errors occurred.",
        "validationErrors": {
            "email": ["Email format is invalid"],
            "password": ["Password must be at least 8 characters"]
        }
    },
    "pagination": null,
    "traceId": "00-fedcba0987654321-1234567890abcdef-00",
    "timestamp": "2024-01-15T10:30:00Z"
}
```

#### Centralized API Request Client (MANDATORY)

**All outgoing HTTP requests MUST use a centralized client class that provides:**
- Consistent configuration (timeouts, retries, headers)
- Body format selection (JSON, Form, Multipart)
- Authentication handling
- Logging and tracing
- Error handling

```csharp
// ═══════════════════════════════════════════════════════════
// Centralized HTTP Client Service
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Centralized service for making HTTP requests with consistent 
/// configuration, headers, body formats, and error handling.
/// </summary>
public interface IApiClient
{
    /// <summary>
    /// Sends a GET request to the specified URI.
    /// </summary>
    Task<ApiClientResponse<T>> GetAsync<T>(
        string uri,
        ApiRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a POST request with the specified body.
    /// </summary>
    Task<ApiClientResponse<TResponse>> PostAsync<TRequest, TResponse>(
        string uri,
        TRequest body,
        ApiRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a PUT request with the specified body.
    /// </summary>
    Task<ApiClientResponse<TResponse>> PutAsync<TRequest, TResponse>(
        string uri,
        TRequest body,
        ApiRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a PATCH request with the specified body.
    /// </summary>
    Task<ApiClientResponse<TResponse>> PatchAsync<TRequest, TResponse>(
        string uri,
        TRequest body,
        ApiRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a DELETE request to the specified URI.
    /// </summary>
    Task<ApiClientResponse<T>> DeleteAsync<T>(
        string uri,
        ApiRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a multipart form request (for file uploads).
    /// </summary>
    Task<ApiClientResponse<TResponse>> PostMultipartAsync<TResponse>(
        string uri,
        MultipartFormDataContent content,
        ApiRequestOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for customizing individual API requests.
/// </summary>
public class ApiRequestOptions
{
    /// <summary>
    /// Request body format (JSON, FormUrlEncoded, Xml).
    /// </summary>
    public BodyFormat BodyFormat { get; set; } = BodyFormat.Json;

    /// <summary>
    /// Custom headers to add to this request.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Query string parameters.
    /// </summary>
    public Dictionary<string, string> QueryParams { get; set; } = new();

    /// <summary>
    /// Request timeout (overrides default).
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Number of retry attempts for transient failures.
    /// </summary>
    public int? RetryCount { get; set; }

    /// <summary>
    /// Authentication scheme override.
    /// </summary>
    public AuthenticationScheme? AuthScheme { get; set; }

    /// <summary>
    /// Bearer token (if using Bearer auth).
    /// </summary>
    public string? BearerToken { get; set; }

    /// <summary>
    /// API key (if using API key auth).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Skip SSL certificate validation (development only).
    /// </summary>
    public bool SkipSslValidation { get; set; } = false;
}

public enum BodyFormat
{
    Json,
    FormUrlEncoded,
    Xml,
    PlainText
}

public enum AuthenticationScheme
{
    None,
    Bearer,
    ApiKey,
    Basic
}

/// <summary>
/// Standardized response from API client operations.
/// </summary>
public class ApiClientResponse<T>
{
    public bool IsSuccess { get; set; }
    public HttpStatusCode StatusCode { get; set; }
    public T? Data { get; set; }
    public string? RawContent { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, IEnumerable<string>> Headers { get; set; } = new();
    public TimeSpan Elapsed { get; set; }
}

// ═══════════════════════════════════════════════════════════
// Implementation
// ═══════════════════════════════════════════════════════════

public class ApiClient : IApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiClient> _logger;
    private readonly ApiClientSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiClient(
        HttpClient httpClient,
        ILogger<ApiClient> logger,
        IOptions<ApiClientSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<ApiClientResponse<TResponse>> PostAsync<TRequest, TResponse>(
        string uri,
        TRequest body,
        ApiRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ApiRequestOptions();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Build request
            var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(uri, options.QueryParams));
            
            // Set headers
            ApplyHeaders(request, options);
            ApplyAuthentication(request, options);

            // Set body based on format
            request.Content = CreateContent(body, options.BodyFormat);

            // Add correlation ID
            var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
            request.Headers.Add("X-Correlation-Id", correlationId);

            _logger.LogInformation(
                "Sending {Method} request to {Uri} with CorrelationId {CorrelationId}",
                request.Method, uri, correlationId);

            // Send request
            using var cts = CreateTimeoutCts(options.Timeout, cancellationToken);
            var response = await _httpClient.SendAsync(request, cts.Token);

            stopwatch.Stop();

            // Process response
            var rawContent = await response.Content.ReadAsStringAsync(cancellationToken);
            
            var result = new ApiClientResponse<TResponse>
            {
                IsSuccess = response.IsSuccessStatusCode,
                StatusCode = response.StatusCode,
                RawContent = rawContent,
                Elapsed = stopwatch.Elapsed,
                Headers = response.Headers.ToDictionary(h => h.Key, h => h.Value)
            };

            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(rawContent))
            {
                result.Data = JsonSerializer.Deserialize<TResponse>(rawContent, _jsonOptions);
            }
            else if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = rawContent;
                _logger.LogWarning(
                    "Request to {Uri} failed with {StatusCode}: {Error}",
                    uri, response.StatusCode, rawContent);
            }

            _logger.LogInformation(
                "Request to {Uri} completed with {StatusCode} in {ElapsedMs}ms",
                uri, response.StatusCode, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogError("Request to {Uri} timed out after {ElapsedMs}ms", uri, stopwatch.ElapsedMilliseconds);
            
            return new ApiClientResponse<TResponse>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.RequestTimeout,
                ErrorMessage = "Request timed out",
                Elapsed = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Request to {Uri} failed with exception", uri);
            
            return new ApiClientResponse<TResponse>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.InternalServerError,
                ErrorMessage = ex.Message,
                Elapsed = stopwatch.Elapsed
            };
        }
    }

    private HttpContent CreateContent<T>(T body, BodyFormat format)
    {
        return format switch
        {
            BodyFormat.Json => new StringContent(
                JsonSerializer.Serialize(body, _jsonOptions),
                Encoding.UTF8,
                "application/json"),
            
            BodyFormat.FormUrlEncoded => new FormUrlEncodedContent(
                body as Dictionary<string, string> 
                ?? throw new ArgumentException("Body must be Dictionary<string, string> for form encoding")),
            
            BodyFormat.Xml => new StringContent(
                SerializeToXml(body),
                Encoding.UTF8,
                "application/xml"),
            
            BodyFormat.PlainText => new StringContent(
                body?.ToString() ?? string.Empty,
                Encoding.UTF8,
                "text/plain"),
            
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private void ApplyAuthentication(HttpRequestMessage request, ApiRequestOptions options)
    {
        var scheme = options.AuthScheme ?? _settings.DefaultAuthScheme;

        switch (scheme)
        {
            case AuthenticationScheme.Bearer:
                var token = options.BearerToken ?? _settings.DefaultBearerToken;
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;

            case AuthenticationScheme.ApiKey:
                var apiKey = options.ApiKey ?? _settings.DefaultApiKey;
                if (!string.IsNullOrEmpty(apiKey))
                    request.Headers.Add("X-API-Key", apiKey);
                break;

            case AuthenticationScheme.Basic:
                // Handle basic auth if needed
                break;
        }
    }

    // ... Additional helper methods (ApplyHeaders, BuildUri, etc.)
}

// ═══════════════════════════════════════════════════════════
// Usage Examples
// ═══════════════════════════════════════════════════════════

// JSON POST (default)
var result = await _apiClient.PostAsync<CreateUserRequest, UserDto>(
    "/api/users",
    new CreateUserRequest { Email = "test@example.com", Name = "Test" },
    cancellationToken: ct);

// Form URL encoded POST
var formResult = await _apiClient.PostAsync<Dictionary<string, string>, TokenResponse>(
    "/oauth/token",
    new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = "my-client",
        ["client_secret"] = "secret"
    },
    new ApiRequestOptions { BodyFormat = BodyFormat.FormUrlEncoded },
    ct);

// GET with custom headers and query params
var getResult = await _apiClient.GetAsync<List<UserDto>>(
    "/api/users",
    new ApiRequestOptions
    {
        QueryParams = new Dictionary<string, string>
        {
            ["page"] = "1",
            ["pageSize"] = "20",
            ["status"] = "active"
        },
        Headers = new Dictionary<string, string>
        {
            ["X-Custom-Header"] = "value"
        },
        Timeout = TimeSpan.FromSeconds(30)
    },
    ct);

// With API key authentication
var apiKeyResult = await _apiClient.GetAsync<DataResponse>(
    "/external/data",
    new ApiRequestOptions
    {
        AuthScheme = AuthenticationScheme.ApiKey,
        ApiKey = "external-api-key-123"
    },
    ct);
```

#### 4. Async/Await and CancellationToken

```csharp
// ═══════════════════════════════════════════════════════════
// EVERY async method MUST accept and propagate CancellationToken
// ═══════════════════════════════════════════════════════════

// ✅ GOOD: Full CancellationToken propagation
public class UserService : IUserService
{
    private readonly IUserRepository _repository;
    private readonly IEmailService _emailService;
    private readonly ILogger<UserService> _logger;

    /// <summary>
    /// Creates a new user and sends a welcome email.
    /// </summary>
    /// <param name="request">User creation request</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The created user</returns>
    /// <exception cref="ValidationException">When validation fails</exception>
    /// <exception cref="DuplicateEmailException">When email already exists</exception>
    public async Task<UserDto> CreateUserAsync(
        CreateUserRequest request, 
        CancellationToken cancellationToken = default)
    {
        // Check cancellation before expensive operations
        cancellationToken.ThrowIfCancellationRequested();

        // Validate
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        // Check for existing user
        var existingUser = await _repository.GetByEmailAsync(
            request.Email, 
            cancellationToken);
        
        if (existingUser != null)
            throw new DuplicateEmailException(request.Email);

        // Create user
        var user = new User(request.Email, request.Name);
        await _repository.AddAsync(user, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        // Send welcome email (non-critical, don't fail if this fails)
        try
        {
            await _emailService.SendWelcomeEmailAsync(user, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to send welcome email to {Email}", user.Email);
            // Queue for retry
            await _backgroundJobs.EnqueueAsync<SendWelcomeEmailJob>(
                job => job.ExecuteAsync(user.Id, CancellationToken.None));
        }

        return user.ToDto();
    }
}

// ✅ GOOD: Repository with CancellationToken
public class UserRepository : IUserRepository
{
    private readonly DbContext _context;

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<List<User>> GetByFilterAsync(
        UserFilter filter, 
        CancellationToken cancellationToken)
    {
        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(u => u.Name.Contains(filter.Search));

        if (filter.Status.HasValue)
            query = query.Where(u => u.Status == filter.Status.Value);

        return await query
            .OrderBy(u => u.Name)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(cancellationToken);
    }
}
```

#### 5. Database Best Practices

##### Database Normalization (MANDATORY)

**All database designs MUST adhere to all five normal forms (5NF):**

```
┌─────────────────────────────────────────────────────────────┐
│              DATABASE NORMALIZATION REQUIREMENTS            │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  1NF - First Normal Form                                    │
│  □ Eliminate repeating groups                               │
│  □ Create separate table for related data                   │
│  □ Identify each set with a primary key                     │
│                                                             │
│  2NF - Second Normal Form                                   │
│  □ Meet all 1NF requirements                                │
│  □ Remove subsets of data to separate tables                │
│  □ Create relationships using foreign keys                  │
│                                                             │
│  3NF - Third Normal Form                                    │
│  □ Meet all 2NF requirements                                │
│  □ Remove columns not dependent on primary key              │
│  □ Eliminate transitive dependencies                        │
│                                                             │
│  4NF - Fourth Normal Form                                   │
│  □ Meet all 3NF requirements                                │
│  □ Remove multi-valued dependencies                         │
│  □ No table may contain two+ independent multi-valued facts │
│                                                             │
│  5NF - Fifth Normal Form (Project-Join Normal Form)         │
│  □ Meet all 4NF requirements                                │
│  □ Cannot be decomposed into smaller tables without loss    │
│  □ Every join dependency is implied by candidate keys       │
│                                                             │
│  EXCEPTIONS:                                                │
│  Denormalization is permitted ONLY when:                    │
│  • Documented performance requirements demand it            │
│  • The trade-off is explicitly approved                     │
│  • Data integrity is maintained via other means             │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

##### Connection Management

// ✅ GOOD: Use connection pooling, short-lived connections
public class DapperUserRepository : IUserRepository
{
    private readonly string _connectionString;

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        
        var command = new CommandDefinition(
            "SELECT * FROM Users WHERE Id = @Id AND IsDeleted = 0",
            new { Id = id },
            cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<User>(command);
    }
}

// ═══════════════════════════════════════════════════════════
// Always Use Parameterized Queries
// ═══════════════════════════════════════════════════════════

// ❌ BAD: SQL Injection vulnerability
var sql = $"SELECT * FROM Users WHERE Email = '{email}'";

// ✅ GOOD: Parameterized query
var sql = "SELECT * FROM Users WHERE Email = @Email";
var user = await connection.QuerySingleOrDefaultAsync<User>(
    sql, 
    new { Email = email });

// ═══════════════════════════════════════════════════════════
// Transaction Management
// ═══════════════════════════════════════════════════════════

public async Task TransferFundsAsync(
    Guid fromAccount, 
    Guid toAccount, 
    decimal amount,
    CancellationToken cancellationToken)
{
    await using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync(cancellationToken);
    
    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
    
    try
    {
        // Debit from source
        await connection.ExecuteAsync(
            "UPDATE Accounts SET Balance = Balance - @Amount WHERE Id = @Id",
            new { Id = fromAccount, Amount = amount },
            transaction);

        // Credit to destination
        await connection.ExecuteAsync(
            "UPDATE Accounts SET Balance = Balance + @Amount WHERE Id = @Id",
            new { Id = toAccount, Amount = amount },
            transaction);

        await transaction.CommitAsync(cancellationToken);
    }
    catch
    {
        await transaction.RollbackAsync(cancellationToken);
        throw;
    }
}
```

#### 6. Caching Strategy

```csharp
// ═══════════════════════════════════════════════════════════
// Cache-Aside Pattern
// ═══════════════════════════════════════════════════════════

public class CachedUserRepository : IUserRepository
{
    private readonly IUserRepository _innerRepository;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedUserRepository> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var cacheKey = $"user:{id}";

        // Try cache first
        var cachedUser = await _cache.GetAsync<User>(cacheKey, cancellationToken);
        if (cachedUser != null)
        {
            _logger.LogDebug("Cache hit for user {UserId}", id);
            return cachedUser;
        }

        // Cache miss - get from database
        _logger.LogDebug("Cache miss for user {UserId}", id);
        var user = await _innerRepository.GetByIdAsync(id, cancellationToken);

        // Store in cache
        if (user != null)
        {
            await _cache.SetAsync(cacheKey, user, CacheDuration, cancellationToken);
        }

        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        await _innerRepository.UpdateAsync(user, cancellationToken);
        
        // Invalidate cache
        var cacheKey = $"user:{user.Id}";
        await _cache.RemoveAsync(cacheKey, cancellationToken);
    }
}

// ═══════════════════════════════════════════════════════════
// Cache Key Conventions
// ═══════════════════════════════════════════════════════════

public static class CacheKeys
{
    public static string User(Guid id) => $"user:{id}";
    public static string UserByEmail(string email) => $"user:email:{email.ToLowerInvariant()}";
    public static string UserRoles(Guid userId) => $"user:{userId}:roles";
    public static string UserPermissions(Guid userId) => $"user:{userId}:permissions";
    public static string Config(string key) => $"config:{key}";
}
```

#### 7. Logging Standards

```csharp
// ═══════════════════════════════════════════════════════════
// Structured Logging
// ═══════════════════════════════════════════════════════════

public class UserService
{
    private readonly ILogger<UserService> _logger;

    public async Task<User> CreateUserAsync(CreateUserRequest request, CancellationToken ct)
    {
        // ✅ GOOD: Structured logging with context
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Email"] = request.Email,
            ["RequestId"] = Activity.Current?.Id ?? Guid.NewGuid().ToString()
        }))
        {
            _logger.LogInformation("Creating new user");

            try
            {
                var user = await _repository.CreateAsync(request, ct);
                
                _logger.LogInformation(
                    "User created successfully with ID {UserId}", 
                    user.Id);
                
                return user;
            }
            catch (DuplicateEmailException ex)
            {
                _logger.LogWarning(
                    ex, 
                    "Failed to create user - email already exists");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, 
                    "Unexpected error creating user");
                throw;
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════
// Log Level Guidelines
// ═══════════════════════════════════════════════════════════

// TRACE   → Detailed debugging (method entry/exit, variable values)
// DEBUG   → Diagnostic information (cache hits/misses, query results)
// INFO    → Normal operation events (user created, order processed)
// WARNING → Unexpected but handled situations (retry, fallback used)
// ERROR   → Failures requiring attention (exception caught, operation failed)
// FATAL   → Critical failures (app startup failed, unrecoverable state)

// ❌ BAD: Logging sensitive data
_logger.LogInformation("User login: {Email}, Password: {Password}", email, password);

// ✅ GOOD: Redact sensitive data
_logger.LogInformation("User login attempt for {Email}", email);
```

---

## Product Development Management

### Role Definition

As a Product Development Manager, you bridge the gap between business requirements and technical implementation. Your decisions affect:

- **User Experience**: How users interact with and perceive the product
- **Technical Architecture**: How the system is built and maintained
- **Team Velocity**: How efficiently the team can deliver
- **Business Outcomes**: How well the product meets its goals

### Strategic Thinking Framework

#### 1. Feature Prioritization Matrix

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

#### 2. User Story Standards

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

#### 3. Requirements Analysis Checklist

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

#### 4. Sprint Planning Guidelines

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

#### 5. Technical Debt Management

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

#### 6. Stakeholder Communication

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

### Cross-Functional Collaboration

#### Working with Design
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

#### Working with Engineering
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

---

## Security Mindset

### Apply to EVERY Feature

For every endpoint, component, service, and database operation, ask yourself:

| Question | What to Look For |
|----------|------------------|
| **"How would an attacker exploit this?"** | Think like a penetration tester. What inputs could be malicious? What assumptions can be violated? |
| **"What if this input is malicious?"** | SQL injection, XSS, command injection, path traversal. Never trust user input. |
| **"What sensitive data could leak?"** | Check logs, error messages, API responses, stack traces. Are you exposing internal details? |
| **"What happens if this is called 10,000 times per second?"** | DoS potential. Rate limiting, resource exhaustion, database locks. |
| **"What if the user is authenticated but unauthorized?"** | Don't conflate authentication (who you are) with authorization (what you can do). |
| **"What if this fails partially?"** | Inconsistent state, orphaned records, leaked resources. Transactions, cleanup. |

### Security Code Review Checklist

```markdown
## Security Review Checklist

### Input Validation
- [ ] All inputs validated server-side (client validation is for UX only)
- [ ] Input length limits enforced
- [ ] Input type checking implemented
- [ ] Whitelist validation preferred over blacklist

### Authentication
- [ ] Passwords hashed with Argon2id (the ONLY approved hashing algorithm)
- [ ] API keys hashed with Argon2id before storage
- [ ] Session tokens are cryptographically random
- [ ] Session expiration implemented
- [ ] Secure cookie flags set (HttpOnly, Secure, SameSite)

### Cryptography Standards
┌─────────────────────────────────────────────────────────────┐
│              APPROVED CRYPTOGRAPHIC ALGORITHMS              │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Password/Secret Hashing:                                   │
│  ✅ Argon2id (ONLY approved algorithm)                      │
│  ❌ bcrypt - NOT APPROVED                                   │
│  ❌ scrypt - NOT APPROVED                                   │
│  ❌ PBKDF2 - NOT APPROVED                                   │
│  ❌ SHA-256/SHA-512 - NOT APPROVED for passwords            │
│  ❌ MD5 - NEVER USE                                         │
│                                                             │
│  Argon2id Configuration:                                    │
│  • Memory: 64 MB minimum (65536 KB)                         │
│  • Iterations: 3 minimum                                    │
│  • Parallelism: 4                                           │
│  • Salt: 16 bytes, cryptographically random                 │
│  • Hash length: 32 bytes                                    │
│                                                             │
│  Symmetric Encryption: AES-256-GCM                          │
│  Asymmetric Encryption: RSA-2048+ or Ed25519                │
│  JWT Signing: RS256 or ES256                                │
│                                                             │
└─────────────────────────────────────────────────────────────┘
- [ ] Session expiration implemented
- [ ] Secure cookie flags set (HttpOnly, Secure, SameSite)

### Authorization
- [ ] Access control checks on every request
- [ ] Authorization checked server-side, not client-side
- [ ] Principle of least privilege applied
- [ ] Resource ownership verified

### Data Protection
- [ ] Sensitive data encrypted at rest
- [ ] Sensitive data encrypted in transit (TLS)
- [ ] PII handled according to regulations (GDPR, etc.)
- [ ] Secrets not hardcoded or logged

### Error Handling
- [ ] Detailed errors logged server-side only
- [ ] Generic errors shown to users
- [ ] No stack traces in production responses
- [ ] Failed attempts monitored and alerted
```

---

## Failure Mode Design

### For Every Integration Point

**Every external dependency must have a defined failure response. No silent failures.**

| Component | Failure Scenario | Expected Behavior |
|-----------|-----------------|-------------------|
| **Database** | Connection failed | Return cached data if available; queue writes for retry; show degraded UI |
| **Database** | Query timeout | Cancel operation; log with context; return error to user |
| **Cache** | Redis unavailable | Fall back to database; log warning; continue without cache |
| **External API** | Timeout / 5xx | Retry with exponential backoff; circuit breaker; fallback response |
| **Message Queue** | Full / unavailable | Apply backpressure; store locally; retry with limits |
| **File Storage** | Upload failed | Retry; notify user; don't lose the file |

### Circuit Breaker Pattern

```csharp
// Circuit breaker states:
// CLOSED  → Normal operation, requests flow through
// OPEN    → Failures exceeded threshold, requests fail fast
// HALF-OPEN → Testing if service recovered

public class CircuitBreakerSettings
{
    public int FailureThreshold { get; set; } = 5;        // Failures before opening
    public TimeSpan OpenDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int SuccessThreshold { get; set; } = 3;        // Successes to close
}

// Usage with Polly
services.AddHttpClient<IExternalService, ExternalService>()
    .AddPolicyHandler(Policy
        .Handle<HttpRequestException>()
        .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (result, duration) => 
                logger.LogWarning("Circuit opened for {Duration}", duration),
            onReset: () => 
                logger.LogInformation("Circuit closed"),
            onHalfOpen: () => 
                logger.LogInformation("Circuit half-open, testing...")));
```

### Graceful Degradation Strategy

```
┌─────────────────────────────────────────────────────────────┐
│              GRACEFUL DEGRADATION LEVELS                    │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Level 0: FULL FUNCTIONALITY                                │
│  └─ All systems operational, optimal experience             │
│                                                             │
│  Level 1: DEGRADED BUT FUNCTIONAL                           │
│  └─ Non-critical features disabled                          │
│  └─ Real-time features fall back to polling                 │
│  └─ Caching more aggressively                               │
│                                                             │
│  Level 2: CORE FEATURES ONLY                                │
│  └─ Only essential features available                       │
│  └─ Read-only mode for some features                        │
│  └─ Queuing writes for later processing                     │
│                                                             │
│  Level 3: MAINTENANCE MODE                                  │
│  └─ Static content only                                     │
│  └─ Clear messaging to users                                │
│  └─ Estimated recovery time displayed                       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## Quality Assurance

### Testing Pyramid

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

### Test Quality Standards

```csharp
// ═══════════════════════════════════════════════════════════
// Unit Test Standards
// ═══════════════════════════════════════════════════════════

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

// ═══════════════════════════════════════════════════════════
// Test Naming Convention
// ═══════════════════════════════════════════════════════════

// Pattern: [Method]_[Scenario]_[ExpectedBehavior]
// Examples:
// - CreateUser_WithValidData_ReturnsCreatedUser
// - CreateUser_WithDuplicateEmail_ThrowsDuplicateEmailException
// - GetUser_WithNonExistentId_ReturnsNull
// - Login_WithCorrectCredentials_ReturnsToken
// - Login_WithWrongPassword_ThrowsAuthenticationException
```

### Code Coverage Requirements

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

---

## Documentation Standards

### Code Documentation

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

### README Template

```markdown
# [Project Name]

[One-paragraph description of what this project does]

## Quick Start

\`\`\`bash
# Clone and install
git clone [repo-url]
cd [project-name]
[install commands]

# Run
[run command]
\`\`\`

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

\`\`\`bash
# Run tests
[test command]

# Run linting
[lint command]

# Build for production
[build command]
\`\`\`

## Deployment

[Deployment instructions or link to deployment guide]

## Contributing

[Contribution guidelines or link]

## License

[License type]
```

### Architecture Decision Record (ADR) Template

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

---

## Final Review Checklist

### Before Marking Implementation Complete

**Perform this comprehensive review:**

#### 1. Code Quality Review
- [ ] All code follows SOLID principles
- [ ] DRY principle applied (no significant duplication)
- [ ] Consistent naming conventions throughout
- [ ] All public methods have XML documentation
- [ ] All async methods accept and propagate CancellationToken
- [ ] No hardcoded values (use configuration)
- [ ] Error handling is comprehensive

#### 2. Security Review
- [ ] All inputs validated and sanitized
- [ ] Authentication implemented correctly
- [ ] Passwords and secrets hashed with Argon2id ONLY (no other algorithms)
- [ ] Authorization checks on all protected resources
- [ ] Sensitive data encrypted at rest and in transit
- [ ] No secrets in code or logs
- [ ] SQL injection prevention verified
- [ ] XSS prevention in place
- [ ] CSRF protection implemented

#### 3. Testing Review
- [ ] Unit test coverage meets 90% minimum for ALL layers
- [ ] Integration tests passing
- [ ] Edge cases tested
- [ ] Error scenarios tested
- [ ] Performance tests for critical paths
- [ ] Coverage report generated and verified

#### 4. Frontend Review (if applicable)
- [ ] Responsive design tested across devices
- [ ] Accessibility audit passed (WCAG 2.1 AA)
- [ ] Loading states implemented
- [ ] Error states handled gracefully
- [ ] Keyboard navigation works
- [ ] Performance metrics acceptable (LCP, FID, CLS)

#### 5. Backend Review (if applicable)
- [ ] API follows REST conventions
- [ ] Error responses follow standard format
- [ ] Rate limiting configured
- [ ] Database queries optimized
- [ ] Caching strategy implemented
- [ ] Health checks operational

#### 6. Documentation Review
- [ ] README complete and accurate
- [ ] API documentation up to date
- [ ] Architecture diagrams current
- [ ] Deployment guide tested
- [ ] Configuration documented

#### 7. Operational Readiness
- [ ] Logging sufficient for debugging
- [ ] Monitoring and alerting configured
- [ ] Backup and recovery tested
- [ ] Rollback procedure documented
- [ ] Performance under load verified

#### 8. Runtime Stability
- [ ] All code paths tested for runtime errors
- [ ] Null checks comprehensive
- [ ] Exception handling covers all scenarios
- [ ] No known memory leaks
- [ ] Resource cleanup verified

#### 9. Failure Mode Verification
- [ ] Graceful degradation tested
- [ ] Circuit breakers configured
- [ ] Retry logic implemented
- [ ] Timeout values appropriate
- [ ] Fallback behaviors verified

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

---

## Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | [Date] | [Team] | Initial release |

---

*This document is a living guide. Update it as practices evolve and lessons are learned.*
