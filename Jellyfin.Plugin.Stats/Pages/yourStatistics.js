/*
 * The figures a signed-in user opens about themselves: their plays, their
 * watched time, what they watched most and how much of it they finished. Issue
 * #61.
 *
 * Every other view in this directory answers a question about the server. This
 * one answers a question about one person, and it is drawn from an answer about
 * the caller, so it never names them: the reader already knows who they are. Two
 * fields are taken off each row of the top list, so the identifier the fold
 * grouped on does not reach the picture.
 *
 * It shows no server total. The first condition of #61 is that a reader may not
 * derive somebody else's figures from this page, and a server-wide number beside
 * a personal one is exactly that subtraction, drawn helpfully. Nothing here has
 * a place to put one.
 *
 * A figure the server could not answer for is drawn with the reason it could
 * not, in the place the figure would have been. That is the #66 rule seen from
 * the drawing end: a window too large for one figure degrades that figure and
 * never the page, so a reader who came for their plays still gets their plays
 * when the top list was the thing that could not be read. A figure degraded in
 * silence would read as a figure that is genuinely nought.
 *
 * Nothing here is scaled, projected or annualised, and nothing is worked out
 * from a clock. The window is named by the reader, resolved by the server in the
 * zone the setting names, and stated back here in the words the answer carried.
 * A view that subtracted thirty days from the browser's clock would be reading
 * one machine about rows another machine folded.
 *
 * No document, no window, no network and no clock, the same as the drawing
 * module. docs/headless-tests.md.
 */

import { barBreakdown, escapeText, lineSeries, stateNotice } from './charts.js';

/* What an answer may say it is. Ready is the one that carries figures; the other
 * three are the situations issue #64 asks every view to tell apart. */
const STATES = ['ready', 'empty', 'loading', 'failed'];

/* The windows a person asks about themselves in, with the words each is drawn
 * under and how its points are grouped. The three are the ruling on #61 of
 * 2026-08-29 and are the whole of what this view offers: a window a reader typed
 * would be a range the query layer refuses rather than a shorter report. */
export const WINDOWS = [
    { id: 'last30Days', words: 'Last 30 days', grouped: 'by day' },
    { id: 'last12Months', words: 'Last 12 months', grouped: 'by month' },
    { id: 'allTime', words: 'All time', grouped: null },
];

/* The headline figures, in the order they are read, with the words each one is
 * drawn under and the figure whose degradation takes it away. A figure the
 * answer does not carry is drawn as unrecorded rather than left out, so a reader
 * can tell a figure that is nought from one nobody has. */
const HEADLINES = [
    { field: 'plays', degradedAs: 'plays', words: 'Plays' },
    { field: 'watchedMinutes', degradedAs: 'watched', words: 'Minutes watched' },
    { field: 'finished', degradedAs: 'completion', words: 'Played to the end' },
    { field: 'abandoned', degradedAs: 'completion', words: 'Left unfinished' },
];

/**
 * Draws one person's own figures over the window they chose, or says which of
 * the other three situations the view is in.
 *
 * @param {{state: string, reason?: string, window?: string, zone?: string, plays?: number|null, watchedMinutes?: number|null, finished?: number|null, abandoned?: number|null, points?: ReadonlyArray<{label: string, value: number|null}>, topItems?: ReadonlyArray<{name: string|null, plays: number|null}>, degraded?: object}} answer The figures, as the server folded them, or the state it is in instead.
 * @returns {string} The view.
 */
export function yourStatistics(answer) {
    const state = stateOf(answer);

    if (state !== 'ready') {
        return (
            '<section class="stats-view stats-view-your-statistics">' +
            stateNotice(state, { title: 'Your statistics', reason: answer.reason }) +
            '</section>'
        );
    }

    const chosen = windowOf(answer);
    const zone = zoneOf(answer);
    const degraded = degradedOf(answer);

    return (
        '<section class="stats-view stats-view-your-statistics">' +
        '<h2 class="stats-view-your-statistics-title">Your statistics</h2>' +
        selector(chosen) +
        '<p class="stats-view-your-statistics-window">' +
        escapeText(windowSentence(chosen, zone)) +
        '</p>' +
        '<p class="stats-view-your-statistics-only-you">These figures are yours alone. Nothing ' +
        'on this page is a total for the server, so there is nothing here to subtract anybody ' +
        'else out of.</p>' +
        headlines(answer, degraded) +
        overTime(answer, chosen) +
        topItems(answer, degraded) +
        '</section>'
    );
}

/**
 * The sentence saying what the figures below cover.
 *
 * The zone is stated on every window and not only on the two that are grouped
 * into days. A person's day begins where the setting says it does, so an
 * all-time total is as much a reading in one zone as a month of them is, and a
 * page that named the zone only sometimes would let a reader take the other
 * windows for readings with no boundary at all.
 *
 * @param {{words: string, grouped: string|null}} chosen Which window.
 * @param {string} zone The zone its days were read in.
 * @returns {string} The sentence.
 */
function windowSentence(chosen, zone) {
    return chosen.grouped === null
        ? `Everything of yours the store still holds, in ${zone}.`
        : `The ${chosen.words.toLowerCase()}, ${chosen.grouped}, in ${zone}.`;
}

/**
 * The three windows, with the one being shown marked.
 *
 * @param {{id: string}} open Which window is being shown.
 * @returns {string} The control.
 */
function selector(open) {
    return (
        '<nav class="stats-view-your-statistics-windows"><ul>' +
        WINDOWS.map(
            (each) =>
                '<li><button type="button" class="stats-view-your-statistics-window-choice" ' +
                `data-window="${escapeText(each.id)}"` +
                `${each.id === open.id ? ' aria-current="true"' : ''}>` +
                `${escapeText(each.words)}</button></li>`,
        ).join('') +
        '</ul></nav>'
    );
}

