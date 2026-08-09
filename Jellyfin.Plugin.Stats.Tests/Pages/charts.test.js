/*
 * The drawing module, read as plain functions from data to markup.
 *
 * There is no document here and none is created. The headless policy replaces a
 * browser driving the pages with a module that has no document access, unit
 * tested directly, and the first test below is the one that keeps that true: it
 * fails the moment the module starts needing a document, rather than that being
 * discovered when somebody tries to run these in a browser.
 *
 * Run with the test runner built into node, which needs no packages and no lock
 * file:
 *
 *     node --test Jellyfin.Plugin.Stats.Tests/Pages/
 */

import assert from 'node:assert/strict';
import test from 'node:test';
import {
    barBreakdown,
    escapeText,
    hourGrid,
    lineSeries,
} from '../../Jellyfin.Plugin.Stats/Pages/charts.js';

/* How many cells a week of hours has. Written out rather than imported,
 * because a test that took the number from the module under test would agree
 * with it however wrong both were. */
const CELLS_IN_A_WEEK = 168;

test('the module draws with no document present', () => {
    assert.equal(typeof document, 'undefined');
    assert.equal(typeof window, 'undefined');

    const drawn = lineSeries([{ label: 'Mon', value: 1 }]);

    assert.match(drawn, /^<svg /);
});

test('a value that is not known leaves a gap and is never drawn as a zero', () => {
    const withAGap = lineSeries([
        { label: 'Mon', value: 10 },
        { label: 'Tue', value: null },
        { label: 'Wed', value: 10 },
    ]);
    const withAZero = lineSeries([
        { label: 'Mon', value: 10 },
        { label: 'Tue', value: 0 },
        { label: 'Wed', value: 10 },
    ]);

    /* Two points drawn and two segments started, so the line is broken where
     * the middle reading is missing. The zero version draws three points and
     * one unbroken line, which is the drawing this one must not be. */
    assert.equal(countOf(withAGap, '<circle'), 2);
    assert.equal(countOf(lineOf(withAGap), 'M'), 2);
    assert.equal(countOf(withAZero, '<circle'), 3);
    assert.equal(countOf(lineOf(withAZero), 'M'), 1);
    assert.notEqual(withAGap, withAZero);
});

test('a series says how many of its readings were not recorded', () => {
    const drawn = lineSeries([
        { label: 'Mon', value: 4 },
        { label: 'Tue', value: null },
        { label: 'Wed', value: null },
    ]);

    assert.match(drawn, /2 of 3 not recorded/);
});

test('a complete series carries no note about missing readings', () => {
    const drawn = lineSeries([
        { label: 'Mon', value: 4 },
        { label: 'Tue', value: 5 },
    ]);

    assert.doesNotMatch(drawn, /not recorded/);
});

test('undefined and a string are absent rather than being read as numbers', () => {
    const drawn = lineSeries([
        { label: 'Mon', value: undefined },
        { label: 'Tue', value: '7' },
        { label: 'Wed', value: Number.NaN },
    ]);

    assert.equal(countOf(drawn, '<circle'), 0);
    assert.match(drawn, /3 of 3 not recorded/);
});

test('a single reading is drawn without dividing by an empty range', () => {
    const drawn = lineSeries([{ label: 'Mon', value: 3 }]);

    assert.equal(countOf(drawn, '<circle'), 1);
    assert.doesNotMatch(drawn, /NaN|Infinity/);
});

test('a series with nothing in it says so rather than drawing an empty frame', () => {
    const drawn = lineSeries([]);

    assert.match(drawn, /Nothing recorded/);
    assert.equal(countOf(drawn, '<circle'), 0);
});

test('the longest bar fills the plot and the others are drawn in proportion', () => {
    const drawn = barBreakdown([
        { label: 'Direct play', value: 100 },
        { label: 'Transcode', value: 50 },
    ]);
    const widths = [...drawn.matchAll(/<rect class="stats-chart-bar"[^>]*width="([0-9.]+)"/g)].map(
        (match) => Number(match[1]),
    );

    assert.equal(widths.length, 2);
    assert.equal(widths[0], 584);
    assert.equal(widths[1], 292);
});

