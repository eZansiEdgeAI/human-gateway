# Research Title: HumanGateway — An Offline-First Human Interaction Fabric for Agentic Workflows

## Research Summary

**HumanGateway** is a proposed standalone communication platform designed to connect humans, AI agents, and applications when network connectivity is unreliable or intermittent.

The initial target environment is **rural schools**, where schools may have old computers, inexpensive Android devices, limited bandwidth, intermittent Internet access, and little appetite for expensive infrastructure. The **first concrete use case is integration with [FlowForge](https://github.com/McFuzzySquirrel/flow-forge)**, an Agent Workforce Platform in which workflows, agents, people, skills and knowledge work together and where human input and human approval are already first-class workflow concepts.

HumanGateway is deliberately separate from FlowForge. FlowForge owns workflow execution, workflow state, agent execution, human-task semantics and audit. HumanGateway owns the communication boundary: reaching a human, storing and forwarding messages, synchronising over unreliable networks, transporting artifacts, and delivering responses back to the workflow.

The architecture is deliberately generic so that HumanGateway can later be reused by other workflow engines, agent platforms, applications, or non-AI systems.

The core idea is to treat communication more like **email than real-time messaging**: messages and content are durable, queued locally, and forwarded when connectivity becomes available.

A small **Edge Gateway** runs at the school, potentially on a Raspberry Pi or an existing old PC. Users connect to it over the school's local network using a lightweight, offline-capable Progressive Web App (PWA). Messages, workflow tasks, responses, and content are stored locally and placed into an outbox. When Internet connectivity becomes available, the Edge Gateway synchronises with a cloud **Relay**.

Users outside the school can access the same service through a web endpoint. The school Edge Gateway does not need to be directly exposed to the Internet; it can maintain outbound synchronisation with the cloud Relay.

The result is an architecture where the **workflow does not depend on continuous connectivity**.

---

## Core Principle

> **Assume everyone and everything can be offline. Make delivery eventual, durable, and recoverable rather than requiring real-time connectivity.**

This is particularly valuable for AI-enabled workflows because a human task can remain pending while the human, school, agent, or network is temporarily unavailable.

---

## First Reference Integration: FlowForge

The first reference implementation for HumanGateway will integrate with **FlowForge**.

FlowForge describes workflows as declarative specifications containing agent steps, `human-input`, `human-approval`, retry and branch semantics. Human-in-the-loop is therefore already part of the workflow model. It also requires human actions to be authenticated and role-checked and maintains an immutable, hash-chained audit trail. citeturn0view0

HumanGateway should not duplicate these responsibilities.

The intended boundary is:

```text
┌───────────────────────────────────────┐
│              FLOWFORGE                │
│                                       │
│ Workforce Packages                    │
│ Agents                                │
│ Workflow Execution                    │
│ Human Task Semantics                  │
│ Workflow State                        │
│ Authentication / Authorisation        │
│ Audit                                 │
└───────────────────┬───────────────────┘
                    │
            Human Interaction API
                    │
                    ▼
┌───────────────────────────────────────┐
│            HUMAN GATEWAY              │
│                                       │
│ Message Protocol                      │
│ Delivery                              │
│ Store / Forward                       │
│ Routing                               │
│ Artifact Transfer                     │
│ Connectivity / Synchronisation        │
└───────────────────┬───────────────────┘
                    │
          ┌─────────┴─────────┐
          │                   │
    School Edge          Cloud Relay
          │                   │
      Local PWA          Remote Web
```

A FlowForge `human-input` or `human-approval` node should be able to emit a human interaction request to HumanGateway. HumanGateway delivers that request through whatever transport is available and eventually returns the human response to FlowForge.

This allows FlowForge to remain transport-independent.

### Example

```text
FlowForge
    │
    │ HumanInteractionRequested
    ▼
HumanGateway
    │
    ▼
School Edge
    │
    ▼
Teacher PWA
    │
    │ response + optional artifact
    ▼
School Edge
    │
    │ store-and-forward
    ▼
HumanGateway Relay
    │
    ▼
FlowForge
    │
    ▼
Workflow resumes
```

The first proof-of-concept should replace or complement FlowForge's current interactive human interaction mechanism with a HumanGateway-backed provider. FlowForge already supports headless workflow execution with supplied answers, making this a useful baseline for comparing synchronous and asynchronous human interaction. citeturn0view0

### Architectural principle

> **FlowForge decides that a human interaction is required. HumanGateway decides how that human is reached and how the response gets back.**

This separation means the same HumanGateway can serve FlowForge, another orchestration engine, a standalone application, or a completely different domain.

---

## Conceptual Architecture

```text
                         INTERNET
                            │
                      occasionally
                            │
                            ▼
                  ┌───────────────────┐
                  │    CLOUD RELAY    │
                  │                   │
                  │ Identity          │
                  │ Routing           │
                  │ Message Store     │
                  │ Artifact Store    │
                  │ Sync API          │
                  └─────────┬─────────┘
                            │
                         HTTPS
                            │
                            ▼
                  ┌───────────────────┐
                  │   SCHOOL EDGE     │
                  │                   │
                  │ .NET Service      │
                  │ SQLite            │
                  │ Inbox             │
                  │ Outbox            │
                  │ Artifact Store    │
                  │ Sync Engine       │
                  │ Local API         │
                  └─────────┬─────────┘
                            │
                         Local LAN
                            │
             ┌──────────────┼──────────────┐
             │              │              │
          Laptop         Android        Tablet
             │              │              │
             └──────────────┼──────────────┘
                            │
                       React PWA
                            │
                          Human
```

The AI workflow harness sits above this layer:

```text
┌──────────────────────────────┐
│       AI WORKFLOW            │
│         HARNESS              │
│                              │
│ Agents / Orchestration       │
│ Human Tasks                  │
└──────────────┬───────────────┘
               │
        Human Interaction
               │
               ▼
┌──────────────────────────────┐
│        HUMAN GATEWAY         │
│                              │
│ Store / Forward              │
│ Routing                      │
│ Identity                     │
│ Messages                     │
│ Artifacts                    │
└──────────────┬───────────────┘
               │
        Local / Remote
               │
               ▼
             Human
```

The workflow engine should not need to know whether the recipient is using a Raspberry Pi, old laptop, Android phone, SMS, USSD, or a web browser.

---

## Key Design Ideas

### 1. Edge-first

The school should be able to operate without Internet access.

The Edge Gateway provides:

- Local message storage
- Local API
- Local user access
- Inbox and outbox
- Artifact storage
- Synchronisation
- Identity for the school gateway

A Raspberry Pi is one possible deployment target, but the software should also run on an old Linux or Windows PC.

### 2. Store-and-forward

Messages should be treated as durable events rather than transient requests.

```text
Create Message
      │
      ▼
Local Store
      │
      ▼
Outbox
      │
      ├── Internet unavailable
      │       │
      │       ▼
      │     WAIT
      │
      └── Internet available
              │
              ▼
            Relay
```

Connectivity failure should result in `WAITING_FOR_SYNC`, not system failure.

### 3. Offline-capable PWA

The client should use:

- React
- TypeScript
- Service Worker
- IndexedDB
- Local caching
- Offline detection
- Local outbox

The same application should work from a school LAN and from the Internet.

### 4. Cloud Relay

The cloud should act primarily as a **relay and rendezvous point**, rather than being required for every local interaction.

Potential initial implementation:

- ASP.NET Core
- PostgreSQL
- Object/blob storage
- HTTPS API

The Edge Gateway makes outbound connections to the Relay, avoiding the need for inbound firewall rules or public IP addresses at the school.

### 5. Durable synchronisation

Synchronisation should use:

- Durable message IDs
- Sequence numbers
- Cursors
- Delivery states
- Content hashes
- Idempotent operations

This allows a gateway to disconnect for hours or days and later converge with the cloud without losing or duplicating messages.

### 6. First-class artifacts

Messages should reference content rather than embedding large files directly.

Examples:

- Images
- PDFs
- Documents
- Audio
- Other educational content

Artifact transfer should eventually support resumable uploads and downloads.

---

## Protocol Concept

The underlying protocol should be transport-agnostic and independent of the .NET implementation.

Core entities could include:

### Participant

Anything capable of sending or receiving communication.

```text
human:teacher-123
agent:assessment-agent
system:school-management
```

### Message

The fundamental communication envelope.

```text
Message
├── ID
├── Sender
├── Recipient(s)
├── Conversation
├── Workflow reference
├── Task reference
├── Payload
├── Attachments
└── Timestamps
```

### Artifact

A content object referenced by a message.

```text
Artifact
├── ID
├── Filename
├── MIME type
├── Size
├── Hash
└── Storage metadata
```

### Delivery

Tracks message movement.

```text
QUEUED
SYNCING
DELIVERED
ACKNOWLEDGED
FAILED
```

### Human Task

A higher-level workflow concept that can be transported by the communication layer.

```text
HumanTask
├── Task ID
├── Question
├── Response Type
├── Options
├── Expiry
└── Workflow Reference
```

A key architectural distinction is:

> **Message is a communication primitive; Human Task is a workflow primitive.**

HumanGateway should transport human tasks without becoming responsible for workflow orchestration.

---

## Example AI Workflow

An AI agent needs a teacher to review a learner's handwritten mathematics work.

```text
Assessment Agent
       │
       │ Create Human Task
       ▼
HumanGateway
       │
       ▼
School Edge
       │
       ▼
Teacher PWA
       │
       │ Photo + response
       ▼
School Edge
       │
       │ Store locally
       ▼
      Outbox
       │
       │ Internet returns
       ▼
Cloud Relay
       │
       ▼
Workflow Harness
       │
       ▼
Assessment Agent
```

The teacher does not need to know whether the AI system is running locally, in Azure, or somewhere else.

---

## External Access

The same communication system can support users outside the school.

```text
Inside School:

Teacher
  │
  ▼
PWA
  │
  ▼
School Edge


Outside School:

Teacher
  │
  ▼
Internet
  │
  ▼
Cloud Relay
  │
  ▼
School Edge
```

The school Edge Gateway should make outbound connections to the Relay rather than being directly exposed to the Internet.

This provides a clean security boundary and avoids requiring schools to configure port forwarding, public IP addresses, or inbound firewall rules.

---

## Future Transport Adapters

The initial implementation should not depend on SMS, WhatsApp, or USSD.

Those should eventually become transport adapters:

```text
                 HumanGateway
                      │
       ┌──────────────┼──────────────┐
       │              │              │
     Edge            Web            SMS
       │              │              │
      PWA          Browser       Feature Phone
```

Potential future adapters:

- SMS
- USSD
- WhatsApp
- Email
- Other messaging platforms

The workflow and message model should remain unchanged regardless of transport.

---

## Proposed Implementation Phases

### Phase 0 — Protocol

Define:

- Message schema
- Participant model
- Artifact model
- Delivery states
- Synchronisation model
- Identity model
- Error model

### Phase 1 — Local Edge

Build:

- .NET Edge Gateway
- SQLite
- Local REST API
- Inbox
- Outbox
- Artifact store

Goal:

> Two devices can communicate entirely over a local network with no Internet.

### Phase 2 — Offline PWA

Build:

- React/TypeScript client
- Service worker
- IndexedDB
- Offline cache
- Local outbox

Goal:

> A user can send and receive messages even when the Internet is unavailable.

### Phase 3 — Synchronisation

Build:

- Sync protocol
- Cursors
- Sequence numbers
- Idempotency
- Retry handling
- Delivery acknowledgements

Goal:

> Messages survive connectivity loss and eventually reach their destination.

### Phase 4 — Cloud Relay

Build:

- ASP.NET Core Relay
- Persistent message store
- Artifact store
- Gateway registration
- Synchronisation endpoint

Goal:

> Multiple disconnected schools can exchange messages through the cloud.

### Phase 5 — Identity and Security

Implement:

- Gateway identity
- User identity
- Authentication
- Authorisation
- Signed requests/tokens
- Encryption in transit
- Secure artifact access

### Phase 6 — External Web Access

Allow authenticated users to access their messages from outside the school.

### Phase 7 — FlowForge Reference Integration

Build the first production-style consumer of HumanGateway using FlowForge.

Create a FlowForge-side `HumanInteractionProvider` abstraction so the workflow engine can use different human interaction mechanisms without embedding transport concerns.

Initial providers could be:

```text
ConsoleHumanInteractionProvider
HumanGatewayInteractionProvider
```

The HumanGateway provider should translate FlowForge human interaction requests into HumanGateway messages and translate responses back into FlowForge workflow events.

Expose concepts such as:

```text
HumanInteractionRequested
HumanResponseReceived
HumanInteractionCompleted
ArtifactReceived
HumanInteractionExpired
```

The workflow engine remains responsible for deciding whether the response is valid, whether the actor is authorised for the workflow node, and how the workflow proceeds.

### Phase 8 — Additional Channels

Add:

- SMS
- USSD
- WhatsApp
- Email

as optional adapters.

---

## Testing Strategy

The defining feature of HumanGateway is resilience to unreliable connectivity.

Testing should therefore include deliberate failure scenarios:

- Internet disappears
- Internet returns
- Edge Gateway reboots
- Client reboots
- Cloud Relay becomes unavailable
- Messages are duplicated
- Messages arrive out of order
- Artifact transfer is interrupted
- Device remains offline for several days
- Multiple clients synchronise simultaneously

A key acceptance criterion should be:

> **A school can disappear from the network for an extended period and, when connectivity returns, the system can reliably converge without losing or duplicating messages.**

---

## Suggested Technology Stack

### Edge

- .NET / ASP.NET Core
- SQLite
- Local filesystem for artifacts
- Background sync worker
- REST/HTTPS

### Client

- React
- TypeScript
- PWA
- Service Worker
- IndexedDB

### Cloud

- ASP.NET Core
- PostgreSQL
- Azure Blob Storage or compatible object storage
- HTTPS
- Containerised deployment

### Development

- Docker/Podman
- Docker Compose
- Automated integration tests
- Network failure/chaos testing

---

## Potential Repository Structure

```text
human-gateway/
│
├── docs/
│   ├── architecture/
│   ├── protocol/
│   ├── security/
│   └── deployment/
│
├── schemas/
│   ├── message.schema.json
│   ├── artifact.schema.json
│   ├── participant.schema.json
│   └── sync.schema.json
│
├── src/
│   ├── HumanGateway.Protocol/
│   ├── HumanGateway.Core/
│   ├── HumanGateway.Edge/
│   ├── HumanGateway.Relay/
│   ├── HumanGateway.Client/
│   └── HumanGateway.Workflow/
│
├── adapters/
│   ├── Sms/
│   ├── Ussd/
│   └── Email/
│
├── deployment/
│   ├── docker/
│   ├── raspberry-pi/
│   └── azure/
│
└── tests/
    ├── unit/
    ├── integration/
    ├── sync/
    └── chaos/
```

---

## Research Questions

The project can be explored through several research questions:

1. How reliably can a store-and-forward architecture support FlowForge human-in-the-loop workflows under intermittent connectivity?
2. What is the cleanest interface between a workflow engine's human-task semantics and a transport-independent human communication fabric?
3. How should workflow identity, human identity and communication identity be separated while still allowing secure end-to-end authorisation and audit?
4. How reliably can FlowForge resume a paused workflow after a human response has been delayed for an extended period?
5. How can the communication layer remain independent of FlowForge while still preserving the workflow and task correlation required by the workflow engine?
6. What is the minimum hardware required for a school Edge Gateway?
2. What is the minimum hardware required for a school Edge Gateway?
3. How should message synchronisation handle duplication, ordering, retries, and long periods of disconnection?
4. What protocol abstractions allow the same human interaction to be delivered through PWA, SMS, USSD, and other transports?
5. How can artifacts be synchronised efficiently over low-bandwidth connections?
6. How should identity and security work across intermittently connected edge nodes?
7. Can the same architecture support AI workflows outside the education environment?
8. What parts of the protocol should be standardised or made implementation-independent?

---

## Initial Proof of Concept

The first prototype should deliberately avoid AI, SMS, cloud complexity, and sophisticated identity.

Build only:

```text
┌──────────────┐
│ Browser A    │
│ PWA          │
└──────┬───────┘
       │
       │ Local Wi-Fi
       ▼
┌──────────────────┐
│ Edge Gateway     │
│                  │
│ .NET             │
│ SQLite           │
│ Inbox            │
│ Outbox           │
└──────┬───────────┘
       │
       │ Local Wi-Fi
       ▼
┌──────────────┐
│ Browser B    │
│ PWA          │
└──────────────┘
```

Then test:

1. Send a message with both devices online.
2. Disconnect the Internet.
3. Send messages.
4. Reboot a client.
5. Reboot the Edge Gateway.
6. Restore connectivity.
7. Synchronise with a cloud Relay.
8. Verify that every message and artifact arrives exactly once from the user's perspective.

If this works reliably, the core architecture has been proven.

---

## Product / Research Boundary

HumanGateway should be treated as a **standalone reusable communication platform**, with FlowForge as its first reference consumer rather than its owner.

This creates three distinct layers:

```text
Application / Workflow Layer
        │
        │ human interaction request
        ▼
Human Interaction Fabric
        │
        │ transport / sync
        ▼
Network / Channel Layer
```

The communication fabric should not assume that the application is an AI system, that the recipient is a teacher, or that the transport is HTTP.

This makes the rural-school deployment a demanding reference environment rather than a hard-coded product constraint.

The longer-term research proposition is therefore not simply:

> "How do we communicate with rural schools?"

It is:

> **"How can human participation in agentic and distributed workflows remain reliable when the network is not?"**

---

## Broader Potential

Although rural schools are the initial target, the architecture could support any environment where connectivity is unreliable or where participants need asynchronous interaction.

Potential applications include:

- Remote education
- Field workers
- Healthcare outreach
- Agricultural services
- Disaster response
- Remote industrial sites
- Community services
- Offline-first enterprise applications
- AI agents operating with human supervisors
- Multi-agent workflows involving distributed humans

The broader proposition is therefore:

> **HumanGateway provides a durable communication boundary between humans, agents and applications across unreliable networks.**

The rural-school scenario is not the product boundary; it is the environment that provides a strong real-world test of the architecture.
