# Infonetica Workflow Engine (Minimal .NET 8 State-Machine API)

A tiny ASP.NET Core **Minimal API** that lets a client define workflows (states + actions), start workflow instances, move instances via actions (with validation), and inspect definitions/instances.&#x20;

---

## Features (Spec Alignment)

* Define workflows made up of **states** and **actions** (transitions).&#x20;
* Each **state** has: `id`, `name`, `isInitial`, `isFinal`, `enabled`.&#x20;
* Each **action** has: `id`, `name`, `enabled`, `fromStates[]`, `toState`.&#x20;
* A workflow definition must contain **exactly one** initial state.&#x20;
* A workflow instance references its definition, tracks current state + basic history (action + timestamp), and **starts at the definition’s initial state**.&#x20;
* API operations: create & retrieve definitions; start instance; execute action if it belongs to the instance’s definition, is enabled, and current state allowed; get current state/history.&#x20;
* Validation: reject bad definitions (duplicate IDs, missing initial, bad refs) and invalid transitions (disabled, wrong from, unknown state, acting on final).&#x20;
* Simple **in-memory** persistence (spec allows in-memory or lightweight file; no DB required).&#x20;
* Implemented with **.NET 8 / C# Minimal API**, low dependencies, as recommended.&#x20;

---

## Quick Start

> Requires .NET 8 SDK installed.

```bash
git clone <your-repo-url>
cd WorkflowEngine
dotnet run --urls "http://localhost:6000"    # choose any free port
```

Open the URL you chose (above) in Postman/curl and call the endpoints below. Quick-start build/run instructions requested in brief.&#x20;

---

## Minimal Data Model

```csharp
record State(Guid Id, string Name, bool IsInitial, bool IsFinal, bool Enabled);
record ActionDef(Guid Id, string Name, bool Enabled, List<Guid> FromStates, Guid ToState);
record WorkflowDefinition(Guid Id, string Name, List<State> States, List<ActionDef> Actions);
record HistoryEntry(Guid ActionId, DateTime Timestamp);
record WorkflowInstance(Guid Id, Guid DefinitionId, Guid CurrentStateId, List<HistoryEntry> History);
```

---

## REST Endpoints

| Verb | Route                                    | Description                                               |
| ---- | ---------------------------------------- | --------------------------------------------------------- |
| POST | `/definitions`                           | Create a workflow definition.                             |
| GET  | `/definitions`                           | List all workflow definitions.                            |
| GET  | `/definitions/{id}`                      | Get a single definition by ID.                            |
| POST | `/definitions/{defId}/instances`         | Start an instance from definition (enters initial state). |
| GET  | `/instances`                             | List all workflow instances.                              |
| GET  | `/instances/{id}`                        | Inspect instance (current state + history).               |
| POST | `/instances/{instId}/actions/{actionId}` | Execute action on instance (validated).                   |

(Operations correspond to spec areas: configuration, runtime start, execute w/ rules, inspect instance.)&#x20;

---

## Sample Definition JSON

Replace the GUIDs; see Testing section for automated generation.

```json
{
  "id": "84229ce4-fa74-4e20-8e77-c826699e87de",
  "name": "Onboarding",
  "states": [
    { "id": "0d0d6b7a-1ec8-4934-b626-02948d12ec62", "name": "Draft",    "isInitial": true,  "isFinal": false, "enabled": true },
    { "id": "eb824e8d-7f5c-4ca6-bbfd-6bff1387af35", "name": "Review",   "isInitial": false, "isFinal": false, "enabled": true },
    { "id": "61f60cb1-8f4e-4d97-8523-ef5e16eefb99", "name": "Approved", "isInitial": false, "isFinal": true,  "enabled": true }
  ],
  "actions": [
    { "id": "b62e216d-eb03-4138-93d1-fe8d7dbd5e4b", "name": "SubmitForReview", "enabled": true, "fromStates": ["0d0d6b7a-1ec8-4934-b626-02948d12ec62"], "toState": "eb824e8d-7f5c-4ca6-bbfd-6bff1387af35" },
    { "id": "1ea6b52b-d325-461a-b6e9-a5afae13656d", "name": "Approve",         "enabled": true, "fromStates": ["eb824e8d-7f5c-4ca6-bbfd-6bff1387af35"], "toState": "61f60cb1-8f4e-4d97-8523-ef5e16eefb99" }
  ]
}
```

---

## Testing (curl)

Set a base URL to match how you launched the app:

```bash
BASE=http://localhost:6000
```

### 1. Create definition

```bash
curl -i -X POST $BASE/definitions \
  -H "Content-Type: application/json" \
  --data @wf.json
```

### 2. Start instance

```bash
INST=$(curl -s -X POST $BASE/definitions/<DEF_ID>/instances | jq -r '.id')
```

### 3. Inspect instance

```bash
curl -s $BASE/instances/$INST | jq
```

### 4. Transition: Draft → Review

```bash
curl -s -X POST $BASE/instances/$INST/actions/<ACT_SUBMIT> | jq
```

### 5. Transition: Review → Approved

```bash
curl -s -X POST $BASE/instances/$INST/actions/<ACT_APPROVE> | jq
```

### 6. Negative checks

* Re-approve final instance → expect 400 (final).
* Approve before Submit → expect 400 (invalid from).
* Disable action in JSON and re-POST new def → expect 400 (disabled action).

These exercise the required validation behaviors.&#x20;

---

## Testing (Postman) – Quick Steps

1. Create an environment with `baseUrl` set to your local URL.
2. In **Create Definition** request, Pre-request Script generate GUIDs, then use them in body.
3. In Tests script, store `defId`, `actSubmit`, `actApprove` from response.
4. **Start Instance** request stores `instId` in Tests script.
5. Run **SubmitForReview** and **Approve** requests using those variables.
6. Confirm instance moves through states; error when acting after final.

The sequence maps to the spec’s required capabilities: create, retrieve, start, execute w/ validation, inspect.&#x20;

---

## Error Behavior

The API returns JSON error objects and appropriate HTTP status codes when validation fails (duplicate IDs, missing initial, disabled action, invalid from-state, acting on final, unknown state). These conditions are required validations in the brief.&#x20;

---

## Assumptions & Shortcuts

* **Client supplies GUIDs** for IDs (simplifies demo; would auto-gen server-side in production). (Spec allows you to choose payload shape.)&#x20;
* State/action `enabled` flags are honored at runtime; cannot enter disabled initial or target state. Enabled is part of the required attributes.&#x20;
* In-memory only; restart clears data. Acceptable per spec (no DB required).&#x20;
* Minimal single-file implementation using ASP.NET Core Minimal API, as encouraged in guidelines.&#x20;
* No auth / security; not in scope of exercise (time-boxed).&#x20;
* Assumptions documented here per brief request.&#x20;

---

## Known Limitations / Future Work

Brief invited TODOs if more time were available.&#x20;

* Persist to JSON file for durability. Spec allows file persistence.&#x20;
* Incremental definition editing (add states/actions after creation). Supported by spec as an option.&#x20;
* Bulk validation report (aggregate errors, not first-fail). Spec emphasizes clear error handling.&#x20;
* Extended metadata on states/actions (description, roles). Spec notes you may add attributes.&#x20;
* Unit tests (xUnit) beyond manual curl/Postman; welcome but optional.&#x20;