test('a bar whose value is not known is labelled and left undrawn', () => {
    const drawn = barBreakdown([
        { label: 'Direct play', value: 8 },
        { label: 'Transcode', value: null },
    ]);

    assert.equal(countOf(drawn, '<rect class="stats-chart-bar"'), 1);
    assert.match(drawn, /Transcode/);
    assert.match(drawn, /1 of 2 not recorded/);
});

test('bars of zero draw a flat set rather than dividing by nothing', () => {
    const drawn = barBreakdown([
        { label: 'One', value: 0 },
        { label: 'Two', value: 0 },
    ]);

    assert.doesNotMatch(drawn, /NaN|Infinity/);
    assert.equal(countOf(drawn, 'width="0"'), 2);
});

test('the week grid is always the whole week, whatever the caller sent', () => {
    const drawn = hourGrid([{ weekday: 0, hour: 0, value: 5 }]);

    assert.equal(countOf(drawn, '<rect class="stats-chart-cell'), CELLS_IN_A_WEEK);
    assert.equal(countOf(drawn, '<rect class="stats-chart-cell-absent"'), CELLS_IN_A_WEEK - 1);
    assert.match(drawn, new RegExp(`${CELLS_IN_A_WEEK - 1} of ${CELLS_IN_A_WEEK} not recorded`));
});

test('an hour outside the week is not drawn anywhere', () => {
    const drawn = hourGrid([
        { weekday: 7, hour: 0, value: 5 },
        { weekday: 0, hour: 24, value: 5 },
        { weekday: -1, hour: 3, value: 5 },
    ]);

    assert.equal(countOf(drawn, '<rect class="stats-chart-cell-absent"'), CELLS_IN_A_WEEK);
});

test('a cell with no figure says so where a reader stops on it', () => {
    const drawn = hourGrid([{ weekday: 1, hour: 9, value: 2 }]);

    assert.match(drawn, /<title>Tue 09:00: 2<\/title>/);
    assert.match(drawn, /<title>Mon 00:00: not recorded<\/title>/);
});

test('a name is placed as text and never as markup', () => {
    const drawn = barBreakdown([{ label: '<script>alert("x")</script>', value: 1 }]);

    assert.doesNotMatch(drawn, /<script>/);
    assert.match(drawn, /&lt;script&gt;/);
});

test('every character that would end an attribute or an element is escaped', () => {
    assert.equal(escapeText(`&<>"'`), '&amp;&lt;&gt;&quot;&#39;');
});

test('the same readings produce the same bytes on every run', () => {
    const readings = [
        { label: 'Mon', value: 1 },
        { label: 'Tue', value: 2 },
    ];

    assert.equal(lineSeries(readings), lineSeries(readings));
});

test('coordinates carry a full stop and never a decimal comma', () => {
    const drawn = hourGrid([{ weekday: 0, hour: 1, value: 1 }]);
    const widths = [...drawn.matchAll(/width="([^"]+)"/g)].map((match) => match[1]);

    assert.ok(widths.length > 0);
    for (const width of widths) {
        assert.doesNotMatch(width, /,/);
    }
});

test('every drawing opens with what it is, for a reader who cannot see it', () => {
    for (const drawn of [
        lineSeries([{ label: 'Mon', value: 1 }], { title: 'Usage over time' }),
        barBreakdown([{ label: 'Direct play', value: 1 }], { title: 'Play methods' }),
        hourGrid([{ weekday: 0, hour: 0, value: 1 }], { title: 'The shape of a week' }),
    ]) {
        assert.match(drawn, /<title>[^<]+<\/title><desc>[^<]+<\/desc>/);
        assert.match(drawn, /role="img"/);
    }
});

/**
 * How many times a fragment appears in a drawing.
 *
 * @param {string} drawn The markup.
 * @param {string} fragment The fragment.
 * @returns {number} The count.
 */
function countOf(drawn, fragment) {
    return drawn.split(fragment).length - 1;
}

/**
 * The path the series itself was drawn as, and nothing else in the markup.
 *
 * The move command that starts a segment is a single letter, and the drawing
 * also carries axes and labels holding the same letter, so counting segments
 * over the whole string counts day names as breaks in the line.
 *
 * @param {string} drawn The markup.
 * @returns {string} The path data.
 */
function lineOf(drawn) {
    const path = drawn.match(/<path class="stats-chart-line" d="([^"]*)"/);
    assert.ok(path, 'the drawing carries no series path');
    return path[1];
}
