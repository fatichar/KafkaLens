# AGENTS.md

## Project Overview

KafkaLens is a cross-platform desktop application built using .NET and
Avalonia UI. It focuses on Kafka inspection, message browsing, and
developer productivity.

## Tech Stack

-   .NET (latest LTS unless specified)
-   Avalonia UI
-   MVVM pattern
-   Microsoft.Extensions.DependencyInjection
-   Microsoft.Extensions.Logging

## Architecture Guidelines

-   Follow MVVM strictly.
-   No business logic inside Views.
-   Keep ViewModels UI-agnostic where possible.
-   Prefer composition over inheritance.
-   Avoid static/shared mutable state unless absolutely necessary.
-   Maintain existing folder and namespace conventions.

## Async & Threading

-   UI updates must run on Dispatcher.UIThread.
-   Avoid blocking calls (.Result, .Wait()).
-   Prefer async/await end-to-end.
-   Background work must never freeze the UI.
-   Be mindful of large Kafka payload handling.

## Performance Considerations

-   Assume large topics and high message volume.
-   Avoid unnecessary allocations.
-   Stream or paginate where possible.
-   Use UI virtualization for large collections.

## Packaging

-   Windows: Inno Setup installer.
-   Other platforms: zip distribution.
-   Icons and assets must be embedded properly.

## Change Approval Policy (Mandatory)

Agents MUST obtain explicit approval before implementing any non-trivial
change.

Non-trivial changes include: - Architectural modifications - Public API
changes - Adding or removing dependencies - Cross-cutting refactors -
Threading model changes - Persistence or serialization changes - Build,
CI/CD, or packaging modifications - Changes affecting multiple modules

Before implementation, provide a concise proposal including: 1. Problem
statement 2. Proposed approach 3. Alternatives considered (brief) 4.
Impacted components/modules

Do not proceed until clear approval is granted.