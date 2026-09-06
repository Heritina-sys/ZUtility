# Code of conduct

## In short

Be decent to the people you work with here. Assume the person on the other side
of the thread is trying to help.

This project is the device-facing collector of the ZK attendance platform,
alongside [ZKAPI](https://github.com/Heritina-sys/ZKAPI) (the API) and
[FrontZK](https://github.com/Heritina-sys/FrontZK) (the dashboard). The same
terms apply across all three.

## Expected

- Critique the code, not the person who wrote it. "This pushes in a loop with no
  acknowledgement" is useful. "Who writes code like this?" is not.
- Accept that reviewers and maintainers will sometimes say no, and that they owe
  you a reason when they do.
- Say when you are unsure. It costs nothing and saves everyone time.
- Assume good faith on a first misunderstanding.

## Not acceptable

- Harassment, insults, or personal attacks.
- Discriminatory language or jokes — including about nationality, gender,
  religion, disability, or sexuality.
- Sexual attention or imagery of any kind.
- Publishing someone's private information without their permission.
- Sustained disruption: reopening settled arguments, brigading threads,
  deliberately derailing discussions.

## A note specific to this project

ZUtility pulls **employee attendance records off a biometric device**. The
people whose punches flow through this code never consented to being monitored
by the maintainers of this repository; they showed up for work and scanned a
finger. Keep that in mind when you touch the code and when you talk about it.

- Do not paste real names, attendance times, or device identifiers into issues,
  pull requests, or test fixtures. This repository is public. Redact and use
  placeholders.
- Do not commit the device communication password or any SDK registration
  detail that is not already public. `SECURITY.md` and `CONTRIBUTING.md` both
  cover what must never be tracked.
- Do not commit build output (`bin/`, `obj/`) or the `Interop.zkemkeeper.dll`
  that `tlbimp` generates — the CI hygiene job rejects them.
- Using data from a deployment of this software to monitor, pressure, or
  embarrass an individual falls under this document, not outside it.

## Scope

Applies in issues, pull requests, commit messages, code comments, discussions,
and any space where you are representing this project.

## Reporting

Contact the maintainer through their
[GitHub profile](https://github.com/Heritina-sys). Reports are handled
privately. Do not open a public issue about someone's conduct.

If your report concerns the maintainer, GitHub's
[abuse reporting](https://github.com/contact/report-abuse) is the escalation
path.

## Enforcement

The maintainer decides what happens, proportionate to what occurred:

1. A private word.
2. A public correction in the thread.
3. A temporary block from interacting with the repository.
4. A permanent ban.

Serious cases skip steps. Decisions are explained to the person affected.

---

Adapted in spirit from the [Contributor Covenant](https://www.contributor-covenant.org),
rewritten to fit this project rather than pasted verbatim.
