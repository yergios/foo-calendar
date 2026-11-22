# Documentation

### Getting started

The first time you sign in, click **Set up profile** in the header (or the button on the home page) to pick a display name, a color, and optionally where you're based. Pick the country first: the **Region** field below it stays disabled until you do, and for the many countries whose public holidays are all nationwide it stays disabled after. Both fields are searchable, so type "astur" rather than scrolling. Picking a country and region is what tells the calendar to start tracking that region's public holidays — you don't need to ask anyone to add it first.

### The calendar

Each row is a person, each column a day. The color of a cell tells you what's going on that day:

- A solid **gold** column marks today.
- A tinted column marks a public holiday for one of the regions the team is in. Hover a day to see which holiday(s) apply.
- A colored block in someone's row is an absence, labeled **V** (Vacation), **A** (Appointment), or **O** (Other). A multi-day absence only shows its letter once, at the start of the block, so it reads as one continuous stretch.

Use the **Sort by** and **Region** controls above the grid to reorder or filter the team list. The **Region** list shows the places the team is actually spread across, so it grows and shrinks as people join, move, and leave.

### Adding, editing, and deleting an absence

- Click **+ Add absence**, or click any empty day in your own row to start a new absence prefilled with that date.
- Click one of your own absence blocks to edit its dates, type, or note — or to delete it, right from that same screen.
- You can only add, edit, or delete absences for yourself. Everyone else's absences are visible but read-only.
- Your **note is yours**. The team sees that you're off and whether it's vacation, an appointment, or other, but nobody else ever sees what you wrote — not on the calendar, not on the admin page, and not in the audit log. There is deliberately no "sick" type: why you're away is nobody's business, so if you're ill, use **Other**, and please keep health details out of the note.

### Bank holidays

Public holidays sync automatically once a week from the [Nager.Date](https://date.nager.at/) public holiday API, for every region somebody on the team is in, plus the team's own home region. Setting your region on your profile pulls its holidays in right away. It may still miss a few from your specific region/city. If so, you'll need to add them yourself.

Click **+ Add bank holiday** to add a new one, or click the bank-holiday day directly to edit/delete it. Anyone can add, edit, or delete a manually-added holiday — but a holiday that came from the automatic sync can't be edited or deleted this way, since it would just come back on the next sync.

### Your profile

From the **Profile** page you can update your display name, color, and region, see your own vacation/other day totals for any year, or delete your profile entirely.

One thing worth knowing before you delete: if you're the last person in your region, that region's public holidays are removed from the calendar along with your profile, including any that were added by hand. The team's home region is never removed this way.

### Admin

Keeping the calendar's public holidays right is everyone's job — adding, editing, and deleting a manual holiday needs no special access at all.

Beyond that, an **Admin** link appears in the header if you've been granted anything extra. The page shows only the parts you hold, so what you see there depends on what you were given:

- **Manage people** — create, edit, and delete anyone's profile, and import a team from a CSV.
- **Read reports** — everyone's vacation/other totals for a year, side by side.
- **Sync bank holidays** — re-run the automatic sync for a given year, or for a single region that failed.
- **Approve absences** — when your team has this turned on, approve or reject requests waiting for review. Each one waiting on you also shows up under "Waiting on you" in the notification bell, and badges the "Absence approvals" link, until it's resolved.
- **Read the audit log** — who changed or deleted what, kept after the thing itself is gone.

Ask whoever administers your identity provider if you need one of these.

---

Still need help? [Contact support](%SUPPORT_URL%).
