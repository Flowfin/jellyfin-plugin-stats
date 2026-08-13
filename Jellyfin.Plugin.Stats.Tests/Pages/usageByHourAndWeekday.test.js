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
    const cells = [{ weekday: 1, hour: 23, plays: 4, watchedMinutes: 90 }];

    assert.throws(() => usageByHourAndWeekday({ cells }), /zone/);
    assert.throws(() => usageByHourAndWeekday({ zone: '', cells }), /zone/);
    assert.throws(() => usageByHourAndWeekday({ zone: '   ', cells }), /zone/);
    assert.throws(() => usageByHourAndWeekday(null), /zone/);
});

test('a figure arrives at the weekday and hour its cell names', () => {
    const drawn = usageByHourAndWeekday({
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
        () => usageByHourAndWeekday({ zone: 'UTC', cells: [] }, { figure: 'transcodes' }),
        /transcodes/,
    );
});

test('the view names no user, whatever the cells carry', () => {
    const drawn = usageByHourAndWeekday({
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
        zone: 'UTC',
        cells: [{ weekday: 0, hour: 9, watchedMinutes: 90 }],
    });
    const nought = usageByHourAndWeekday({
        zone: 'UTC',
        cells: [{ weekday: 0, hour: 9, plays: 0, watchedMinutes: 90 }],
    });

    assert.match(missing, /<title>Mon 09:00: not recorded<\/title>/);
    assert.match(nought, /<title>Mon 09:00: 0<\/title>/);
});

/**
 * A week with one busy hour in it, in the given zone.
 *
 * @param {string} zone The zone the hours were counted in.
 * @returns {string} The view.
 */
function aWeekIn(zone) {
    return usageByHourAndWeekday({
        zone,
        cells: [{ weekday: 1, hour: 23, plays: 4, watchedMinutes: 90 }],
    });
}
