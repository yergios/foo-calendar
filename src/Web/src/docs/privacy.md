# Privacy

<!-- Deployer: this is a generic starting point built from what the software itself
     stores (see docs/data-protection.md in the repo for the full technical map). It
     describes the app accurately, but it is not legal advice and doesn't by itself make
     this deployment compliant with your jurisdiction's law - have it reviewed before you
     rely on it, and add anything specific to how you run this instance (analytics, a
     different retention window, a data processing agreement with your hosting provider,
     and so on). -->

%ORG_NAME% operates this deployment of [%PRODUCT_NAME%](%REPOSITORY_URL%) to coordinate its team's time off. This page explains what personal data the app stores and what happens to it.

### Who's responsible for your data

%ORG_NAME% is the controller for the data in this deployment — not the authors of the software, who never receive it. For any question or request about your data, reach out via [%SUPPORT_URL%](%SUPPORT_URL%).

### What's stored

- **Your profile** — the username your identity provider gives us, your name and display name, and the region you set, which is used only to decide which public holidays apply to you.
- **Your absences** — dates, a type (Vacation, Appointment, or Other), and an optional note. There is deliberately no "sick" type: an absence's type is visible to the rest of the team, and illness is health data, so it is recorded as Other instead. Your note is private — nobody but you can ever read it, on the calendar, on the admin pages, or in the audit log.
- **Notifications** — a short record of each absence request that was approved or rejected, kept for a limited time and then deleted automatically.
- **The audit log** — who changed or deleted a shared team record (a person, an absence, a bank holiday) and when, kept for a bounded period so changes can be accounted for, then deleted automatically. It never records the contents of an absence note.

### What isn't stored or shared

- Your absence notes never leave the app and are never shown to anyone but you.
- This page uses no tracking cookies and no third-party analytics.
- Data isn't sold or shared with third parties for marketing.

### How long it's kept

Profile and absence data is kept for as long as you're part of the team. Notifications and audit entries are deleted automatically after a bounded retention window that this deployment configures.

### Your rights

Depending on where you are, you may have the right to access, correct, or delete your data, or to ask what's held about you. Contact [%SUPPORT_URL%](%SUPPORT_URL%) to exercise any of these.

---

Questions about this notice: [%SUPPORT_URL%](%SUPPORT_URL%).
