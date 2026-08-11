# Help

## Documentation

### Getting started

The first time you sign in, click **Set up profile** in the header (or the button on the home page) to pick a display name, a color, and optionally your home city.

### The calendar

Each row is a person, each column a day. The color of a cell tells you what's going on that day:

- A solid **gold** column marks today.
- A tinted column marks a public holiday for one of the configured regions. Hover a day to see which holiday(s) apply.
- A colored block in someone's row is an absence, labeled **V** (Vacation), **S** (Sick), or **O** (Other). A multi-day absence only shows its letter once, at the start of the block, so it reads as one continuous stretch.

Use the **Sort by** and **Region** controls above the grid to reorder or filter the team list.

### Adding, editing, and deleting an absence

- Click **+ Add absence**, or click any empty day in your own row to start a new absence prefilled with that date.
- Click one of your own absence blocks to edit its dates, type, or note — or to delete it, right from that same screen.
- You can only add, edit, or delete absences for yourself. Everyone else's absences are visible but read-only.

### Bank holidays

Public holidays sync automatically once a week from the [Nager.Date](https://date.nager.at/) public holiday API, for every region configured for this app. It may still miss a few from your specific region/city. If so, you'll need to add them yourself.

Click **+ Add bank holiday** to add a new one, or click the bank-holiday day directly to edit/delete it. Anyone can add, edit, or delete a manually-added holiday — but a holiday that came from the automatic sync can't be edited or deleted this way, since it would just come back on the next sync.

### Your profile

From the **Profile** page you can update your display name, color, and home city, see your own vacation/sick/other day totals for any year, or delete your profile entirely.

### Admin

If you have admin access, an **Admin** link appears in the header. From there you can manage anyone's profile, see everyone's absence stats side by side, and manually trigger a bank-holiday sync for a given year.

---

Still need help? [Contact support](%VITE_SUPPORT_URL%).

## FAQ

**Why can't I add an absence?**
You need a profile first — click **Set up profile** in the header.

**Why doesn't clicking a day do anything?**
Only your own row's empty days are clickable, to start a new absence. Other people's rows, and days that already have something on them, aren't click-to-add targets.

**Can I edit or delete someone else's absence?**
No. You can only manage your own absences. Anyone can manage manually-added bank holidays, though, since those apply to the whole team.

**A public holiday for my region is missing. What do I do?**
Add it manually with **+ Add bank holiday**. If it's a whole region that's missing from the list entirely, ask an admin to add it to the region catalog.

**Why can't I delete a bank holiday?**
Only manually-added holidays can be deleted or edited. One that came from the automatic Nager sync is locked, since editing it would just be undone the next time the sync runs.

**I set a custom sort order and now it's gone.**
Custom ordering is stored in your own browser, not on the server. Clearing your browser's site data, or switching devices, resets it back to sorting by name.

**Do weekends or holidays count toward my stats?**
The Vacation/Sick/Other totals on your profile only count working days within the range you picked, so a week-long vacation over a weekend doesn't inflate your total.

---

Still need help? [Contact support](%VITE_SUPPORT_URL%).
