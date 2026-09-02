# Foo Calendar trademark and attribution policy

The Elastic License 2.0 in [LICENSE.txt](LICENSE.txt) governs the **code**. It grants no
rights to the **name** or the **mark**: "You may not alter, remove, or obscure any
licensing, copyright, or other notices of the licensor in the software. Any use of the
licensor's trademarks is subject to applicable law."

This document says what that means in practice, so nobody has to guess.

## What you may do

- **Run it, modify it, fork it.** The license grants the right to use, copy, distribute,
  make available, and prepare derivative works. Fix bugs, add features, change the
  database, rip out half of it. You do not need permission and you do not owe anyone a
  patch.
- **Restyle it to match your brand.** Colors, fonts, corner radius and light/dark default
  all live in [`src/Web/src/styles/theme.css`](src/Web/src/styles/theme.css) and are meant
  to be edited. That file's header lists the contrast rules to keep so the result stays
  accessible.
- **Name your deployment after your team.** `Team:OrgName` puts your own name in the
  header. That is the deployment's name, and it is yours.
- **Say what your fork is based on.** "Based on Foo Calendar", "a fork of Foo Calendar",
  "compatible with Foo Calendar" are all accurate and all fine.
- **Redistribute your fork**, under these same terms, with prominent notices saying you
  changed it (the license requires that; see its Notices section).

## What you may not do

- **Rename the product.** There is no setting that changes the app's name, and removing
  the name from the code is not a rebrand: it is removing the licensor's notice. What the
  header shows is `Team:OrgName`; what the footer shows is the product.
- **Remove or hide the footer attribution.** The product name, mark and copyright line in
  the footer are one of the licensor's notices in the software, within the meaning of the
  license's Notices section.
- **Call your fork "Foo Calendar"**, or ship it under a name or logo confusingly similar
  to it.
- **Use the mark as your own product's mark**, or in a way that implies this project
  endorses, sponsors, or supports you.
- **Offer it to third parties as a hosted or managed service** exposing a substantial part
  of its functionality. This one is the license, not trademark: see LICENSE.txt.

Running it for your own organization's staff is not "offering it to third parties", and
your organization being a profitable business changes nothing. Selling access to it, or
running it as a service for other people's teams, is.

## Where the name and mark live in the code

They are constants, not configuration, in exactly two places:

- [`src/Api/Configuration/ProductInfo.cs`](src/Api/Configuration/ProductInfo.cs)
- [`src/Web/src/lib/productInfo.ts`](src/Web/src/lib/productInfo.ts)

with the mark itself in [`src/Web/src/components/BrandMark.vue`](src/Web/src/components/BrandMark.vue)
(monochrome, for the footer) and the favicon in
[`src/Web/index.html`](src/Web/index.html) (full color).

## Asking

If you want to do something this document doesn't cover, or you want permission for
something it rules out, ask. The answer is often yes.
