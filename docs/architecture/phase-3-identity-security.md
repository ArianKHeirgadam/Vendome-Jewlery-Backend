# Phase 3: Identity, authorization, and sessions

## Scope

Phase 3 implements the authentication and authorization boundary: ASP.NET Core Identity, the three base roles, permission policies, JWT access tokens, rotating refresh tokens, revocable sessions, account lockout, and authenticator-app MFA enrollment. It adds no product, inventory, order, payment, invoice, Worker, or SignalR behavior. It also does not add public registration or email delivery; confirmed accounts other than the bootstrap Owner require a later administration and notification workflow.

Controllers receive request contracts and delegate all security behavior through `IAccountAuthenticationService`. Identity, EF Core, hashing, token issuance, and bootstrap logic remain in Infrastructure. Domain security entities expose lifecycle methods but do not depend on ASP.NET Core or EF Core.

## Roles and permissions

`Owner`, `Admin`, and `Customer` are idempotently created as system roles. The 21 permissions defined in the project specification are stored as rows and exposed as authorization policies with the same stable names. The Owner role receives every active permission. Admin starts with no implicit permission; grants must be explicit. Customer authorization in later business phases must still perform resource-ownership checks using the authenticated subject claim.

Access-token validation loads current role and permission assignments from SQL Server. Role or permission claims are not trusted merely because they existed when the JWT was issued. The pure `AccountAdministrationPolicy` records the invariants that an Admin cannot manage an Owner, an Admin cannot grant access they do not possess, and the final active Owner cannot be removed or deactivated. Administration endpoints will apply those rules when user management is introduced.

## Token and session lifecycle

- Access tokens use HMAC-SHA256 with a base64-encoded key of at least 32 random bytes. Issuer, audience, signature, algorithm, lifetime, token use, session ID, token ID, and security-stamp hash are validated. Access tokens last 10 minutes by default.
- JWTs contain identity and session references, not current permissions. A database validation step rejects inactive or unconfirmed users, expired or revoked sessions, changed security stamps, and privileged sessions that did not complete MFA.
- Refresh tokens contain 64 random bytes. Only their SHA-256 hashes are stored. They are single-use and rotate inside a database transaction. A token family cannot extend beyond the fixed session lifetime.
- Reusing a consumed token revokes its complete token family and session and writes a critical security event. A rowversion conflict during concurrent refresh is treated as reuse, not as a successful second refresh.
- Logout revokes one session and its tokens. Logout-all revokes every session and changes the Identity security stamp. Users can list and revoke only their own sessions through the Phase 3 API.
- Passwords, JWTs, refresh tokens, MFA keys, recovery codes, and connection strings are never written to application logs.

## MFA flow

Owner and Admin access requires MFA. A valid password for a privileged account without MFA returns a short-lived `mfa_enrollment` ticket instead of access credentials.

1. `POST /api/v1/auth/mfa/setup` validates the enrollment ticket, creates an authenticator key when necessary, and returns the manual key, `otpauth` URI, and a replacement enrollment ticket.
2. Identity changes the security stamp when it creates the authenticator key. The original ticket is therefore invalid immediately; only the replacement ticket can complete setup.
3. `POST /api/v1/auth/mfa/enable` validates a TOTP code, enables two-factor authentication, rotates the security stamp again, returns ten one-time recovery codes, and creates the first authenticated session.
4. Later logins accept either an authenticator code or one recovery code, never both. Invalid second factors contribute to account lockout.

The enrollment endpoints are anonymous only in the HTTP authorization sense; their signed, purpose-limited ticket is required and cannot authenticate as an access token. Production Data Protection keys must be persisted outside an ephemeral container before email-confirmation or password-reset token delivery is added.

## Brute-force and API controls

Identity requires confirmed email, unique email addresses, a minimum 12-character mixed password, PBKDF2 Identity V3 hashing with 210,000 iterations, five failed attempts, and a 15-minute lockout by default. Unknown and locked accounts perform a dummy password verification, and all credential failures return the same public 401 response.

Login, refresh, and MFA use independent, configuration-driven fixed-window rate limits partitioned by the connection IP. A reverse proxy must replace `RemoteIpAddress` only from trusted proxy networks; arbitrary forwarded headers must not be accepted. Application rate limiting reduces abuse but is not a DDoS guarantee. Production still requires a WAF or reverse proxy, origin protection, connection limits, monitoring, and alerting.

CORS remains deny-by-default, HTTPS redirection and production HSTS remain enabled, authentication responses are marked `no-store`, and API responses receive defensive content-type, framing, referrer, permissions, and CSP headers. Swagger remains Development-only.

## Configuration and bootstrap

`Jwt:SigningKey` and `ConnectionStrings:GoldInvoice` are deliberately empty in base settings. They must come from environment variables, .NET user secrets for local development, or a production secret manager. JWT options fail startup for missing or short keys.

Owner bootstrap is disabled by default and requires email, display name, and a password only when explicitly enabled. It creates an Owner only when none exists, confirms the bootstrap email, requires MFA, and never changes the password of an existing account. Bootstrap values must be removed after the first successful startup.

No schema change is required in Phase 3. Identity, role-permission, session, refresh-token, login-attempt, trusted-device, and security-event tables were created by `InitialDomainModel`; EF reports no pending model changes.

## Verification

The Phase 3 tests exercise Owner protection, last-Owner continuity, permission-grant limits, Identity lockout, JWT signature and live-session validation, role enrichment, MFA enrollment with an RFC 6238 TOTP, recovery-code generation, refresh rotation and reuse response, session revocation, explicit Owner bootstrap, idempotent role/permission bootstrap, option validation, and registration of every permission policy. EF InMemory is used only for Identity orchestration tests; SQL Server-specific types, constraints, indexes, delete behavior, and migrations remain covered by the relational metadata tests from Phase 2.
