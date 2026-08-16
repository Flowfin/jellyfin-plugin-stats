/*
 * The week view, read as a function from figures to markup.
 *
 * The three conditions of issue #58 are what these are written against. The
 * view states the zone it was computed in, a play appears in the hour the zone
 * says, and the view names nobody. The first and third are asserted here; the
 * middle one is a chain, and this end of it is that a cell arrives at the
 * weekday and hour it names. The other end, that a play lands in the cell the
 * zone puts it in, is HourAndWeekdayGridTests in the .NET suite, including
 * across a summer change.
 *
 * The state tests at the foot are the first condition of issue #64, which asks
 * every view for a test of each of nothing recorded yet, still loading and
 * could not be read. This is the one view in the tree, so they are here.
 *
 * Run with the test runner built into node:
 *
 *     node --test Jellyfin.Plugin.Stats.Tests/Pages/
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import { usageByHourAndWeekday } from '../../Jellyfin.Plugin.Stats/Pages/usageByHourAndWeekday.js';

test('the view draws with no document present', () => {
    assert.equal(typeof document, 'undefined');
    assert.equal(typeof window, 'undefined');

    assert.match(aWeekIn('UTC'), /^<figure /);
});

test('the view states the zone its hours were counted in', () => {
    const drawn = aWeekIn('Europe/Berlin');

    /* Twice, and deliberately. The caption is what a reader sees and the
     * description is what a reader who cannot see the drawing is given
     * instead, and a view that says it in only one of the two leaves one of
     * them looking at a grid whose midnight is anybody's. */
    assert.match(drawn, /<figcaption[^>]*>Hours and days are counted in Europe\/Berlin\./);
    assert.match(drawn, /<desc>[^<]*Hours are counted in Europe\/Berlin\.<\/desc>/);
});

test('a week with no zone is refused rather than drawn', () => {
    const ready = {
        state: 'ready',
        cells: [{ weekday: 1, hour: 23, plays: 4, watchedMinutes: 90 }],
    };

    assert.throws(() => usageByHourAndWeekday(ready), /zone/);
    assert.throws(() => usageByHourAndWeekday({ ...ready, zone: '' }), /zone/);
    assert.throws(() => usageByHourAndWeekday({ ...ready, zone: '   ' }), /zone/);
});

test('a figure arrives at the weekday and hour its cell names', () => {
    const drawn = usageByHourAndWeekday({
        state: 'ready',
        zone: 'Europe/Berlin',
        cells: [{ weekday: 1, hour: 23, plays: 4, watchedMinutes: 90 }],
    });

    /* Tuesday is the second row the grid draws, and the cell a reader stops on
     * says which hour of which day it is. A figure that arrived a row or an
     * hour out would be drawn under another name and read as true. */
    assert.match(drawn, /<title>Tue 23:00: 4<\/title>/);
    assert.match(drawn, /<title>Tue 22:00: not recorded<\/title>/);
    assert.match(drawn, /<title>Wed 23:00: not recorded<\/title>/);
});

test('the view draws the figure it was asked for', () => {
    const week = {
        state: 'ready',
        zone: 'UTC',
        cells: [{ weekday: 0, hour: 9, plays: 4, watchedMinutes: 90 }],
    };

    const plays = usageByHourAndWeekday(week);
    const watched = usageByHourAndWeekday(week, { figure: 'watchedMinutes' });

    assert.match(plays, /<title>Mon 09:00: 4<\/title>/);
    assert.match(watched, /<title>Mon 09:00: 90<\/title>/);
    assert.match(plays, /<title>Plays by hour and by weekday<\/title>/);
    assert.match(watched, /<title>Watched time by hour and by weekday<\/title>/);
});

test('a figure this view does not have is refused rather than drawn empty', () => {
    assert.throws(
        () =>
            usageByHourAndWeekday(
                { state: 'ready', zone: 'UTC', cells: [] },
                { figure: 'transcodes' },
            ),
        /transcodes/,
    );
});

