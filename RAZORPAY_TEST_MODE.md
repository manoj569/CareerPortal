# Razorpay Test Mode

The backend supports one portal-wide Candidate membership: **₹99 INR for 30 days**. Pricing,
currency, plan name, and duration are read from trusted backend configuration. The order request
contains no client-controlled amount. `RazorpayGateway` rejects any Key ID that does not begin
with `rzp_test_`.

## Required User Secrets

From the solution directory, configure Test Mode credentials without editing appsettings:

```powershell
dotnet user-secrets set "Razorpay:KeyId" "rzp_test_your_key_id" --project JobPortal.API
dotnet user-secrets set "Razorpay:KeySecret" "your_test_key_secret" --project JobPortal.API
dotnet user-secrets set "Razorpay:WebhookSecret" "your_test_webhook_secret" --project JobPortal.API
```

Production deployments should provide the equivalent environment variables
`Razorpay__KeyId`, `Razorpay__KeySecret`, and `Razorpay__WebhookSecret` through their secret
manager. Never commit real values.

## Endpoints

- `POST /api/payments/razorpay/orders` — authenticated verified Active Candidate; body `{}`.
- `POST /api/payments/{paymentId}/razorpay/confirm` — verifies the checkout HMAC signature and
  confirms that Razorpay reports the payment as captured.
- `POST /api/payments/{paymentId}/razorpay/reconcile` — asks Razorpay for pending-order state.
- `GET /api/payments/status` — current membership and latest payment.
- `GET /api/payments` and `GET /api/payments/history` — paginated Candidate-owned records.
- `POST /api/payments/razorpay/webhook` — anonymous transport endpoint with mandatory Razorpay
  signature verification.

## Manual Swagger Test

1. Enable automatic capture in the Razorpay Test Mode Dashboard.
2. Apply migrations and start the API in Development.
3. Register and verify a Candidate, log in, then authorize Swagger with its bearer token.
4. Call `POST /api/payments/razorpay/orders` with `{}`. Confirm the response contains `9900`,
   `INR`, a Test Mode Key ID, the local `paymentId`, and the Razorpay `orderId`.
5. Open Razorpay Test Checkout from the frontend using only the returned checkout values and
   Razorpay test payment data. Do not use real customer payment details.
6. Paste checkout's `razorpay_order_id`, `razorpay_payment_id`, and `razorpay_signature` into
   `POST /api/payments/{paymentId}/razorpay/confirm`.
7. Repeat the same confirmation to verify idempotency, then call `GET /api/payments/status`.
8. For a pending order, call the reconciliation endpoint. It remains pending unless Razorpay
   reports a captured payment.

Swagger cannot manufacture a valid Razorpay signature. A frontend Test Checkout or a signed
Razorpay webhook is required for a successful verification test.

## Webhook Configuration

After a public HTTPS URL exists, create a Razorpay **Test Mode** webhook with:

- URL: `https://your-public-host/api/payments/razorpay/webhook`
- Secret: the same value stored in `Razorpay:WebhookSecret`
- Events: `payment.captured`, `payment.failed`, and `order.paid`

The endpoint verifies `X-Razorpay-Signature` against the unmodified raw request body and uses
`X-Razorpay-Event-Id` for idempotency. It does not require a bearer token. Do not proxy the
webhook through a component that rewrites the JSON body.

## Operational Limitations

- This module intentionally rejects Razorpay Live Mode keys.
- Membership activation requires a captured payment; configure Razorpay Test Mode automatic
  capture because this module does not call the capture API.
- Reconciliation is Candidate-triggered; a scheduled administrative reconciliation worker is
  not included.
- Refunds are not implemented. They require a future audited administrative workflow.
- A public HTTPS deployment is required before real webhook delivery can be configured.
- Only payment metadata is stored. Card, bank, and UPI credentials are never accepted or stored.
