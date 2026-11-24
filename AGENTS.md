There's no need for you to verify visual changes, I will.

## Git

This repo works directly on `master`. This overrides the global "never commit on main/master"
rule and step 1 of the `/commit` command (branch creation) for this project only:

- Do not create a branch, for `/commit` or otherwise. Commit on `master` as if `direct` had
  been passed, unless I name a branch or ask for one.
- After committing, push `master` to its remote (`git push`), unless I say not to.
- Everything else about commits still applies: conventional commit subject, the
  `---` / `Co-authored by AI.` footer, no `Co-Authored-By` trailer.

## Definition of done

A change is done when `scripts/check.sh` passes: every check CI runs that does not need
Docker, the end-to-end suite and the performance budget included.
