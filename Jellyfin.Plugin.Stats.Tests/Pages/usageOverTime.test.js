/*
 * The range view, read as a function from figures to markup.
 *
 * Issue #57 asks for plays and watched time per day with the direct and
 * transcoded split visible in the same view, and for a view that names no user.
 * The split and the naming are what these are written against.
 *
 * The three conditions of that issue are about the request rather than the
 * drawing, so none of them is asserted here and none is claimed. The page
 * rendering from one request and the range control being bounded by the cap the
 * query layer enforces are statements about a response, and there is none.
 *
 * Run with the test runner built into node:
 *
 *     node --test Jellyfin.Plugin.Stats.Tests/Pages/
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import {
    DELIVERY_IS_READ_AT_THE_START,
    usageOverTime,
} from '../../Jellyfin.Plugin.Stats/Pages/usageOverTime.js';

/**
 * One day as the fold produces it, with the delivery figures filled in.
 *
 * @param {string} day The calendar day, in the zone the answer names.
 * @param {{plays: number, watchedMinutes?: number|null, unknown?: number, transcode?: number}} figures Its figures.
 * @returns {object} The day.
 */
function day(day, figures) {
    return {
        day,
        watchedMinutes: figures.watchedMinutes ?? null,
        delivery: {
            plays: figures.plays,
            unknown: figures.unknown ?? 0,
            directPlay: figures.plays - (figures.unknown ?? 0) - (figures.transcode ?? 0),
            directStream: 0,
            transcode: figures.transcode ?? 0,
        },
    };
}

/**
 * A range of three days with a re-encoded play on the middle one.
 *
 * @returns {object} The answer.
 */
function aWeek() {
    return {
        state: 'ready',
        zone: 'Europe/Berlin',
        plays: 9,
        watchedMinutes: 300,
        days: [
            day('2026-01-05', { plays: 2, watchedMinutes: 60 }),
            day('2026-01-06', { plays: 4, watchedMinutes: 150, transcode: 3 }),
            day('2026-01-07', { plays: 3, watchedMinutes: 90 }),
        ],
    };
}

/**
 * The two drawings a ready view is made of, separately.
 *
 * The lower drawing legitimately reads nought on a day the server re-encoded
 * nothing, so a claim about a gap in the upper one has to be made about the
 * upper one rather than about the markup as a whole.
 *
 * @param {string} view The view.
 * @returns {{figure: string, reEncoded: string}} The two drawings.
 */
function drawings(view) {
    const first = view.indexOf('<svg');
    const second = view.indexOf('<svg', first + 1);

    return { figure: view.slice(first, second), reEncoded: view.slice(second) };
}

test('the view draws with no document present', () => {
    assert.equal(typeof document, 'undefined');
    assert.equal(typeof window, 'undefined');

    assert.match(usageOverTime(aWeek()), /^<figure /);
});

test('the view states the zone its days were read in', () => {
    const drawn = usageOverTime(aWeek());

    /* Twice, and deliberately. The caption is what a reader sees and the
     * description is what a reader who cannot see the drawing is given instead,
     * and a view that says it in only one of the two leaves one of them looking
     * at a series whose midnight is anybody's. */
    assert.match(drawn, /<figcaption[^>]*>9 plays over 3 days, read in Europe\/Berlin\./);
    assert.match(drawn, /<desc>[^<]*Days are read in Europe\/Berlin\.<\/desc>/);
});

test('a range with no zone is refused rather than drawn', () => {
    const ready = aWeek();

    assert.throws(() => usageOverTime({ ...ready, zone: undefined }), /zone/);
    assert.throws(() => usageOverTime({ ...ready, zone: '' }), /zone/);
    assert.throws(() => usageOverTime({ ...ready, zone: '   ' }), /zone/);
});

test('the plays the server re-encoded are drawn over the same days as the plays', () => {
    const drawn = usageOverTime(aWeek());

    assert.match(drawn, /<title>Plays per day<\/title>/);
    assert.match(drawn, /<title>Plays re-encoded per day<\/title>/);
    assert.match(drawn, /<title>2026-01-06: 4<\/title>/);
    assert.match(drawn, /<title>2026-01-06: 3<\/title>/);
    assert.match(drawn, /<title>2026-01-05: 0<\/title>/);
});

