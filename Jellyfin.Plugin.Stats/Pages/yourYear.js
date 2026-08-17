/*
 * The wrap-up a signed-in user opens about their own year.
 *
 * The recap applications built on the prior art need an administrator's key to
 * produce one, which is the thing this plugin exists not to need. This is the
 * drawing half of that page: figures in, markup out, and no credential anywhere
 * because nothing here fetches anything. Issue #67.
 *
 * What it will not do is make a year look whole. A wrap-up computed from rows a
 * retention sweep has been through is a shorter wrap-up wearing a yearly title,
 * so the window is stated before any figure is drawn, in the same view and not
 * in a footnote somebody scrolls past. Issue #69 holds why, and the fold already
 * carries the coverage; what this adds is that a reader meets it first.
 *
 * Nothing here is scaled, projected or annualised. A figure over four months is
 * drawn as a figure over four months beside a sentence saying so. Multiplying it
 * out would be the page inventing plays the rows do not hold, and it is the
 * shape somebody reaches for when a wrap-up looks thin.
 *
 * A figure the fold could not answer for is drawn as unrecorded rather than as
 * nought, and a year with nothing in it says that rather than showing a page of
 * noughts. A user who watched nothing and a user whose rows are gone are
 * different facts, and this is the page where confusing them is a statement
 * about somebody's own history. Issue #64.
 *
 * It names one person, which is the point of it, and it never names them: it is
 * drawn from an answer about the caller, and the caller already knows who they
 * are. Two fields are taken off each row of a top list, so an identifier the fold
 * carries for its own grouping does not reach the picture.
 *
 * No document, no window, no network and no clock, the same as the drawing
 * module. docs/headless-tests.md.
 */

import { barBreakdown, escapeText, stateNotice } from './charts.js';

/* What an answer may say it is. Ready is the one that carries figures; the other
 * three are the situations issue #64 asks every view to tell apart. */
const STATES = ['ready', 'empty', 'loading', 'failed'];

/* The headline figures, in the order they are read, with the words each one is
 * drawn under. A figure the answer does not carry is drawn as unrecorded rather
 * than left out, so a reader can tell a figure that is nought from one nobody
 * has. */
const HEADLINES = [
    { field: 'plays', words: 'Plays' },
    { field: 'watchedMinutes', words: 'Minutes watched' },
    { field: 'distinctItems', words: 'Different things watched' },
    { field: 'finished', words: 'Played to the end' },
    { field: 'abandoned', words: 'Left unfinished' },
];

/**
 * Draws one person's year, with the part of it the store could still answer for
 * stated above the figures, or says which of the other three situations the view
 * is in.
 *
 * @param {{state: string, reason?: string, year?: number, zone?: string, anythingRecorded?: boolean, plays?: number|null, watchedMinutes?: number|null, distinctItems?: number|null, finished?: number|null, abandoned?: number|null, topItems?: ReadonlyArray<{name: string|null, plays: number|null}>, coverage?: {wholeYear: boolean, firstDayCovered: string|null, lastDayCovered: string, daysCovered: number}}} answer The year, as the server folded it, or the state it is in instead.
 * @returns {string} The view.
 */
export function yourYear(answer) {
    const state = stateOf(answer);

    if (state !== 'ready') {
        return (
            '<section class="stats-view stats-view-year">' +
            stateNotice(state, { title: 'Your year', reason: answer.reason }) +
            '</section>'
        );
    }

    const year = yearOf(answer);
    const zone = zoneOf(answer);

    return (
        '<section class="stats-view stats-view-year">' +
        `<h2 class="stats-view-year-title">${escapeText(`Your ${year}`)}</h2>` +
        `<p class="stats-view-year-window">${escapeText(windowCovered(answer, year, zone))}</p>` +
        (answer.anythingRecorded === true
            ? headlines(answer) + topItems(answer)
            : '<p class="stats-view-year-nothing">Nothing of yours was recorded in this ' +
              'window, so there are no figures to show. That is not the same as a year in ' +
              'which you watched nothing.</p>') +
        '</section>'
    );
}

