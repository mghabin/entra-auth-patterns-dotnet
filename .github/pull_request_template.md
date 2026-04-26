<!-- Keep this short. Reviewers should be able to grok the PR in <60s. -->

## Why

<!-- One paragraph: what problem does this solve? Cite the issue/finding/audit
that motivated it if applicable. -->

## What

<!-- Bullet list of the actual changes. Mention any new dependencies, new env
vars, new infra resources, or breaking changes. -->

-

## Verification

<!-- How did you confirm it works?
     - tests added/updated  : …
     - manual check         : …
     - lint/build clean     : …
     - cloud deploy verified: env=…  -->

## Risk & rollback

<!-- Worst case if this regresses, and how to revert. For infra changes, note
whether the change is reversible (e.g. KV purge protection is NOT). -->

---

### Checklist

- [ ] CI is green (`build-test`, `bicep-lint`, `CodeQL`, `dependency-review`)
- [ ] No new warnings (project-level `TreatWarningsAsErrors` will fail otherwise)
- [ ] Docs updated if public surface or operator workflow changed
- [ ] No secrets, tokens, or PII in the diff (run `git diff --stat`)
- [ ] If infra: previewed with `WHAT_IF=1 ./scripts/provision-apps.sh ENV=…` (or `az deployment ... what-if`) before merge
