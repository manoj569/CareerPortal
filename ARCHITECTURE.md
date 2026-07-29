# Job Portal Architecture

## Dependency direction

The solution follows a layered architecture:

```text
Domain       Shared
   \          /
    Application
      /     \
Infrastructure Persistence
         \   /
           API
```

- `JobPortal.Domain` contains entities, enums, and domain constants. It has no framework dependencies.
- `JobPortal.Shared` contains transport-neutral response models.
- `JobPortal.Application` owns use cases, validation, DTOs, and dependency abstractions.
- `JobPortal.Infrastructure` implements external concerns such as JWT, hashing, SMTP, and Razorpay.
- `JobPortal.Persistence` implements EF Core repositories, mappings, migrations, and the unit of work.
- `JobPortal.API` is the composition root and HTTP transport.

References must continue to point inward. Domain code must not reference EF Core, ASP.NET Core, or infrastructure implementations.

## API conventions

- Public endpoints use `/api/jobs`.
- Authenticated user endpoints use `/api/dashboard`, `/api/memberships`, and `/api/payments`.
- Candidate-only profile, resume, saved-job, and application endpoints use `/api/candidate`.
- Administrator endpoints use `/api/admin`.
- Collection endpoints are paginated and capped at 100 items.
- All database and external I/O is asynchronous and accepts a `CancellationToken`.
- Expected failures use `AppException`; unhandled implementation details are never returned to clients.
- UTC timestamps use the `Utc` suffix. Services use `TimeProvider` for deterministic testing.

## Data access

- Read-only queries use `AsNoTracking` and project to response DTOs in SQL.
- Sorting fields are allow-listed; user input is never interpolated into SQL.
- Soft-delete query filters are enabled for all `BaseEntity` types.
- Payment and membership transitions use SQL Server row-version concurrency tokens.
- SQL retry is limited to transient failures; command timeout is 30 seconds.
- The DbContext is pooled. Scoped services must never retain entity or DbContext references beyond a request.
- Migrations are the only supported schema-change mechanism.

## Security boundaries

- JWT signing keys, Razorpay secrets, SMTP credentials, and production connection strings belong in environment variables or a secret manager.
- Razorpay amounts and plan duration are server-controlled.

## Initial Administrator bootstrap

The API can create the first Administrator through a disabled-by-default, idempotent startup initializer.
Configure `BootstrapAdmin:Enabled`, `Email`, `Password`, `FirstName`, and `LastName` through
.NET User Secrets locally or a production secret manager. Never place the credentials in an
appsettings file. Set `BootstrapAdmin:Enabled` back to `false` after the first Administrator has
been created. The initializer does not apply database migrations and will never elevate an
existing non-Administrator account.
- Payment signatures use constant-time verification.
- Password changes and resets revoke active refresh tokens.
- Authentication endpoints have stricter per-client rate limits.
- Output caching is restricted to anonymous public-job reads and varies by query and origin.
- Forwarded headers are accepted only from configured trusted proxies.

## Candidate profiles and applications

Candidate endpoints require the `Candidate` role and re-check that the current account is both
email-verified and Active. Every profile, resume, saved-job, and application query is scoped by
the authenticated user's identifier; client-supplied candidate identifiers are never accepted.

- `GET|PUT /api/candidate/profile`
- `PUT|GET|DELETE /api/candidate/resume`
- `GET /api/candidate/saved-jobs`
- `PUT|DELETE /api/candidate/saved-jobs/{jobId}`
- `POST /api/candidate/jobs/{jobId}/applications`
- `GET /api/candidate/applications`
- `GET /api/candidate/applications/{applicationId}`
- `POST /api/candidate/applications/{applicationId}/withdraw`

Resume storage is abstracted behind `IResumeStorage`. The default local implementation generates
opaque server-side keys and writes beneath `ResumeStorage:RootPath`, which must not be inside a
`wwwroot` path. Uploads are limited to 5 MB and require matching PDF, legacy DOC, or DOCX
extension, media type, and file signature. Production deployments should configure a persistent
private volume or replace the implementation with private object storage, and should add malware
scanning and retention policies. Resume files referenced by submitted applications are retained
when a candidate replaces or removes the current resume.

Applications require an active portal-wide membership and an available published, visible,
unexpired job. A unique database index prevents any duplicate application for a candidate/job
pair. Candidates can withdraw only applications still in `Submitted` status.

## Administrator job lifecycle