/**
 * What part of the year the figures were folded over.
 *
 * This is drawn whether or not the year is whole, and above the figures rather
 * than under them. A wrap-up that says what it covers only where the answer is
 * partial leaves a reader who never sees the sentence assuming every wrap-up
 * they have read was a whole year.
 *
 * @param {object} answer The year.
 * @param {number} year Which year it is.
 * @param {string} zone The zone its days were read in.
 * @returns {string} The sentence.
 */
function windowCovered(answer, year, zone) {
    const coverage = answer.coverage;

    if (coverage === null || typeof coverage !== 'object') {
        throw new Error(
            'This view is not drawn without the window its figures were folded over. A ' +
                'wrap-up computed from rows a retention sweep has been through is a shorter ' +
                'wrap-up under a yearly title, and nothing in the figures says which it is.',
        );
    }

    if (coverage.wholeYear === true) {
        return `The whole of ${year}, in ${zone}.`;
    }

    const from = coverage.firstDayCovered;
    const days = coverage.daysCovered;

    if (typeof from !== 'string' || from === '') {
        return (
            `Part of ${year} only, in ${zone}. Nothing older survives in the store, so the ` +
            'figures below are not the whole year and are not scaled up to it.'
        );
    }

    return (
        `${from} to ${coverage.lastDayCovered} only, which is ${days} days of ${year}, in ` +
        `${zone}. The rest of the year is not in the store any more, so the figures below ` +
        'are over those days and are not scaled up to a year.'
    );
}

/**
 * The headline figures, each under its words, with the ones nobody has said so.
 *
 * @param {object} answer The year.
 * @returns {string} The markup.
 */
function headlines(answer) {
    let drawn = '';

    for (const headline of HEADLINES) {
        const value = answer[headline.field];
        const said =
            typeof value === 'number'
                ? String(value)
                : /* Not nought. A figure the fold could not answer for and a
                   * figure that is genuinely nought are different facts, and on
                   * this page the second is somebody's year and the first is a
                   * gap in what was kept. */
                  'not recorded';

        drawn +=
            `<div class="stats-view-year-figure"><dt>${escapeText(headline.words)}</dt>` +
            `<dd>${escapeText(said)}</dd></div>`;
    }

    return `<dl class="stats-view-year-figures">${drawn}</dl>`;
}

/**
 * The things watched most, as bars.
 *
 * Two fields are read off each row. The fold groups these on the server's
 * identifier for the item and labels them with its name, and the identifier is
 * its own business: it says nothing to the person reading their year, and a page
 * asset is served to anybody who asks for it.
 *
 * @param {object} answer The year.
 * @returns {string} The markup.
 */
function topItems(answer) {
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
                `like a year somebody watched nothing in. The states are ${STATES.join(', ')}.`,
        );
    }

    return state;
}

/**
 * Which year the figures are about, or a refusal.
 *
 * @param {object} answer The year.
 * @returns {number} The year.
 */
function yearOf(answer) {
    const year = answer.year;

    if (!Number.isInteger(year)) {
        throw new Error(
            'This view is not drawn without the year it is about. A wrap-up headed by a year ' +
                'a page worked out for itself would name one year over another year of figures.',
        );
    }

    return year;
}

/**
 * The zone the year was read in, or a refusal.
 *
 * A year has two ends and both of them are a local midnight, so a year read in
 * one zone and a year read in another are different sets of rows. A page that
 * stated a zone it was not given would be quoting a setting rather than
 * describing what it drew.
 *
 * @param {object} answer The year.
 * @returns {string} The zone.
 */
function zoneOf(answer) {
    const zone = answer.zone;

    if (typeof zone !== 'string' || zone.trim().length === 0) {
        throw new Error(
            'This view is not drawn without the zone its year was read in. A year has a local ' +
                'midnight at each end, so a year read in another zone is another set of rows ' +
                'and reads exactly like this one.',
        );
    }

    return zone.trim();
}
