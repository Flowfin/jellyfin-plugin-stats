/*
 * The wrap-up view, read as a function from figures to markup.
 *
 * None of the three conditions of issue #67 is asserted here and none is
 * claimed. They are about the request: no elevated credential in the request
 * path, another user's wrap-up refused in the authorization matrix, and a year
 * selector over the years the store holds. There is no request and no store
 * behind this, and what it draws is handed to it.
 *
 * What is asserted is the rule issue #69 settled and this page is where a reader
 * meets it: a wrap-up over part of a year says so, and no figure is scaled up to
 * a whole one.
 *
 * Run with the test runner built into node:
 *
 *     node --test Jellyfin.Plugin.Stats.Tests/Pages/
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import { yourYear } from '../../Jellyfin.Plugin.Stats/Pages/yourYear.js';

/**
 * A year with figures in it, whole unless the coverage says otherwise.
 *
 * @param {object} [coverage] What part of the year the store could answer for.
 * @returns {object} The answer.
 */
function aYear(coverage) {
    return {
        state: 'ready',
        year: 2025,
        zone: 'Europe/Berlin',
        anythingRecorded: true,
        plays: 120,
        watchedMinutes: 5400,
        distinctItems: 44,
        finished: 90,
        abandoned: 30,
        topItems: [
            { name: 'A film', plays: 9 },
            { name: 'An episode', plays: 4 },
        ],
        coverage: coverage ?? {
            wholeYear: true,
            firstDayCovered: '2025-01-01',
            lastDayCovered: '2025-12-31',
            daysCovered: 365,
        },
    };
}

test('the view draws with no document present', () => {
    assert.equal(typeof document, 'undefined');
    assert.equal(typeof window, 'undefined');

    assert.match(yourYear(aYear()), /^<section /);
});

test('a whole year says so, in the zone it was read in', () => {
    assert.match(
        yourYear(aYear()),
        /<p class="stats-view-year-window">The whole of 2025, in Europe\/Berlin\.<\/p>/,
    );
});

test('a year the store holds only part of says which part, before any figure', () => {
    const drawn = yourYear(
        aYear({
            wholeYear: false,
            firstDayCovered: '2025-09-01',
            lastDayCovered: '2025-12-31',
            daysCovered: 122,
        }),
    );

    assert.match(drawn, /2025-09-01 to 2025-12-31 only, which is 122 days of 2025/);
    assert.match(drawn, /not scaled up to a year/);

    /* Before, and not underneath. A reader who meets the figures first has
     * already read a hundred and twenty plays as a year by the time the sentence
     * arrives. */
    assert.ok(drawn.indexOf('122 days of 2025') < drawn.indexOf('stats-view-year-figures'));
});

test('no figure is scaled up to a full year', () => {
    /* The same figures under a whole year and under a third of one. A view that
     * projected would have to draw them differently, and this is the assertion
     * that says it must not: the store holds a hundred and twenty plays either
     * way, and the sentence above them is what changes. */
    const whole = yourYear(aYear());
    const part = yourYear(
        aYear({
            wholeYear: false,
            firstDayCovered: '2025-09-01',
            lastDayCovered: '2025-12-31',
            daysCovered: 122,
        }),
    );

    for (const figure of ['>120<', '>5400<', '>44<', '>90<', '>30<']) {
        assert.ok(whole.includes(figure), `the whole year is missing ${figure}`);
        assert.ok(part.includes(figure), `the partial year is missing ${figure}`);
    }
});

test('a year with no coverage on it is refused rather than drawn as a whole one', () => {
    const answer = aYear();
    delete answer.coverage;

    assert.throws(() => yourYear(answer), /window/);
});

test('a figure the fold could not answer for is drawn as unrecorded rather than as nought', () => {
    const answer = aYear();
    answer.distinctItems = null;

    const drawn = yourYear(answer);

    assert.match(drawn, /<dt>Different things watched<\/dt><dd>not recorded<\/dd>/);
    assert.doesNotMatch(drawn, /<dt>Different things watched<\/dt><dd>0<\/dd>/);
});

test('a figure that is genuinely nought is drawn as nought', () => {
    const answer = aYear();
    answer.abandoned = 0;

    assert.match(yourYear(answer), /<dt>Left unfinished<\/dt><dd>0<\/dd>/);
});

test('a year with nothing recorded says so rather than showing a page of noughts', () => {
    const answer = aYear();
    answer.anythingRecorded = false;

    const drawn = yourYear(answer);

    assert.match(drawn, /Nothing of yours was recorded in this window/);
    assert.match(drawn, /not the same as a year in which you watched nothing/);
    assert.doesNotMatch(drawn, /stats-view-year-figures/);
});

test('a year with no zone is refused rather than drawn', () => {
    const answer = aYear();

    assert.throws(() => yourYear({ ...answer, zone: undefined }), /zone/);
    assert.throws(() => yourYear({ ...answer, zone: '  ' }), /zone/);
});

test('a wrap-up with no year on it is refused rather than headed by a guess', () => {
    const answer = aYear();
    delete answer.year;

    assert.throws(() => yourYear(answer), /year/);
});

test('the top list carries the names and not the identifiers the fold groups on', () => {
    const answer = aYear();
    answer.topItems = [
        {
            /* The fold's own key, which is the server's identifier for the item.
             * It is here because a view widened to hand a whole row to the
             * drawing is the shape that leaks one, and a suite testing only with
             * rows shaped the way the view expects would not show it. */
            key: '6f9619ff-8b86-d011-b42d-00c04fc964ff',
            name: 'A film',
            plays: 9,
            watchedMinutes: 300,
        },
        { key: '00000000-0000-0000-0000-000000000001', name: null, plays: 2 },
    ];

    const drawn = yourYear(answer);

    assert.match(drawn, /<title>A film: 9<\/title>/);
    assert.match(drawn, /<title>Not named: 2<\/title>/);
    assert.doesNotMatch(drawn, /6f9619ff/);
    assert.doesNotMatch(drawn, /00000000-0000/);
});

test('a year with no state is refused rather than drawn', () => {
    const answer = aYear();
    delete answer.state;

    assert.throws(() => yourYear(answer), /state/);
    assert.throws(() => yourYear({ ...answer, state: 'done' }), /state/);
    assert.throws(() => yourYear(null), /state/);
});

test('each of the four situations is drawn as itself', () => {
    const ready = yourYear(aYear());
    const empty = yourYear({ state: 'empty' });
    const loading = yourYear({ state: 'loading' });
    const failed = yourYear({ state: 'failed', reason: 'The store could not be opened.' });

    assert.match(ready, /Your 2025/);
    assert.match(empty, /Nothing recorded yet/);
    assert.match(loading, /Still loading/);
    assert.match(failed, /Could not be read/);
    assert.match(failed, /The store could not be opened\./);

    /* The four are told apart by what they say and not only by being different
     * strings, so a view that drew the same empty frame for all of them fails
     * here rather than passing on four markup blobs nobody compared. */
    assert.equal(new Set([ready, empty, loading, failed]).size, 4);
});
