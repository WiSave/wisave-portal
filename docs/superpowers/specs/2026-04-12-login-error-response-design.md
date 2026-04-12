# Login Error Response Design

## Goal

Expand `POST /api/auth/login` so failed authentication responses explain why the request was rejected, while preserving the current success payload and status code.

## Approved Direction

- Keep successful logins unchanged: `200 OK` with `AuthResponse`.
- Keep failed logins on `401 Unauthorized`.
- Return a structured JSON body for `401` responses with:
  - a stable machine-readable `code`
  - a human-readable `message`
- Distinguish these failure cases:
  - `USER_NOT_FOUND`
  - `INVALID_PASSWORD`
  - `LOCKED_OUT`
  - `NOT_ALLOWED` when ASP.NET Identity reports that state

## API Shape

Use a small typed DTO instead of anonymous objects so endpoint metadata, tests, and callers share one contract.

Example:

```json
{
  "code": "INVALID_PASSWORD",
  "message": "The password is incorrect."
}
```

## Implementation Notes

- Add the DTO next to the existing auth DTOs in `src/WiSave.Portal/Auth/Models/AuthDtos.cs`.
- In `AuthEndpoints.Login`, branch on `SignInResult` so each failure case maps to the correct code/message pair.
- Unknown user should return the same typed `401` body instead of bare `Results.Unauthorized()`.
- Update minimal API metadata so Swagger documents the `401` payload type.

## Testing

- Extend `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`.
- Cover at least:
  - unknown email returns `401` with `USER_NOT_FOUND`
  - bad password returns `401` with `INVALID_PASSWORD`
  - locked out account returns `401` with `LOCKED_OUT`
- Keep existing success-path tests intact.
