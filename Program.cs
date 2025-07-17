using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<JsonOptions>(opts =>
    opts.SerializerOptions.WriteIndented = true);
var app = builder.Build();

// In-memory stores
var definitions = new ConcurrentDictionary<Guid, WorkflowDefinition>();
var instances   = new ConcurrentDictionary<Guid, WorkflowInstance>();

// Validation helper
void ValidateDefinition(WorkflowDefinition def)
{
    if (def is null)
        throw new ArgumentException("Definition payload missing.");

    // States required
    if (def.States is null || def.States.Count == 0)
        throw new ArgumentException("Workflow must include at least one state.");

    // Actions collection required (may be empty, but not null)
    if (def.Actions is null)
        throw new ArgumentException("Actions collection is required (may be empty).");

    // No null entries
    if (def.States.Any(s => s is null))
        throw new ArgumentException("Null state entry detected.");
    if (def.Actions.Any(a => a is null))
        throw new ArgumentException("Null action entry detected.");

    // Duplicate IDs (states / actions)
    if (def.States.Select(s => s.Id).Distinct().Count() != def.States.Count ||
        def.Actions.Select(a => a.Id).Distinct().Count() != def.Actions.Count)
        throw new ArgumentException("Duplicate state or action IDs detected.");

    // Exactly one initial state (spec)
    if (def.States.Count(s => s.IsInitial) != 1)
        throw new ArgumentException("Definition must have exactly one initial state.");

    // Validate each action
    var stateIds = def.States.Select(s => s.Id).ToHashSet();

    foreach (var a in def.Actions)
    {
        if (a.FromStates is null || a.FromStates.Count == 0)
            throw new ArgumentException($"Action '{a.Name}' must specify at least one from-state.");

        // No null/duplicate from-states
        if (a.FromStates.Any(fs => fs == Guid.Empty))
            throw new ArgumentException($"Action '{a.Name}' has an invalid from-state id.");
        if (a.FromStates.Distinct().Count() != a.FromStates.Count)
            throw new ArgumentException($"Action '{a.Name}' has duplicate from-state ids.");

        // Target state must exist
        if (!stateIds.Contains(a.ToState))
            throw new ArgumentException($"Action '{a.Name}' points to unknown target state.");

        // All from-states must exist
        var unknown = a.FromStates.Where(fs => !stateIds.Contains(fs)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Action '{a.Name}' references unknown from-state(s): {string.Join(",", unknown)}.");
    }
}

// ---- Endpoints ----

// Create definition
app.MapPost("/definitions", (WorkflowDefinition def) =>
{
    try
    {
        ValidateDefinition(def);
        if (!definitions.TryAdd(def.Id, def))
            return Results.Conflict(new { error = "Definition already exists." });
        return Results.Created($"/definitions/{def.Id}", def);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// List definitions
app.MapGet("/definitions", () => Results.Ok(definitions.Values));

// Get definition
app.MapGet("/definitions/{id:guid}", (Guid id) =>
    definitions.TryGetValue(id, out var d)
        ? Results.Ok(d)
        : Results.NotFound(new { error = "Definition not found." })
);

// Start instance
app.MapPost("/definitions/{defId:guid}/instances", (Guid defId) =>
{
    if (!definitions.TryGetValue(defId, out var def))
        return Results.NotFound(new { error = "Definition not found." });

    var init = def.States.Single(s => s.IsInitial);

    // Initial state must be enabled (can't start disabled workflows)
    if (!init.Enabled)
        return Results.BadRequest(new { error = "Initial state is disabled; cannot start instance." });

    var inst = new WorkflowInstance(Guid.NewGuid(), defId, init.Id, new());
    instances[inst.Id] = inst;
    return Results.Created($"/instances/{inst.Id}", inst);
});

// List instances
app.MapGet("/instances", () => Results.Ok(instances.Values));

// Get instance
app.MapGet("/instances/{id:guid}", (Guid id) =>
    instances.TryGetValue(id, out var i)
        ? Results.Ok(i)
        : Results.NotFound(new { error = "Instance not found." })
);

// Execute action
app.MapPost("/instances/{instId:guid}/actions/{actionId:guid}", (Guid instId, Guid actionId) =>
{
    if (!instances.TryGetValue(instId, out var inst))
        return Results.NotFound(new { error = "Instance not found." });

    // Fetch the definition this instance was created from (enforces "belongs to definition")
    if (!definitions.TryGetValue(inst.DefinitionId, out var def))
        return Results.Problem("Definition missing for instance.", statusCode: 500);

    // Lookup action *within this definition only* (prevents cross-definition execution)
    var action = def.Actions.SingleOrDefault(a => a.Id == actionId);
    if (action is null)
        return Results.BadRequest(new { error = "Unknown action for this instance's definition." });

    if (!action.Enabled)
        return Results.BadRequest(new { error = "Action disabled." });

    var curr = def.States.Single(s => s.Id == inst.CurrentStateId);
    if (curr.IsFinal)
        return Results.BadRequest(new { error = "Already final." });

    if (!action.FromStates.Contains(curr.Id))
        return Results.BadRequest(new { error = "Invalid from-state." });

    // Check target state exists & is enabled before transition
    var targetState = def.States.SingleOrDefault(s => s.Id == action.ToState);
    if (targetState is null)
        return Results.BadRequest(new { error = "Action points to unknown target state." });
    if (!targetState.Enabled)
        return Results.BadRequest(new { error = "Target state disabled." });

    var history = inst.History.Append(new HistoryEntry(action.Id, DateTime.UtcNow)).ToList();
    var next    = inst with { CurrentStateId = action.ToState, History = history };
    instances[instId] = next;
    return Results.Ok(next);
});

app.Run();

// ------------------- TYPE DECLARATIONS BELOW -------------------

record State(Guid Id, string Name, bool IsInitial, bool IsFinal, bool Enabled);
record ActionDef(Guid Id, string Name, bool Enabled, List<Guid> FromStates, Guid ToState);
record WorkflowDefinition(Guid Id, string Name, List<State> States, List<ActionDef> Actions);
record HistoryEntry(Guid ActionId, DateTime Timestamp);
record WorkflowInstance(Guid Id, Guid DefinitionId, Guid CurrentStateId, List<HistoryEntry> History);
