# Phase 1: Application foundation

## Scope

This phase establishes the executable foundation only: project references, dependency-injection composition, validated configuration, Problem Details, global exception handling, correlation IDs, structured logging, and health checks. It deliberately introduces no business entities, persistence model, external integration, or database migration.

## Decisions

### Dependency direction

Dependencies point inward. Domain remains independent; Contracts does not reference API or Infrastructure; Application references Domain and Contracts; Infrastructure references Application, Domain, and Contracts. API and Worker are composition roots and reference Application, Infrastructure, and Contracts. Test projects follow the dependency boundaries defined for their test level.

Each executable calls `AddApplication` and `AddInfrastructure`. These methods are intentionally small in Phase 1 and are the stable registration points for later feature modules and technical adapters.

### Configuration and transport security

API host and correlation-ID settings use strongly typed options with startup validation. CORS is deny-by-default because no origins are configured in the base settings. Configured origins must use HTTPS, except for HTTP loopback origins used during local development. Host filtering defaults to `localhost` and must be overridden with the deployed host name.

### Error boundary

The API uses the .NET 8 exception-handler abstraction and Problem Details service. Expected malformed requests map to 4xx responses; unexpected exceptions map to a sanitized 500 response. The full exception is retained in structured server logs, while the response exposes only stable public text and the request trace identifier.

### Request correlation and logging

A bounded, character-restricted incoming correlation ID is reused when safe. Missing, duplicated, oversized, or unsafe values are replaced with the current distributed-trace ID or a generated identifier. The effective value becomes `HttpContext.TraceIdentifier`, is returned in the response header, and is attached to a logging scope. Both hosts emit UTC JSON console logs with scopes enabled.

### Health model

The API exposes separate liveness and readiness paths. Both contain a self-check in this phase. Future infrastructure checks belong only to readiness, so an unavailable dependency will not cause an orchestrator to restart a healthy process repeatedly. The Worker deliberately opens no HTTP listener; health publishing for that process will be added with its hosting adapter when background infrastructure is introduced.

## Database changes

None. Phase 1 contains no `DbContext`, database provider, entity mapping, or migration.
