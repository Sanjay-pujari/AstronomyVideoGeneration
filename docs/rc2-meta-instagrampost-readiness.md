# RC2 Meta OAuth and InstagramPost readiness

## Certification boundary

This audit reuses the sole Meta OAuth implementation and does not perform a live
social operation. The repository checkout contains neither a Meta app secret nor
a persisted Meta token file, so a real account cannot honestly be certified as
healthy from this environment. The final operator step is the existing OAuth
start route followed by the governed dry-run request.

## OAuth and identity authority

1. **Start:** `GET /api/metaoauth/start` (or `?redirect=true`) is implemented by
   `MetaOAuthController.Start`.
2. **Callback:** `GET /api/metaoauth/callback` is implemented by
   `MetaOAuthController.Callback`. It validates a cryptographically random,
   HttpOnly, ten-minute state cookie with a fixed-time comparison before passing
   the code to the service.
3. **Service:** `MetaOAuthService.BuildAuthorizationUrl` and
   `MetaOAuthService.CompleteSetupAsync` are the only Meta OAuth flow.
4. **Token store:** `MetaOAuthService.PersistTokenFileAsync` uses
   `AtomicTokenFile.WriteAsync`.
5. **Token path:** `Meta:TokenFilePath` when set; otherwise
   `<AppContext.BaseDirectory>/meta-oauth-token.json`. Redacted diagnostics are
   written alongside it as `meta-oauth-result.json`.
6. **Authorization request:** endpoint
   `https://www.facebook.com/v23.0/dialog/oauth`, redirect URI
   `https://localhost:59235/api/metaoauth/callback`, and permissions
   `pages_manage_posts`, `pages_read_engagement`, `pages_show_list`,
   `instagram_basic`, and `instagram_content_publish`.
7. **Permission comparison:** those five permissions cover Page discovery,
   Facebook Page publishing/verification, Instagram identity discovery, and
   Instagram content publishing used by the listed image and video targets.
   No permission is missing for those operations. `business_management` was
   removed because this implementation does not call a Business Manager API.
   Production use of publishing permissions commonly requires Meta app review
   and advanced access; that provider-console status cannot be inferred locally.
8. **Persisted model:** selected Page ID/name/access token, Instagram professional
   account ID/username/name/account type, long-lived user access token,
   generated/expiry timestamps, reauthorization flag, and granted permissions.
   The short-lived token and authorization code are never persisted. Meta does
   not issue or store a Google-style refresh token here.
9. **Long-lived behavior:** the callback exchanges the short-lived user token
   with `fb_exchange_token`. Health reports `CanRefresh=false`; invalid, revoked,
   or expired authorization is `ReauthorizationRequired` and RC2 maps it to
   `RC2_PUBLISH_REAUTHORIZATION_REQUIRED` with `/api/metaoauth/start`. The code
   does not claim a nonexistent refresh-token capability.
10. **Discovery:** `/me/accounts?fields=id,name,access_token` discovers manageable
    Pages. Selection fails closed when configured ID/name does not match. The
    selected Page token discovers `instagram_business_account`, then reads
    `username,name,account_type` for that professional account. The callback
    debugs the long-lived token and rejects missing grants before atomically
    replacing authorization state.
11. **Expected Page:** ID `1135323479659435`, name `AstroPulse`.
12. **Expected Instagram identity:** username `sanjaypujari25`; ID, display name,
    and account type are provider-discovered and deliberately not guessed.
13. **Identity result:** not certified in this checkout because no authorization
    artifact exists. A mismatch prevents persistence.
14. **Health authority:** `TokenHealthService.CheckMetaAsync` reads the same token
    path as publishers/OAuth, calls `debug_token`, checks expiry and grants, then
    validates Page and Instagram identities. Expected post-OAuth status is
    `Healthy`; current local result is `MissingInitialAuthorization`.

## Governed media and zero-side-effect dry-run

15. **InstagramPost role:** `Rc2PublishingExecutionService.ResolveArtifacts`
    requires exactly `HeroPortrait`; there is no square fallback, language
    selection, directory scan, or filesystem guessing.
16. **Path:** obtained only from the committed Phase 20 authority and resolved
    beneath the plan output root. The concrete plan is unavailable in this
    checkout, so no path is fabricated.
17. **Artifact validation:** the executor checks regular-file existence, exact
    Phase 20 byte length, and SHA-256 before credential preflight. InstagramPost
    additionally identifies the unchanged asset as JPEG, width 320–1440, and
    aspect ratio 4:5 through 1.91:1. Concrete bytes/SHA require the runtime plan.
18. **Staging:** `AzureBlobPublicMediaStorageService` implements
    `IPublicMediaStorageService`. InstagramPost preflight validates
    `MetaPublishing:PublicMediaUploadEnabled` plus enabled Azure Blob provider,
    connection string, container, SAS lifetime, and provider media compatibility.
19. **Dry-run staging calls:** zero. Preflight reads configuration and the local
    governed file only; it never calls `IPublicMediaStorageService`.
20. **Dry-run container calls:** zero.
21. **Dry-run Instagram publish calls:** zero (and Facebook calls are zero).
22. **Dry-run result:** after a successful real OAuth callback and valid runtime
    Phase 20 artifact/storage configuration, the existing early return produces
    HTTP 200, `overallStatus=Succeeded`, `publicationState=NotPublished`, null
    failure fields, and `dryRunPassed=true`. It was not falsely reported as run
    here because credentials and the named plan are absent.

## Live design and remaining blocker

23. **Retry/checkpoint design:** the intended implementation must use the shared
    RC2 idempotency key, database compare-and-swap claim, and stale-Publishing
    lease. Sequence: credential preflight; authority/file verification; claim;
    public staging; create container; immediately persist container ID; bounded,
    configurable polling (`Processing`, `Ready`, `Failed`, or
    `RemoteOutcomeUnknown`); publish; immediately persist returned media/post ID;
    remote verification; `Published`. A retry resumes from either durable remote
    ID and must never recreate solely because local status is `Failed`. Normalized,
    bounded failure persistence remains shared with YouTube.
24. **Remaining blocker before live InstagramPost:** governed image-post provider
    execution and its container/media checkpoint schema are intentionally not
    implemented by this certification; `PublishProviderAsync` still fails closed
    for image posts. Before any first live operation, implement that sequence,
    complete Meta app review/advanced access, supply storage credentials, run the
    existing OAuth flow, verify the discovered identities, and obtain explicit
    operator approval. No live test is part of this change.

Operator verification request (after OAuth):

```http
POST /api/rc2/publishing/media
Content-Type: application/json

{
  "planId": "baa5af31-4ba9-4d1d-8ef3-0796210a9ed2",
  "mediaTypes": ["Hero"],
  "targets": ["InstagramPost"],
  "dryRun": true
}
```

Then inspect `GET /api/rc2/publishing/status/baa5af31-4ba9-4d1d-8ef3-0796210a9ed2`;
InstagramPost must be configured/enabled with healthy credentials and remain
`NotPublished`.
