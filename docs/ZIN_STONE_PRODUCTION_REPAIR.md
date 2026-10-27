# ZIN STONE production repair

Production API: https://zain-qoute.runasp.net

Frontend: https://zin-stone-quote-frontend.vercel.app

The FTP host is site102307.siteasp.net. It is not an application URL.

## Release safeguards

Only deploy to site102307 and connect to database db76143 on db76143.public.databaseasp.net. Production startup checks the connection string and authenticated DB_NAME() before initialization. Production schema changes require an explicit backed-up migration; startup does not run EnsureCreated or the legacy bulk schema upgrade.

Before deployment, back up every application/storage file and all database tables. Keep secret-bearing backups encrypted outside Git. Verify decryption and hashes. Database recipient/outbox snapshots taken during remediation remain in dedicated backup tables; do not drop them. Historical recipient rows remain deactivated, not deleted.

Run the SQL Server migration database/migrations/05_whatsapp_test_delivery.sql only after backup. It is additive and validates DB_NAME(). Rollback disables WhatsApp and retains the new audit table.

Publish a complete consistent set of DLLs, deps.json, runtimeconfig.json, templates and web.config. The previous IIS build failed because the dependency manifest requested an EF Core assembly version missing from the deployment. Use app_offline.htm only during the file swap. Never overwrite App_Data. Verify remote hashes, remove app_offline.htm, then verify the public API and logs. Rollback restores the encrypted pre-release file snapshot and production configuration, while retaining current documents and database rows.

## Notifications

Telegram:Enabled=false and Telegram:OutboxWorkerEnabled=false. No startup recipient import exists. Disabled services cannot schedule, send or retry outbox entries, including manual retry requests. Leave both flags false.

WhatsApp:Enabled=false. The old webhook path also requires WhatsApp:LegacyWebhookEnabled, which is absent/false. No automatic caller or background sender is registered for Meta delivery. Email:Enabled is absent/false during validation.

## Controlled Meta test configuration

Provision all secrets server-side, with credentials belonging exclusively to the Meta test phone number:

- WhatsApp__Enabled=true only for an explicitly approved test invocation.
- WhatsApp__TestMode=true; disabling test mode disables this implementation.
- WhatsApp__ApprovedTestRecipients__0, __1, etc.: approved test numbers in international digit-only form. Empty means no sends.
- WhatsApp__ApiVersion: the Meta API version approved in the owner's current Meta application; no version fallback exists.
- WhatsApp__PhoneNumberId and WhatsApp__AccessToken: dedicated test-number credentials.
- WhatsApp__DocumentTemplateName and WhatsApp__TemplateLanguage: approved document-header template with two body parameters, quotation number and customer name, in that order.
- WhatsApp__ConfirmedServiceWindowOpen defaults false. Set true only after confirming the recipient's service window; otherwise the approved template is mandatory.
- Optional WhatsApp__WebhookEnabled=true, WhatsApp__AppSecret and WhatsApp__WebhookVerifyToken for /api/whatsapp/status. Callback signatures and phone-number identity are verified.

The document argument is an existing private PDF storage basename, never a URL. Attempts persist media/message IDs and file hashes under a unique durable key. Uploads have bounded retries; message POSTs are never retried automatically because an ambiguous result may already have delivered. Claimed/Submitting/UnknownOutcome records require manual reconciliation before any new attempt. Do not remove the idempotency record to retry blindly.

Official formats: https://developers.facebook.com/docs/whatsapp/cloud-api/reference/media and https://developers.facebook.com/docs/whatsapp/cloud-api/reference/messages

## Credentials

Missing, placeholder, and known legacy signing keys are rejected. Production requires a dedicated random key. Do not accept compromised keys as previous keys. Existing document links can be refreshed by authorized quotation reads; signing-key rotation invalidates old links and sessions.

Owner password rotation requires an authenticated Owner session plus the current password at POST /api/auth/rotate-owner-password. It replaces the stored hash and session salt. Admin rotation through the Owner-authorized change-password endpoint also replaces the stored hash and salt. Merely editing initial bootstrap configuration does not rotate existing records.

Use hidden PowerShell input and Windows DPAPI outside Git to provision passwords. Verify database identity, restore-check the encrypted backup, and preserve a recoverable encrypted credential snapshot before rotation. Verify new login and old-token rejection before completing the operation. Do not write plaintext credentials to command output, logs or reports.

## Verification

Run dotnet test security-tests/FactoryQuoteApi.SecurityTests.csproj -c Release, node security-tests/xss-regression-tests.cjs, node security-tests/isolation-source-tests.cjs and the responsive browser suite. Production acceptance additionally requires actual authentication, a clearly labeled synthetic quotation, PDF layout inspection, secure retrieval, SQL persistence, dashboard visibility and zero notification activity. Synthetic-host or mocked browser checks alone do not establish production acceptance.
