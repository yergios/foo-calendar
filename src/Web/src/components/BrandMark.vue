<template>
  <!-- Monochrome by design: it inks itself with currentColor, so it stays legible against
       whatever palette a deployment compiled into theme.css without anyone having to
       maintain a second artwork per theme. The full-color variant is the favicon, in
       index.html, where it sits on browser chrome rather than on the app's own surfaces.
       Both are the same drawing: the shape is what carries the identity. -->
  <svg
    viewBox="0 0 32 32"
    fill="currentColor"
    aria-hidden="true"
    focusable="false"
    class="shrink-0"
  >
    <rect
      x="3"
      y="5"
      width="26"
      height="24"
      rx="4"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
    />
    <rect x="3" y="5" width="26" height="7" rx="4" />
    <rect x="3" y="9" width="26" height="3" />
    <rect x="9" y="2" width="2.5" height="6" rx="1.25" />
    <rect x="20.5" y="2" width="2.5" height="6" rx="1.25" />
    <rect x="19" y="19" width="6" height="6" rx="1.5" />
    <rect x="7" y="19" width="6" height="6" rx="1.5" opacity="0.4" />
  </svg>
</template>