test('the view names no user, whatever the cells carry', () => {
    const drawn = usageByHourAndWeekday({
        state: 'ready',
        zone: 'UTC',
        cells: [
            {
                weekday: 3,
                hour: 20,
                plays: 2,
                watchedMinutes: 51,
                /* None of these is a field the server sends. They are here
                 * because the shape that leaks is a view composing its own
                 * text out of a cell, and a view tested only with well-formed
                 * cells would not show it. Measured by writing that shape: a
                 * caption naming the first cell's user reddens this test and
                 * nothing else in the file.
                 *
                 * What it does not catch is a mapping widened to hand the
                 * whole cell to the drawing, because the drawing reads three
                 * fields off a cell and looks at no fourth. That half is held
                 * there rather than here, and this test passes under it. */
                userName: 'Ada',
                userId: '6f9619ff-8b86-d011-b42d-00c04fc964ff',
                itemName: 'An episode',
                deviceName: 'A browser',
            },
        ],
    });

    assert.doesNotMatch(drawn, /Ada/);
    assert.doesNotMatch(drawn, /6f9619ff/);
    assert.doesNotMatch(drawn, /An episode/);
    assert.doesNotMatch(drawn, /A browser/);
    assert.match(drawn, /<title>Thu 20:00: 2<\/title>/);
});

test('a cell with no figure stays absent and is never drawn as a zero', () => {
    const missing = usageByHourAndWeekday({
        state: 'ready',
        zone: 'UTC',
        cells: [{ weekday: 0, hour: 9, watchedMinutes: 90 }],
    });
    const nought = usageByHourAndWeekday({
        state: 'ready',
        zone: 'UTC',
        cells: [{ weekday: 0, hour: 9, plays: 0, watchedMinutes: 90 }],
    });

    assert.match(missing, /<title>Mon 09:00: not recorded<\/title>/);
    assert.match(nought, /<title>Mon 09:00: 0<\/title>/);
});

test('a view with nothing recorded says so and draws no week', () => {
    const drawn = usageByHourAndWeekday({ state: 'empty' });

    assert.match(drawn, /Nothing recorded yet/);
    assert.equal(countOf(drawn, '<rect'), 0);
});

test('a view still waiting for its figures says so and draws no week', () => {
    const drawn = usageByHourAndWeekday({ state: 'loading' });

    assert.match(drawn, /Still loading/);
    assert.equal(countOf(drawn, '<rect'), 0);
});

test('a view whose figures could not be read says so, with the reason', () => {
    const drawn = usageByHourAndWeekday({
        state: 'failed',
        reason: 'The store could not be opened.',
    });

    assert.match(drawn, /Could not be read/);
    assert.match(drawn, /The store could not be opened\./);
    assert.equal(countOf(drawn, '<rect'), 0);
});

test('a reader tells a failure from an empty view without opening the log', () => {
    const nothing = usageByHourAndWeekday({ state: 'empty' });
    const broken = usageByHourAndWeekday({ state: 'failed' });

    /* The two situations this view is most often in with no figures to draw,
     * and the pair the whole of issue #64 turns on: a fresh install and a
     * store that would not open both have nothing to show, and drawn the same
     * way the second reads as the first. */
    assert.notEqual(nothing, broken);
    assert.doesNotMatch(nothing, /Could not be read/);
    assert.doesNotMatch(broken, /Nothing recorded/);
});

test('a state carries no zone requirement, because there is nothing counted to name one for', () => {
    /* The zone is a fact about figures. A view that demanded one while its
     * request was still in flight could not draw the loading state at all,
     * and the state a caller cannot reach is the state nobody shows. */
    assert.match(usageByHourAndWeekday({ state: 'loading' }), /^<figure /);
    assert.match(usageByHourAndWeekday({ state: 'failed' }), /^<figure /);
    assert.match(usageByHourAndWeekday({ state: 'empty' }), /^<figure /);
});

test('an answer that does not say which state it is in is refused', () => {
    const cells = [{ weekday: 1, hour: 23, plays: 4, watchedMinutes: 90 }];

    assert.throws(() => usageByHourAndWeekday({ zone: 'UTC', cells }), /state/);
    assert.throws(() => usageByHourAndWeekday({ state: 'sideways', zone: 'UTC', cells }), /state/);
    assert.throws(() => usageByHourAndWeekday(null), /state/);
});

test('the state a view is in does not excuse a figure it does not have', () => {
    assert.throws(
        () => usageByHourAndWeekday({ state: 'loading' }, { figure: 'transcodes' }),
        /transcodes/,
    );
});

/**
 * A week with one busy hour in it, in the given zone.
 *
 * @param {string} zone The zone the hours were counted in.
 * @returns {string} The view.
 */
function aWeekIn(zone) {
    return usageByHourAndWeekday({
        state: 'ready',
        zone,
        cells: [{ weekday: 1, hour: 23, plays: 4, watchedMinutes: 90 }],
    });
}

/**
 * How many times a fragment appears in the markup.
 *
 * @param {string} drawn The markup.
 * @param {string} fragment The fragment.
 * @returns {number} The count.
 */
function countOf(drawn, fragment) {
    return drawn.split(fragment).length - 1;
}
