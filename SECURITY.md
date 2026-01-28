# Security Policy

## Supported Versions

Only the latest `main` branch is currently supported for security updates.

| Version | Supported          |
| ------- | ------------------ |
| v1.0.x  | :white_check_mark: |
| < 1.0   | :x:                |

## Reporting a Vulnerability

If you discover a security vulnerability within this project, please **DO NOT** create a public GitHub issue.

Instead, please report it via email to the project maintainer (suaybbyaus@gmail.com).

### Service Level Agreement (SLA)
*   **Acknowledgement**: We aim to acknowledge receipt of valid reports within **72 hours**.
*   **Resolution**: Timeline depends on severity and complexity.

### Scope and Exclusions
We are interested in vulnerabilities within the **application logic** of the CloudScale platform.

The following are **OUT OF SCOPE** and reports may be closed without response:
*   Vulnerabilities in **local development emulators** (e.g., Azurite, Cosmos DB Emulator) which are not intended for production use.
*   Vulnerabilities in **third-party dependencies** (unless the usage pattern in this project exacerbates the issue).
*   Attacks requiring physical access to the user's infrastructure.
*   Social engineering attacks.

**Note:** Security reports that fall outside the defined scope may be closed without response.