test('a day whose delivery was never reported has no reading rather than a nought', () => {
    const drawn = usageOverTime({
        state: 'ready',
        zone: 'UTC',
        plays: 5,
        watchedMinutes: 100,
        days: [day('2026-02-01', { plays: 5, watchedMinutes: 100, unknown: 5 })],
    });

    /* The upper line still counts the plays, because they happened. The lower
     * one says nothing, because a reading at nought there says the server sent
     * every one of them as it was, and what is true is that it never said. */
    const { figure, reEncoded } = drawings(drawn);

    assert.match(figure, /<title>2026-02-01: 5<\/title>/);
    assert.doesNotMatch(reEncoded, /<title>2026-02-01: 0<\/title>/);
    assert.match(reEncoded, /1 of 1 not recorded/);
});

test('a day the answer has no figure for breaks the line rather than dropping to nought', () => {
    /* A day the retention sweep emptied, a day the range never covered and a
     * quiet day are three different facts. The fold sends the first two as
     * nothing, and a view turning that into a nought would draw a trough in the
     * line that a reader takes for a quiet day. */
    const drawn = usageOverTime(
        {
            state: 'ready',
            zone: 'UTC',
            plays: 6,
            watchedMinutes: 120,
            days: [
                day('2026-03-01', { plays: 4, watchedMinutes: 120 }),
                day('2026-03-02', { plays: 2, watchedMinutes: null }),
            ],
        },
        { figure: 'watchedMinutes' },
    );

    const { figure } = drawings(drawn);

    assert.match(figure, /<title>2026-03-01: 120<\/title>/);
    assert.doesNotMatch(figure, /<title>2026-03-02: 0<\/title>/);
    assert.match(figure, /1 of 2 not recorded/);
});

test('the view draws the figure it was asked for', () => {
    const plays = usageOverTime(aWeek());
    const watched = usageOverTime(aWeek(), { figure: 'watchedMinutes' });

    assert.match(plays, /<title>Plays per day<\/title>/);
    assert.match(plays, /<figcaption[^>]*>9 plays over 3 days/);
    assert.match(watched, /<title>Watched time per day<\/title>/);
    assert.match(watched, /<figcaption[^>]*>300 minutes watched over 3 days/);
});

test('a figure this view does not have is refused rather than drawn empty', () => {
    assert.throws(() => usageOverTime(aWeek(), { figure: 'transcodes' }), /transcodes/);
});

test('the figure is refused before the state, so a view asking for the wrong one hears about it while loading', () => {
    /* Both are faults in the caller and neither depends on the days. A view that
     * resolved the figure only once an answer was ready would report the mistake
     * on the request that succeeded and stay quiet on the three that did not. */
    assert.throws(
        () => usageOverTime({ state: 'loading' }, { figure: 'transcodes' }),
        /transcodes/,
    );
});

test('plays with no delivery method are counted in words rather than left in the picture', () => {
    const some = usageOverTime({
        state: 'ready',
        zone: 'UTC',
        plays: 10,
        watchedMinutes: 200,
        days: [day('2026-04-01', { plays: 10, watchedMinutes: 200, unknown: 4, transcode: 3 })],
    });

    assert.match(some, /no delivery method for 4 of those plays/);
    assert.match(
        some,
        /counts the plays known to have been re-encoded and not the plays that were/,
    );
    assert.match(usageOverTime(aWeek()), /reported how it delivered every play in the range/);
});

