# ADR-004: Agent Framework Workflow over a Provider-Agnostic Chat Client

> Status: Accepted
> Date: 2026-10-30

## Context

The owner imposed .NET with Microsoft Agent Framework and wants each repository to choose OpenAI, Azure OpenAI, or Anthropic. The review must end in deterministic validation, not in the model's word. See [PRD-001](../prds/PRD-001-pull-request-review-action.md).

## Decision

We will run the review as a `WorkflowBuilder` workflow with context, review, and validation executors, where the review executor is a `ChatClientAgent` returning a typed `ReviewResult`, built on a `Microsoft.Extensions.AI` `IChatClient` that a factory creates from the configured provider (`Microsoft.Extensions.AI.OpenAI` for OpenAI and Azure OpenAI, the official `Anthropic` SDK for Claude).

## Consequences

- Swapping providers is configuration, and tests use a fake `IChatClient`.
- Providers differ in structured-output support; a parse failure gets one repair request before the run fails.
- The framework releases weekly; versions are pinned centrally and upgraded deliberately.

## Alternatives Considered

- **Calling provider SDKs directly:** no shared abstraction, three code paths, and no workflow structure for the validation step.
- **A single provider:** fewer dependencies, but adopters would be tied to it.
