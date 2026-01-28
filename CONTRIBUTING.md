# Contributing to CloudScale Event Intelligence Platform

First off, thank you for considering contributing to the CloudScale Event Intelligence Platform!

## ⚠️ License Notice: CC BY-NC 4.0

**IMPORTANT:** This project is licensed under the **Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0)** license.

*   By contributing to this repository, you agree that your contributions will be licensed under the same terms.
*   **Commercial use of this codebase is strictly prohibited.**
*   You cannot sell, re-license, or use this code for commercial advantage without explicit permission from the author.

## How to Contribute

1.  **Fork the Repository**: Create your own fork of the code.
2.  **Create a Branch**: `git checkout -b feat/my-new-feature`
3.  **Commit Changes**: Use Conventional Commits (e.g., `feat: add new ingestion metric`).
4.  **Test**: Ensure `dotnet test` passes locally.
5.  **Push**: `git push origin feat/my-new-feature`
6.  **Pull Request**: Submit a PR to the `main` branch.

## Coding Standards

*   **Language**: C# (.NET 10 / .NET 8 LTS)
*   **Style**: Follow standard .NET coding conventions.
*   **Testing**: All new logic must have corresponding Unit or Integration tests.
*   **Architecture**: Respect the clean separation between Ingestion, Processing, and Shared libraries.

## Reporting Bugs

Please use the **Bug Report** template to submit issues. detailed reproduction steps are required.
