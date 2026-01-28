# Contributing to CloudScale Event Intelligence Platform

Thank you for your interest in the CloudScale Event Intelligence Platform. This document outlines the terms and standards for contributing.

## 1. License Agreement & User Understanding

**IMPORTANT:** This repository accepts contributions under the explicit understanding that:

1.  **License**: All contributions (including code, documentation, scripts, and assets) are automatically licensed under the **Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0)** license.
2.  **Commercial Restriction**: By submitting a Pull Request, you acknowledge that this codebase is for **Non-Commercial use only**. You agree not to use contributions to this repository for commercial advantage or monetary compensation.
3.  **No Commercial Expectations**: You confirm that your contribution does not introduce or imply any expectation of commercial support, warranty, or liability from the Project Owner.

## 2. Architectural Scope & Boundaries

To maintain the high-performance nature of this platform, we enforce strict architectural boundaries:

*   **Infrastructure Dependencies**: You may **NOT** introduce new infrastructure components (e.g., new databases, message queues, cloud services) without a prior approved **Architectural Decision Record (ADR)**.
*   **Performance Impact**: Any change that impacts the critical ingestion path (e.g., adding reflection, synchronous I/O, or heavy allocations) will be rejected unless accompanied by benchmarks proving no regression.
*   **Breaking Changes**: API contracts are stable. Breaking changes are generally not accepted without a compelling, approved migration path.

## 3. How to Contribute

1.  **Fork & Branch**: Create a feature branch from `main`.
2.  **Code Standards**:
    *   Follow standard .NET 10 / C# conventions.
    *   Cover all logic with Unit Tests (`tests/CloudScale.Shared.Tests`).
    *   Cover system flows with Integration Tests (`tests/CloudScale.Integration.Tests`).
3.  **Commit Messages**: Use Conventional Commits (e.g., `feat: add resilience policy`, `fix: resolving null reference`).
4.  **Pull Request**:
    *   Fill out the PR template completely.
    *   Acknowledge the legal checklist.
    *   Understand that rejection is possible and may occur without detailed justification if the contribution does not align with the roadmap.

## 4. Reporting Bugs

Please use the provided **Bug Report** template.
*   Issues that cannot be reproduced on the `main` branch may be closed.
*   Issues lacking environmental details (Docker version, OS) will be asked for clarification.