/**
 * The headline figures, each under its words, with the ones nobody has said so
 * and the ones the server could not read carrying the reason it could not.
 *
 * @param {object} answer The figures.
 * @param {object} degraded What the server could not read, and why.
 * @returns {string} The markup.
 */
function headlines(answer, degraded) {
    let drawn = '';

    for (const headline of HEADLINES) {
        const reason = degraded[headline.degradedAs];
        const value = answer[headline.field];
        const cut = typeof reason === 'string';
        const said = cut
            ? reason
            : typeof value === 'number'
              ? String(value)
              : /* Not nought. A figure the fold could not answer for and a
                 * figure that is genuinely nought are different facts, and on
                 * this page the second is somebody's own history. */
                'not recorded';

        drawn +=
            '<div class="stats-view-your-statistics-figure' +
            (cut ? ' stats-view-your-statistics-degraded' : '') +
            `"><dt>${escapeText(headline.words)}</dt><dd>${escapeText(said)}</dd></div>`;
    }

    return `<dl class="stats-view-your-statistics-figures">${drawn}</dl>`;
}

/**
 * The watched time over the window, where the window is grouped into points.
 *
 * All time is one number and not a series, so it is drawn as the totals above
 * and nothing more. A line over a single point would be a picture of one reading
 * pretending to be a trend.
 *
 * @param {object} answer The figures.
 * @param {{grouped: string|null}} chosen Which window.
 * @returns {string} The markup, or nothing where the window has no points.
 */
function overTime(answer, chosen) {
    const points = answer.points ?? [];

    if (chosen.grouped === null || points.length === 0) {
        return '';
    }

    return lineSeries(
        points.map((point) => ({ label: point.label, value: point.value })),
        {
            title: 'Your watched time',
            description: `Minutes you watched, ${chosen.grouped}, over the window stated above.`,
        },
    );
}

/**
 * The things this person watched most, as bars.
 *
 * Two fields are read off each row. The fold groups these on the server's
 * identifier for the item and labels them with its name, and the identifier is
 * its own business: it says nothing to the person reading their own figures, and
 * a page asset is served to anybody who asks for it.
 *
 * @param {object} answer The figures.
 * @param {object} degraded What the server could not read, and why.
 * @returns {string} The markup.
 */
function topItems(answer, degraded) {
    const reason = degraded.topItems;

    if (typeof reason === 'string') {
        return (
            '<p class="stats-view-your-statistics-degraded stats-view-your-statistics-top-items">' +
            escapeText(`What you watched most: ${reason}`) +
            '</p>'
        );
    }

    const rows = answer.topItems ?? [];

    if (rows.length === 0) {
        return '';
    }

    return barBreakdown(
        rows.map((item) => ({
            label:
                typeof item.name === 'string' && item.name.trim() !== '' ? item.name : 'Not named',
            value: typeof item.plays === 'number' ? item.plays : null,
        })),
        {
            title: 'What you watched most',
            description: 'One bar per item, most plays first, over the window stated above.',
        },
    );
}

/**
 * Which of the four situations the answer says it is in, or a refusal.
 *
 * @param {unknown} answer The answer.
 * @returns {string} The state.
 */
function stateOf(answer) {
    const state = answer === null || typeof answer !== 'object' ? undefined : answer.state;

    if (!STATES.includes(state)) {
        throw new Error(
            'This view is not drawn without a state saying whether its figures are ready, ' +
                'absent, still coming or unreadable. An answer that says nothing looks exactly ' +
                `like a person who has watched nothing. The states are ${STATES.join(', ')}.`,
        );
    }

    return state;
}

/**
 * Which window the figures are over, or a refusal.
 *
 * @param {object} answer The figures.
 * @returns {{id: string, words: string, grouped: string|null}} The window.
 */
function windowOf(answer) {
    const chosen = WINDOWS.find((each) => each.id === answer.window);

    if (chosen === undefined) {
        throw new Error(
            'This view is not drawn without being told which window its figures are over. A ' +
                'page that headed them with a window of its own choosing would put one ' +
                "window's name over another window's numbers. The windows are " +
                `${WINDOWS.map((each) => each.id).join(', ')}.`,
        );
    }

    return chosen;
}

/**
 * The zone the window was read in, or a refusal.
 *
 * A window has a local midnight at each end, so a window read in one zone is a
 * different set of rows from the same window read in another. A page that stated
 * a zone it was not given would be quoting a setting rather than describing what
 * it drew.
 *
 * @param {object} answer The figures.
 * @returns {string} The zone.
 */
function zoneOf(answer) {
    const zone = answer.zone;

    if (typeof zone !== 'string' || zone.trim().length === 0) {
        throw new Error(
            'This view is not drawn without the zone its window was read in. A window has a ' +
                'local midnight at each end, so a window read in another zone is another set ' +
                'of rows and reads exactly like this one.',
        );
    }

    return zone.trim();
}

/**
 * What the server could not read in full and why, or a refusal.
 *
 * An answer carrying no such record at all is refused rather than read as
 * nothing having degraded. Reading an absent field as an assurance is how a
 * figure that was cut short reaches a reader as a figure that is simply small.
 *
 * @param {object} answer The figures.
 * @returns {object} The reasons, by figure.
 */
function degradedOf(answer) {
    const degraded = answer.degraded;

    if (degraded === null || typeof degraded !== 'object') {
        throw new Error(
            'This view is not drawn without being told which figures the server could not read ' +
                'in full, even where that is none of them. An absent record read as an ' +
                'assurance would draw a figure that was cut short as a figure that is small.',
        );
    }

    return degraded;
}