All job-management routes under `/api/admin/jobs` require the exact `Administrator` role. The
paginated list supports company, category, status, featured, expiry-range, and keyword filters;
detail and update operations use the existing validated job DTOs.

- `GET /api/admin/jobs`
- `GET /api/admin/jobs/{id}`
- `PUT /api/admin/jobs/{id}`
- `POST /api/admin/jobs/{id}/publish`
- `POST /api/admin/jobs/{id}/unpublish`
- `POST /api/admin/jobs/{id}/close`
- `POST /api/admin/jobs/{id}/archive`
- `POST /api/admin/jobs/{id}/feature`
- `POST /api/admin/jobs/{id}/unfeature`

Publishing revalidates the complete job, its non-deleted company and category, and a mandatory
future expiry date. Unpublishing returns a Published job to Draft; closing produces Closed;
archiving is final. Feature status is removed whenever a job is unpublished, closed, archived,
hidden, or automatically expired. Public output-cache entries are evicted after administrator
lifecycle changes.

`JobExpiryHostedService` runs a configurable UTC cycle (`JobExpiry:Enabled`,
`IntervalMinutes`, and `RunOnStartup`). It performs one conditional database update from
Published to Expired for overdue rows, making repeated runs idempotent, and does not run
migrations. Only visible Published jobs with a future expiry are eligible for public, related,
featured, saved-job, or new-application queries. Existing applications remain queryable after
the associated job closes or expires.

## Administrator application review

Application-review endpoints require the exact `Administrator` role:

- `GET /api/admin/applications` accepts job, company, category, status, submitted-date, and
  keyword filters together with capped pagination.
- `GET /api/admin/applications/{applicationId}` returns the review-safe candidate profile, job
  summary, cover letter, current status, and status history.
- `GET /api/admin/applications/{applicationId}/resume` streams the application-time resume
  snapshot from private storage. Storage keys and public resume URLs are never returned.
- `PUT /api/admin/applications/{applicationId}/status` supports `Reviewed`, `Shortlisted`, and
  `Rejected` under the application transition rules.

Every transition records its previous and new status, the authenticated actor, UTC timestamp,
and an optional administrator-only note. Candidate application DTOs do not include status
history or internal notes. Shortlist and rejection notification attempts run only after the
database commit and receive no internal-note value, so an SMTP failure cannot revert the review.
For production-scale delivery, replace direct SMTP notification with a durable transactional
outbox and worker.

To exercise the flow in Swagger, sign in as an Administrator, authorize with the access token,
list applications to obtain an identifier, open its detail, optionally download its resume, then
send `{"status":"Reviewed","internalNote":"Reviewed in Swagger"}` to the status endpoint.
Follow with `Shortlisted` or `Rejected`; final and withdrawn applications must return a conflict.

## Transactional email

Email verification and password reset use direct SMTP delivery after token state has been
committed. Configure SMTP credentials through User Secrets or a production secret manager and
set `Email:Enabled` only when all required email settings are available. Delivery failures do not
roll back token state, allowing resend or another password-reset request to recover. A durable
transactional outbox should be considered before scaling production delivery.

## Razorpay Test Mode payments

The only purchasable plan is a portal-wide ₹99 INR Candidate membership lasting 30 days.
Payment orders are first persisted locally in `Created` state and become `Pending` only after
Razorpay returns matching server-requested order details. Checkout confirmations, raw-body
webhooks, and reconciliation all pass through `IRazorpayGateway`; membership is activated or
extended only after a verified signature or a provider-confirmed captured payment.

Provider order IDs, payment IDs, and webhook event IDs remain globally unique even if a local
record is soft-deleted. Candidate reads and confirmations are owner-scoped. The webhook is
anonymous only at the HTTP authentication layer and rejects every request without a valid
Razorpay webhook HMAC. Configuration and manual testing are documented in
`RAZORPAY_TEST_MODE.md`. Refunds remain a future audited administrative workflow.

## Scaling guidance

- API instances are stateless and can scale horizontally.
- The current output cache is per process. Use a distributed output-cache store when deploying multiple instances if cache consistency becomes important.
- File logging is suitable for local/single-node operation. Production deployments should ship structured logs to a centralized sink.
- For large job catalogs, replace `Contains` keyword search with SQL Server full-text search or an external search index.
- Revenue remains grouped by currency; cross-currency totals require an explicit exchange-rate service and accounting date.

## Build policy

`Directory.Build.props` enables recommended .NET analyzers and treats every warning as an error. Generated EF migrations have only the generated-code allocation rule suppressed. A successful build must report zero warnings.
