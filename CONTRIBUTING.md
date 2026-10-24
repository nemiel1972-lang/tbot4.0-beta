# Contributing

Thanks for helping. These rules apply to everyone, whether the code was written by hand or with the help of AI.

## Before you start

- For anything bigger than a small fix, open an issue or ask in the dev chat first. A short "I'd like to do X" saves everyone time.
- Look at how the surrounding code is written and match it.

## Pull requests

- **Keep it small.** One change per PR. A first PR should be especially small.
- **Explain it.** Say what changes and why, in a few sentences. Mention anything that behaves differently afterwards.
- **Test it yourself first.** Say what you tested and how (for example: "ran against the test server for two hours, attack and farm cycles OK"). Untested code is a feature request, not a PR.
- **Understand it.** You should be able to explain every line you submit.
- **Stay around.** Please answer review comments. A PR that nobody follows up on will be closed.
- **No unrelated changes.** No reformatting, renaming or new dependencies unless the PR is about exactly that. New dependencies need a short justification.

## AI assistance

AI tools are fine, for fixes, translations, docs, regexes and reviews just as for features. The same rules apply as for hand-written code:

- Note in the PR description that AI was involved.
- You read and tested the result. Reviewers should not be the first ones to run it.
- Large generated changes may be treated as a proof of concept or feature request and rewritten before merging.

## Review

- Every PR gets the same review, no matter who or what wrote it.
- Reviewers may ask for changes, ask to split a PR, or decline it. Please don't take that personally.
- Merged code lands in beta or development builds first, where bugs are expected and reported before a stable release.

## Bugs

When reporting a bug, please include your version, the relevant log lines and the config sections involved (with secrets removed).
