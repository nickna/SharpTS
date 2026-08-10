# Release incident history

This file keeps package-publication incidents separate from the generic
[release runbook](releasing.md). Records are historical facts, not current package recommendations.

## v1.0.8 partial NuGet publication

Workflow run `29182139378` published `SharpTS` 1.0.8 and then received HTTP 403 while publishing
`SharpTS.LanguageServer`; wildcard publication stopped before `SharpTS.Sdk`. The tag expected
`SharpTS`, `SharpTS.LanguageServer`, and `SharpTS.Sdk`. `SharpTS.Hosting` was introduced later and
was not part of that tag.

The incident investigation recorded this public state:

| Package | v1.0.8 state | Recovery action |
| --- | --- | --- |
| `SharpTS` | Published | None; idempotent retries skip it. |
| `SharpTS.Sdk` | Missing | Rebuild from tag `v1.0.8`, publish the original-version artifact, and verify inventory. |
| `SharpTS.LanguageServer` | Unregistered | Resolve ownership and API-key scope, then publish the tag artifact or record an intentional exception. |
| `SharpTS.Hosting` | Not in tag | No v1.0.8 package is expected. |

The durable response was to replace wildcard publication with manifest-ordered, idempotent pushes,
preflight registration and artifact checks, and post-publish inventory verification. Operators
recovering any partial release should follow [Recovering a partial publish](releasing.md#recovering-a-partial-publish).
