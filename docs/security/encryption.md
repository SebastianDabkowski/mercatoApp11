## Encryption controls

- **Data in transit**: HTTPS is enforced with HSTS and 308 redirects. Terminate TLS with modern ciphers (TLS 1.2+), disable HTTP on edge/load balancers, and pin health checks to HTTPS.
- **Data at rest**: SQL Server and backups must use storage-level encryption (TDE/backup encryption). Object storage (blobs/log archives) must enable server-side encryption with customer-managed keys. Application-level encryption protects payout credentials, tax IDs, company registration numbers, and personal ID numbers.
- **Key management**: Data Protection keys rotate every 90 days and persist to `Security:KeyRingPath` (keep this on encrypted storage). Keys must be backed by a managed KMS (e.g., Azure Key Vault or AWS KMS on the storage account/volume). Do not store secrets in source control.

## Field-level encryption

| Area                        | Fields                                          | Mechanism                           |
|-----------------------------|-------------------------------------------------|-------------------------------------|
| Seller payouts              | `PayoutAccount`, `PayoutBankAccount`, `PayoutBankRouting` | ASP.NET Data Protection             |
| Seller verification/KYC     | `TaxId`, `CompanyRegistrationNumber`, `PersonalIdNumber` | ASP.NET Data Protection             |

## Runbooks

- **Key rotation**: Data Protection rotates automatically every 90 days. To force a rotation, delete expired keys from the key ring path after ensuring at least one active key remains. Rotate underlying KMS keys on the storage backend per cloud guidance; application will transparently consume re-encrypted key rings.
- **Incident response**: If a key is suspected compromised, revoke it from the key ring storage, force regeneration by recycling the app, and re-encrypt persisted secrets (payout/KYC fields) by loading each record and saving it to trigger protection with the new key. Audit access to the key ring storage and KMS, and rotate database/storage encryption keys.

## Deployment checklist

- Set `Security:KeyRingPath` to a persisted, encrypted location (e.g., mounted volume encrypted by KMS).
- Ensure load balancer/application gateway forces HTTPS and supports TLS 1.2+ (prefer TLS 1.3).
- Enable database TDE and encrypted backups; ensure blob/log storage uses SSE with customer-managed keys.
- Back up and monitor the Data Protection key ring location with restricted access (ops-only).
