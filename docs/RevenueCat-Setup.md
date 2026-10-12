# RevenueCat setup

The server side is built: the webhook, plans, the weekly conversion limit and `GET /api/billing/entitlement` (see "Billing" in `API-Reference.md`). What remains is configuration in the stores and RevenueCat, the app SDK, and the webhook secret. Do the steps in order.

## What the server expects

| Thing | Value |
|---|---|
| Webhook URL | `https://swimrebase-pro.azurewebsites.net/api/billing/revenuecat` (staging: `https://swimrebase-dev.azurewebsites.net/api/billing/revenuecat`) |
| Webhook header | `Authorization: <the secret below>` (RevenueCat's "Authorization header value" field; a `Bearer ` prefix also works) |
| Secret config key | `RevenueCat:WebhookSecret` (Key Vault name `RevenueCat--WebhookSecret`) |
| RevenueCat user id | The user's id (the Supabase `sub`), set with `Purchases.logIn(userId)` |
| Sandbox purchases | Count as paid only where `Billing:AcceptSandbox` is true (Development and Staging; false in production) |
| Limits | `Billing:FreeWeeklyConversions` (2), `Billing:FairUseWeeklyConversions` (50) |

The server handles one paid line. It does not read entitlement ids, so any product that sends a purchase event grants Pro until its expiry. Keep to one subscription group.

## 1. Webhook secret

1. Generate a long random value, for example `openssl rand -base64 48`.
2. Staging and production each get their own: `az keyvault secret set --vault-name <vault> --name RevenueCat--WebhookSecret --value '<secret>'`, then restart the web app so it is picked up.
3. Locally: `dotnet user-secrets set "RevenueCat:WebhookSecret" "<secret>" --project src/Presentation/REST`.
4. Until it is set, the webhook refuses every request (`401`).

## 2. App Store Connect

1. Create the app record and a subscription group, for example "SwimRebase Pro".
2. Add a monthly auto-renewing subscription, product id `pro_monthly`.
3. Set prices by hand per territory rather than accepting the converted tiers: US$9.99 and **A$15.99** for Australia. Apple's AU price includes GST.
4. Do not add an introductory free trial; the 21-day trial is the app's own (it needs no payment method).
5. Fill in the localised name, description and review screenshot. Add a Sandbox tester account (Users and Access > Sandbox).
6. Create an App Store Connect API key or in-app purchase key for RevenueCat (RevenueCat's guide lists exactly what it asks for).

## 3. Google Play Console

1. Create the subscription `pro_monthly` with a monthly base plan.
2. Set prices per country by hand: US$9.99, A$15.99. Play shows tax-inclusive prices in Australia too.
3. Add licence testers (Settings > License testing).
4. Create the service account RevenueCat asks for and grant it the needed Play Console permissions.

## 4. RevenueCat dashboard

1. Create a project with an iOS app and an Android app; add the store credentials from steps 2 and 3.
2. Import the products, then create one entitlement, `pro`, and attach both products.
3. Create an offering (`default`) with a monthly package pointing at those products. Add a paywall (RevenueCat Paywalls) if you want RevenueCat to render it.
4. Integrations > Webhooks: add the URL above, set the authorization header value to the secret, choose the production and sandbox environments, and send a test event. A `200` response means the secret matches. Use the staging URL for sandbox purchases against staging.

## 5. App (frontend)

1. Install the RevenueCat SDK and configure it with the platform public API key.
2. After sign-in call `Purchases.logIn(userId)` with the user's id. Without this, purchases belong to an anonymous id and the server ignores them.
3. Show the paywall: after a `quota_exceeded` SSE event, from the "X of 2 conversions left" line, and at the end of the trial. Present the offering; show price, renewal terms, and links to the privacy policy and terms (App Review requires these).
4. Add "Restore purchases".
5. After a purchase or restore, refetch `GET /api/billing/entitlement`, retrying for a few seconds until `plan` is `pro` (the webhook can lag the app).
6. Gate on the server's plan, never on the SDK's `customerInfo` alone.

## Account deletion

Deleting an account also removes the user's RevenueCat subscriber record, which needs a **secret** API key (RevenueCat > Project settings > API keys > secret key, v1 read/write).

1. `az keyvault secret set --vault-name <vault> --name RevenueCat--SecretApiKey --value '<sk_...>'`, then restart the web app. Locally: `dotnet user-secrets set "RevenueCat:SecretApiKey" "<sk_...>" --project src/Presentation/REST`.
2. Deleting the Supabase user needs the project's service-role key: `az keyvault secret set --vault-name <vault> --name Supabase--ServiceRoleKey --value '<key>'` (Supabase dashboard > Project Settings > API). Locally: `dotnet user-secrets set "Supabase:ServiceRoleKey" "<key>" --project src/Presentation/REST`.
3. Until both are set, `POST /api/account/deletion/confirm` fails with a `5xx` after the database data has been removed; setting them and retrying completes the deletion.

## 6. Test it

1. Staging, sandbox tester: buy `pro_monthly`. In RevenueCat's customer view, confirm the event; in the app, confirm `plan: "pro"` and a higher `conversions.limit`.
2. Cancel in the sandbox; `subscription.willRenew` becomes `false` and `plan` stays `pro` until `expiresAt`. Sandbox subscriptions renew every few minutes and expire after a handful of renewals, so you can watch an expiry drop the plan back to `free`.
3. Use up the free conversions (a free user, or an expired trial): the third `read_swim_board` in a week should produce `quota_exceeded`.
4. Check production with a real small purchase, or a promotional entitlement, before announcing.

## Known gaps

- If the webhook is down for a long time, a missed event is not recovered automatically (there is no reconciliation against RevenueCat's REST API). RevenueCat retries failed webhooks for a while.
- The week resets Monday 00:00 UTC (10-11am Monday in Australia); there is no per-user timezone yet.
- No reminder emails (trial ending, limit reached). Those need an email provider and a scheduled job.
- Chat itself is not limited; only swim-board conversions are.