test('the caption says which moment the delivery figures speak about', () => {
    /* Issue #158. A row holds the method the server reported when the play began
     * and, beside it, the moment that method first changed, and these figures are
     * folded from the first of the two. Both are true statements about different
     * moments, and a reader given the lower line with nothing saying which moment
     * it is about reads a disagreement into two figures that do not disagree.
     *
     * The sentence is asserted on both figures and in both delivery cases,
     * because the reason a reader needs it does not depend on which figure they
     * asked for or on whether the server happened to report every method. A
     * caption that carried it only when something went unreported would leave the
     * ordinary range - the one almost every reader meets - saying nothing. */
    const some = usageOverTime({
        state: 'ready',
        zone: 'UTC',
        plays: 10,
        watchedMinutes: 200,
        days: [day('2026-04-01', { plays: 10, watchedMinutes: 200, unknown: 4, transcode: 3 })],
    });

    for (const drawn of [
        some,
        usageOverTime(aWeek()),
        usageOverTime(aWeek(), { figure: 'watchedMinutes' }),
    ]) {
        assert.ok(
            drawn.includes(DELIVERY_IS_READ_AT_THE_START),
            'The view drew a range without saying which moment its delivery figures are about.',
        );
    }
});

test('the sentence about the two moments names the start rather than the fold', () => {
    /* The words are read here rather than only being carried through, so a later
     * edit that turned them into a sentence about how a play was delivered over
     * its whole course would fail beside the C# case that drives the fold. That
     * case proves the figures follow the start; this one proves the view has not
     * stopped saying so. */
    assert.match(DELIVERY_IS_READ_AT_THE_START, /when it began/);
    assert.match(DELIVERY_IS_READ_AT_THE_START, /re-encoded partway through/);
});

test('the total under the picture is the one the answer carries, not the sum of the readings', () => {
    /* The fold counts what it was handed separately from the days it produced,
     * so that a fold which lost a day shows. A view adding the readings up and
     * calling that the total would put the two statements back together and the
     * loss would be invisible again. */
    const drawn = usageOverTime({
        state: 'ready',
        zone: 'UTC',
        plays: 40,
        watchedMinutes: 90,
        days: [day('2026-05-01', { plays: 4, watchedMinutes: 90 })],
    });

    assert.match(drawn, /40 plays over 1 days/);
});

test('the view names no user, whatever the days carry', () => {
    const drawn = usageOverTime({
        state: 'ready',
        zone: 'UTC',
        plays: 1,
        watchedMinutes: 30,
        days: [
            {
                day: '2026-06-01',
                watchedMinutes: 30,
                /* None of these is a field the fold produces. They are here
                 * because the shape that leaks is a view composing its own text
                 * out of a day, and a view tested only with well-formed days
                 * would not show it. Measured by writing that shape: a caption
                 * naming the first day's user reddens this test and nothing else
                 * in the file. */
                userName: 'Ada',
                userId: '6f9619ff-8b86-d011-b42d-00c04fc964ff',
                itemName: 'An episode',
                delivery: {
                    plays: 1,
                    unknown: 0,
                    directPlay: 1,
                    directStream: 0,
                    transcode: 0,
                },
            },
        ],
    });

    assert.doesNotMatch(drawn, /Ada/);
    assert.doesNotMatch(drawn, /6f9619ff/);
    assert.doesNotMatch(drawn, /An episode/);
});

test('a range with no state is refused rather than drawn', () => {
    const ready = aWeek();
    delete ready.state;

    assert.throws(() => usageOverTime(ready), /state/);
    assert.throws(() => usageOverTime({ ...ready, state: 'done' }), /state/);
    assert.throws(() => usageOverTime(null), /state/);
});

test('each of the four situations is drawn as itself', () => {
    const ready = usageOverTime(aWeek());
    const empty = usageOverTime({ state: 'empty' });
    const loading = usageOverTime({ state: 'loading' });
    const failed = usageOverTime({ state: 'failed', reason: 'The store could not be opened.' });

    assert.match(ready, /<title>2026-01-05: 2<\/title>/);
    assert.match(empty, /Nothing recorded yet/);
    assert.match(loading, /Still loading/);
    assert.match(failed, /Could not be read/);
    assert.match(failed, /The store could not be opened\./);

    /* The four are told apart by what they say and not only by being different
     * strings, so a view that drew the same empty frame for all of them fails
     * here rather than passing on four markup blobs nobody compared. */
    assert.equal(new Set([ready, empty, loading, failed]).size, 4);
});
