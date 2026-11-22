using System.Globalization;

using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class AbsenceServiceTests : SqliteTestBase
{
    private readonly RecordingAuditLog audit = new();
    private readonly RecordingBankHolidayService bankHolidays = new();
    private readonly AbsenceService sut;

    public AbsenceServiceTests()
    {
        sut = new AbsenceService(Db, bankHolidays, audit);
    }

    private Person CreatePerson(string username = "jdoe", string homeCity = null)
    {
        var person = new Person
        {
            Username = username,
            DisplayName = username,
            HomeCity = homeCity,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = username,
        };
        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    private static CreateAbsenceRequest Request(
        string start, string end, AbsenceType type = AbsenceType.Vacation, string note = null,
        string startTime = null, string endTime = null) =>
        new(
            DateOnly.Parse(start, CultureInfo.InvariantCulture), DateOnly.Parse(end, CultureInfo.InvariantCulture), type, note,
            startTime is null ? null : TimeOnly.Parse(startTime, CultureInfo.InvariantCulture),
            endTime is null ? null : TimeOnly.Parse(endTime, CultureInfo.InvariantCulture));

    [Fact]
    [Trait("Spec", "ABS-4")]
    public async Task CreateAsync_WhenNoOverlap_CreatesAbsence()
    {
        var person = CreatePerson();

        var result = await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        result.Id.ShouldBeGreaterThan(0);
        result.PersonId.ShouldBe(person.Id);
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_WhenDatesOverlapAnExistingAbsence_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(person, Request("2026-03-05", "2026-03-10")));
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_WhenNewAbsenceStartsOnExistingEndDate_Throws()
    {
        // Touching ranges (new start == existing end) still share that one day, so it counts
        // as an overlap, not a valid back-to-back pair.
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(person, Request("2026-03-06", "2026-03-10")));
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_WhenNewAbsenceStartsTheDayAfterExistingEnds_Succeeds()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        var result = await sut.CreateAsync(person, Request("2026-03-07", "2026-03-10"));

        result.ShouldNotBeNull();
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_DoesNotConflictWithAnotherPersonsOverlappingAbsence()
    {
        var alice = CreatePerson("alice");
        var bob = CreatePerson("bob");
        await sut.CreateAsync(alice, Request("2026-03-02", "2026-03-06"));

        var result = await sut.CreateAsync(bob, Request("2026-03-02", "2026-03-06"));

        result.ShouldNotBeNull();
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task UpdateAsync_WhenDatesUnchanged_DoesNotConflictWithItself()
    {
        var person = CreatePerson();
        var created = await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        var updated = await sut.UpdateAsync(
            person, created.Id, Request("2026-03-02", "2026-03-06", AbsenceType.Other, "updated"));

        updated.Type.ShouldBe(AbsenceType.Other);
        updated.Note.ShouldBe("updated");
    }

    [Fact]
    [Trait("Spec", "ABS-4")]
    public async Task UpdateAsync_WhenNotOwner_ThrowsUnauthorizedAccessException()
    {
        var owner = CreatePerson("alice");
        var intruder = CreatePerson("bob");
        var created = await sut.CreateAsync(owner, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => sut.UpdateAsync(intruder, created.Id, Request("2026-04-01", "2026-04-05")));
    }

    [Fact]
    [Trait("Spec", "ABS-4")]
    public async Task UpdateAsync_WhenAbsenceDoesNotExist_ThrowsKeyNotFoundException()
    {
        var person = CreatePerson();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => sut.UpdateAsync(person, 999, Request("2026-04-01", "2026-04-05")));
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task UpdateAsync_WhenNewDatesOverlapADifferentAbsence_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-01-05", "2026-01-10"));
        var second = await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.UpdateAsync(person, second.Id, Request("2026-01-08", "2026-01-12")));
    }

    [Fact]
    [Trait("Spec", "ABS-4")]
    public async Task DeleteAsync_WhenNotOwner_ThrowsUnauthorizedAccessException()
    {
        var owner = CreatePerson("alice");
        var intruder = CreatePerson("bob");
        var created = await sut.CreateAsync(owner, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => sut.DeleteAsync(intruder, created.Id));
    }

    [Fact]
    [Trait("Spec", "ABS-4")]
    public async Task DeleteAsync_WhenAbsenceDoesNotExist_ThrowsKeyNotFoundException()
    {
        var person = CreatePerson();

        await Should.ThrowAsync<KeyNotFoundException>(() => sut.DeleteAsync(person, 999));
    }

    [Fact]
    [Trait("Spec", "ABS-4")]
    public async Task DeleteAsync_RemovesTheAbsence()
    {
        var person = CreatePerson();
        var created = await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await sut.DeleteAsync(person, created.Id);

        var remaining = await sut.GetOverlappingAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), person.Id);
        remaining.Absences.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    public async Task GetStatsAsync_CountsOnlyWeekdays()
    {
        var person = CreatePerson();
        // Mon 2026-03-02 through Sun 2026-03-08: one full calendar week, 5 working days.
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-08"));

        var stats = await sut.GetStatsAsync(person, 2026);

        stats.VacationDays.ShouldBe(5);
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    public async Task GetStatsAsync_ClipsAbsenceToTheRequestedYear()
    {
        var person = CreatePerson();
        // Wed 2025-12-31 through Fri 2026-01-02: only Jan 1 (Thu) and Jan 2 (Fri) fall in 2026.
        await sut.CreateAsync(person, Request("2025-12-31", "2026-01-02"));

        var stats = await sut.GetStatsAsync(person, 2026);

        stats.VacationDays.ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    public async Task GetStatsAsync_SplitsDaysByAbsenceType()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-02-02", "2026-02-02", AbsenceType.Vacation));
        await sut.CreateAsync(person, Request("2026-02-04", "2026-02-04", AbsenceType.Other));

        var stats = await sut.GetStatsAsync(person, 2026);

        stats.VacationDays.ShouldBe(1);
        stats.OtherDays.ShouldBe(1);
    }

    private int nextHolidayId = 1;

    private void SeedHoliday(string region, string date) =>
        bankHolidays.Holidays.Add(new BankHolidayDto(
            nextHolidayId++, region, DateOnly.Parse(date, CultureInfo.InvariantCulture), "Test holiday", HolidaySource.Manual, false));

    [Fact]
    [Trait("Spec", "ABS-10")]
    [Trait("Spec", "HOL-9")]
    public async Task GetStatsAsync_ExcludesABankHolidayMatchingThePersonsExactRegion()
    {
        var person = CreatePerson(homeCity: "CH-ZH");
        // Mon 2026-03-02 through Fri 2026-03-06: 5 working days, one of them a holiday.
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        SeedHoliday("CH-ZH", "2026-03-04");

        var stats = await sut.GetStatsAsync(person, 2026);

        stats.VacationDays.ShouldBe(4);
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    [Trait("Spec", "HOL-9")]
    public async Task GetStatsAsync_ExcludesANationwideBankHolidayForThePersonsCountry()
    {
        var person = CreatePerson(homeCity: "CH-ZH");
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        SeedHoliday("CH", "2026-03-04");

        var stats = await sut.GetStatsAsync(person, 2026);

        stats.VacationDays.ShouldBe(4);
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    [Trait("Spec", "HOL-9")]
    public async Task GetStatsAsync_StillCountsABankHolidayForADifferentRegion()
    {
        var person = CreatePerson(homeCity: "CH-ZH");
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        SeedHoliday("ES-MD", "2026-03-04");

        var stats = await sut.GetStatsAsync(person, 2026);

        stats.VacationDays.ShouldBe(5);
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    [Trait("Spec", "HOL-9")]
    public async Task GetStatsAsync_StillCountsABankHolidayForASiblingRegionInTheSameCountry()
    {
        var person = CreatePerson(homeCity: "CH-ZH");
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        SeedHoliday("CH-BE", "2026-03-04");

        var stats = await sut.GetStatsAsync(person, 2026);

        stats.VacationDays.ShouldBe(5);
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    [Trait("Spec", "HOL-9")]
    public async Task GetStatsAsync_StillCountsABankHolidayWhenThePersonHasNoRegion()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        SeedHoliday("CH-ZH", "2026-03-04");

        var stats = await sut.GetStatsAsync(person, 2026);

        stats.VacationDays.ShouldBe(5);
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    [Trait("Spec", "HOL-9")]
    public async Task GetAllStatsAsync_ExcludesOnlyEachPersonsOwnBankHoliday()
    {
        var zurich = CreatePerson("alice", "CH-ZH");
        var madrid = CreatePerson("bob", "ES-MD");
        await sut.CreateAsync(zurich, Request("2026-03-02", "2026-03-06"));
        await sut.CreateAsync(madrid, Request("2026-03-02", "2026-03-06"));
        SeedHoliday("CH-ZH", "2026-03-04");

        var stats = await sut.GetAllStatsAsync(2026);

        stats.Stats.Single(s => s.PersonId == zurich.Id).VacationDays.ShouldBe(4);
        stats.Stats.Single(s => s.PersonId == madrid.Id).VacationDays.ShouldBe(5);
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    public async Task GetAllStatsAsync_ReturnsZeroForPersonWithNoAbsences()
    {
        var person = CreatePerson();

        var stats = await sut.GetAllStatsAsync(2026);

        var personStats = stats.Stats.ShouldHaveSingleItem();
        personStats.PersonId.ShouldBe(person.Id);
        personStats.VacationDays.ShouldBe(0);
        personStats.OtherDays.ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    public async Task GetAllStatsAsync_ComputesEachPersonIndependently()
    {
        var alice = CreatePerson("alice");
        var bob = CreatePerson("bob");
        // Mon 2026-06-01 through Fri 2026-06-05: 5 consecutive working days.
        await sut.CreateAsync(alice, Request("2026-06-01", "2026-06-05"));

        var stats = await sut.GetAllStatsAsync(2026);

        stats.Stats.Single(s => s.PersonId == alice.Id).VacationDays.ShouldBe(5);
        stats.Stats.Single(s => s.PersonId == bob.Id).VacationDays.ShouldBe(0);
    }

    // No state to wait in: what was just created is on the calendar and counts.
    [Fact]
    [Trait("Spec", "ABS-7")]
    public async Task CreateAsync_IsInEffectImmediately()
    {
        var person = CreatePerson();
        // Mon 2026-05-04 through Fri 2026-05-08.
        var created = await sut.CreateAsync(person, Request("2026-05-04", "2026-05-08"));

        var calendar = await sut.GetOverlappingAsync(
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), viewerPersonId: null);
        var stats = await sut.GetStatsAsync(person, 2026);

        calendar.Absences.ShouldHaveSingleItem().Id.ShouldBe(created.Id);
        stats.VacationDays.ShouldBe(5);
    }

    [Fact]
    [Trait("Spec", "ABS-8")]
    public async Task GetOverlappingAsync_ReturnsEveryAbsenceTouchingTheRange_ByStartDateThenId()
    {
        var alice = CreatePerson("alice");
        var bob = CreatePerson("bob");
        var later = await sut.CreateAsync(alice, Request("2026-07-20", "2026-07-24"));
        var straddlingStart = await sut.CreateAsync(alice, Request("2026-06-29", "2026-07-01"));
        var sameDayFirst = await sut.CreateAsync(bob, Request("2026-07-06", "2026-07-06"));
        var sameDaySecond = await sut.CreateAsync(alice, Request("2026-07-06", "2026-07-06"));
        var straddlingEnd = await sut.CreateAsync(bob, Request("2026-07-31", "2026-08-03"));
        await sut.CreateAsync(bob, Request("2026-06-22", "2026-06-30"));
        await sut.CreateAsync(alice, Request("2026-08-01", "2026-08-07"));

        var result = await sut.GetOverlappingAsync(
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), viewerPersonId: null);

        result.Absences.Select(a => a.Id).ShouldBe(
            [straddlingStart.Id, sameDayFirst.Id, sameDaySecond.Id, later.Id, straddlingEnd.Id]);
    }

    [Fact]
    [Trait("Spec", "ABS-12")]
    [Trait("Spec", "AUD-2")]
    public async Task CreateAsync_LogsWhatWasFiledWithoutTheNote()
    {
        var person = CreatePerson();

        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06", AbsenceType.Other, "Flu"));

        var entry = audit.Entries.Single(e => e.Action == AuditActions.AbsenceCreated);
        entry.Target.ShouldBe(person.DisplayName);
        entry.Details.ShouldBe("Other, 2026-03-02 → 2026-03-06");
        entry.Details.ShouldNotContain("Flu");
    }

    [Fact]
    [Trait("Spec", "ABS-12")]
    [Trait("Spec", "AUD-2")]
    public async Task UpdateAsync_LogsWhatChangedWithoutTheNote()
    {
        var person = CreatePerson();
        var created = await sut.CreateAsync(
            person, Request("2026-03-02", "2026-03-06", AbsenceType.Other, "Flu"));

        await sut.UpdateAsync(
            person, created.Id, Request("2026-04-02", "2026-04-06", AbsenceType.Vacation, "Trip"));

        var entry = audit.Entries.Single(e => e.Action == AuditActions.AbsenceUpdated);
        entry.Target.ShouldBe(person.DisplayName);
        entry.Details.ShouldBe("Vacation, 2026-04-02 → 2026-04-06");
        entry.Details.ShouldNotContain("Trip");
    }

    [Fact]
    [Trait("Spec", "ABS-9")]
    public async Task GetOverlappingAsync_ReturnsTheViewersOwnNote()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06", AbsenceType.Other, "Flu"));

        var result = await sut.GetOverlappingAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), person.Id);

        result.Absences.ShouldHaveSingleItem().Note.ShouldBe("Flu");
    }

    // The team can see that someone is off and what kind of absence it is. Why is between
    // them and the app.
    [Fact]
    [Trait("Spec", "ABS-9")]
    public async Task GetOverlappingAsync_WithholdsEveryoneElsesNote()
    {
        var author = CreatePerson("jdoe");
        var viewer = CreatePerson("asmith");
        await sut.CreateAsync(author, Request("2026-03-02", "2026-03-06", AbsenceType.Other, "Flu"));

        var result = await sut.GetOverlappingAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), viewer.Id);

        var absence = result.Absences.ShouldHaveSingleItem();
        absence.Note.ShouldBeNull();
        absence.Type.ShouldBe(AbsenceType.Other);
    }

    [Fact]
    [Trait("Spec", "ABS-9")]
    public async Task GetOverlappingAsync_WhenTheViewerHasNoProfileYet_WithholdsEveryNote()
    {
        var author = CreatePerson();
        await sut.CreateAsync(author, Request("2026-03-02", "2026-03-06", AbsenceType.Other, "Flu"));

        var result = await sut.GetOverlappingAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), viewerPersonId: null);

        result.Absences.ShouldHaveSingleItem().Note.ShouldBeNull();
    }

    [Fact]
    [Trait("Spec", "ABS-12")]
    [Trait("Spec", "AUD-2")]
    public async Task DeleteAsync_LogsWhatWentWithoutTheNote()
    {
        var person = CreatePerson();
        var created = await sut.CreateAsync(
            person, Request("2026-03-02", "2026-03-06", AbsenceType.Other, "Flu"));

        await sut.DeleteAsync(person, created.Id);

        var entry = audit.Entries.Single(e => e.Action == AuditActions.AbsenceDeleted);
        entry.Target.ShouldBe(person.DisplayName);
        entry.Details.ShouldBe("Other, 2026-03-02 → 2026-03-06");
        entry.Details.ShouldNotContain("Flu");
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_TwoAppointmentsSameDayWithOverlappingTimes_BothSucceed()
    {
        var person = CreatePerson();

        var first = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "11:00"));
        var second = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "10:00", endTime: "12:00"));

        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_AppointmentOnADayWithAnExistingVacation_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-08-20", "2026-08-20"));

        await Should.ThrowAsync<InvalidOperationException>(() => sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00")));
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_VacationOnADayWithAnExistingAppointment_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(person, Request("2026-08-20", "2026-08-20")));
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_AppointmentsOnDifferentDays_BothSucceed()
    {
        var person = CreatePerson();

        var first = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));
        var second = await sut.CreateAsync(
            person, Request("2026-08-21", "2026-08-21", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));

        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task UpdateAsync_AppointmentOnADayWithAnExistingVacation_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-08-20", "2026-08-20"));
        var toEdit = await sut.CreateAsync(
            person, Request("2026-08-25", "2026-08-25", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));

        await Should.ThrowAsync<InvalidOperationException>(() => sut.UpdateAsync(
            person, toEdit.Id,
            Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00")));
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task UpdateAsync_TwoAppointmentsSameDay_DoesNotConflictWithItself()
    {
        var person = CreatePerson();
        await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));
        var toEdit = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "13:00", endTime: "14:00"));

        var updated = await sut.UpdateAsync(
            person, toEdit.Id,
            Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:30", endTime: "10:30"));

        updated.Type.ShouldBe(AbsenceType.Appointment);
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_TimeBasedOtherAndAppointmentSameDay_BothSucceed()
    {
        var person = CreatePerson();

        var appointment = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));
        var timeBasedOther = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Other, startTime: "13:00", endTime: "14:00"));

        appointment.Id.ShouldNotBe(timeBasedOther.Id);
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_TimeBasedOtherOnADayWithAnExistingVacation_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-08-20", "2026-08-20"));

        await Should.ThrowAsync<InvalidOperationException>(() => sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Other, startTime: "09:00", endTime: "10:00")));
    }

    [Fact]
    [Trait("Spec", "ABS-5")]
    public async Task CreateAsync_WholeDayOtherOnADayWithAnExistingTimeBasedOther_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Other, startTime: "09:00", endTime: "10:00"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(person, Request("2026-08-20", "2026-08-20", AbsenceType.Other)));
    }

    [Fact]
    [Trait("Spec", "ABS-10")]
    public async Task GetStatsAsync_TimeBasedOther_DoesNotCountTowardOtherDays()
    {
        var person = CreatePerson();
        await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Other, startTime: "09:00", endTime: "10:00"));

        var stats = await sut.GetStatsAsync(person, 2026);

        stats.OtherDays.ShouldBe(0);
    }
}
