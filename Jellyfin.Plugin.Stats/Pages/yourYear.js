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
 * The years a reader may move to are drawn from the list the store answered with
 * and from nothing else. The shape somebody reaches for instead runs from the
 * oldest row to the year the server is in, and it offers years the account has
 * nothing in, each of which opens empty and reads as a year they watched nothing.
 * A year the list does not carry is absent for one of two reasons and those are
 * different facts, so the selector says which. Inside what retention still keeps,
 * a missing year is a year with nothing recorded. Before the day it keeps from,
 * no year can be offered whatever was recorded in one. Issue #67.
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
 * @param {{state: string, reason?: string, year?: number, zone?: string, years?: {held: ReadonlyArray<number>, keptFrom: string|null}, anythingRecorded?: boolean, plays?: number|null, watchedMinutes?: number|null, distinctItems?: number|null, finished?: number|null, abandoned?: number|null, topItems?: ReadonlyArray<{name: string|null, plays: number|null}>, coverage?: {wholeYear: boolean, firstDayCovered: string|null, lastDayCovered: string, daysCovered: number}}} answer The year, as the server folded it, or the state it is in instead.
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
    const years = yearsOf(answer, year);

    return (
        '<section class="stats-view stats-view-year">' +
        `<h2 class="stats-view-year-title">${escapeText(`Your ${year}`)}</h2>` +
        selector(years, year) +
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
 * The years this account has plays in, most recent first, and what the absent
 * ones mean.
 *
 * Only the years handed in are offered. Filling the run from the earliest to the
 * latest would offer a year the store has nothing of this account in, and a year
 * offered like that opens empty and reads as a year they watched nothing.
 *
 * @param {{held: ReadonlyArray<number>, keptFrom: string|null}} years The years, and the day retention keeps from.
 * @param {number} open Which year is being shown.
 * @returns {string} The control.
 */
function selector(years, open) {
    const offered = [...years.held].sort((a, b) => b - a);
    const gaps = missingYears(offered);

    return (
        '<nav class="stats-view-year-selector">' +
        '<ul class="stats-view-year-choices">' +
        offered
            .map(
                (year) =>
                    `<li><button type="button" class="stats-view-year-choice" data-year="${escapeText(year)}"` +
                    `${year === open ? ' aria-current="true"' : ''}>${escapeText(year)}</button></li>`,
            )
            .join('') +
        '</ul>' +
        `<p class="stats-view-year-kept">${escapeText(keptSentence(years.keptFrom))}</p>` +
        (gaps.length === 0
            ? ''
            : `<p class="stats-view-year-gaps">${escapeText(gapSentence(gaps))}</p>`) +
        '</nav>'
    );
}

/**
 * The years between the first and the last that carry nothing.
 *
 * Every one of these is at or after the year retention keeps from, because a
 * year older than that has no rows left to put it in the list at all. So a gap
 * found here is a year with nothing recorded rather than a year that was swept,
 * and the two sentences below can say so without either of them guessing.
 *
 * @param {ReadonlyArray<number>} offered The years with plays, most recent first.
 * @returns {Array<number>} The years in between with none, most recent first.
 */
function missingYears(offered) {
    const gaps = [];

    for (let year = offered[0] - 1; year > offered[offered.length - 1]; year -= 1) {
        if (!offered.includes(year)) {
            gaps.push(year);
        }
    }

    return gaps;
}

/**
 * What the day retention keeps from means for a year that is not offered.
 *
 * It never says a swept year held anything. Whether an account watched something
 * in a year whose rows are gone is not a question anything here can answer, and
 * a sentence that answered it would be inventing the history it is apologising
 * for. What it says instead is why no earlier year can be offered.
 *
 * @param {string|null} keptFrom The earliest day retention still keeps, or null where nothing is removed by age.
 * @returns {string} The sentence.
 */
function keptSentence(keptFrom) {
    return keptFrom === null
        ? 'Nothing here is removed by age, so a year that is not offered is a year with ' +
              'nothing of yours recorded in it.'
        : `Plays from before ${keptFrom} are not kept, so no earlier year can be offered ` +
              'here, whatever was recorded in one.';
}

/**
 * What a year inside the offered run that carries nothing means.
 *
 * @param {ReadonlyArray<number>} gaps The years with none, most recent first.
 * @returns {string} The sentence.
 */
function gapSentence(gaps) {
    const listed = [...gaps].reverse().join(', ');

    return (
        `${listed} ${gaps.length === 1 ? 'is' : 'are'} inside what is kept and ` +
        `${gaps.length === 1 ? 'has' : 'have'} nothing of yours recorded, so ` +
        `${gaps.length === 1 ? 'it is' : 'they are'} not offered.`
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

/**
 * The years the store answered with, or a refusal.
 *
 * A wrap-up drawn without them is a page telling somebody this is the only year
 * of theirs there is, which is a statement about their history made by a page
 * that was never handed the answer to it.
 *
 * The year being shown has to be one of them. An answer where it is not is two
 * readings that disagree, and drawing it would put a year the store says holds
 * nothing of this account under a page of that account's figures.
 *
 * @param {object} answer The year.
 * @param {number} open Which year is being shown.
 * @returns {{held: ReadonlyArray<number>, keptFrom: string|null}} The years.
 */
function yearsOf(answer, open) {
    const years = answer.years;

    if (years === null || typeof years !== 'object') {
        throw new Error(
            'This view is not drawn without the years the store holds for this account. A ' +
                'wrap-up with no way to reach the others tells a reader this is the only year ' +
                'of theirs there is, which is a claim about their history.',
        );
    }

    const held = years.held;

    if (!Array.isArray(held) || held.length === 0 || !held.every(Number.isInteger)) {
        throw new Error(
            'The years this account has plays in are read as a list of whole years, and this ' +
                'answer carries none. A selector that fell back to a run of years would offer ' +
                'years the store holds nothing of theirs in.',
        );
    }

    if (!held.includes(open)) {
        throw new Error(
            'The year being drawn is not among the years the store holds for this account, so ' +
                'the two halves of this answer disagree. Drawing it would head a page of ' +
                'figures with a year the store says holds nothing of theirs.',
        );
    }

    if (years.keptFrom !== null && typeof years.keptFrom !== 'string') {
        throw new Error(
            'This view is not drawn without being told the day retention keeps from, or null ' +
                'where nothing is removed by age. Reading an absent one as null would turn a ' +
                'fact nobody supplied into an assurance that nothing has been swept.',
        );
    }

    return { held, keptFrom: years.keptFrom };
}
