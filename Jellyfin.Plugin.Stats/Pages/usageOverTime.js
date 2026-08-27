/*
 * The view of how much the server is being used and whether that is going up or
 * down, a day at a time.
 *
 * It is the first question anybody asks of a statistics plugin, and the answer
 * is two figures rather than one: an evening of many short plays and an evening
 * of one long film are different facts about the same server. Issue #57.
 *
 * The plays the server had to re-encode are drawn under the day they fell on
 * rather than as a share of it. Neither the fold behind this nor the delivery
 * figures under it divide anything, on the grounds that a share over a day with
 * no plays in it has no answer, and this view does not divide either. What a
 * reader gets instead is two lines over the same days, and the distance between
 * them is the thing the plugin exists to show.
 *
 * A day the server re-encoded nothing on and a day it reported nothing about are
 * both drawn as gaps rather than as noughts where nothing is known. A day with
 * plays whose delivery was never reported has no reading on the lower line,
 * because nought there says the server sent all of them as they were, and what
 * is true is that it never said. The drawing module holds the same rule from its
 * own side, where a value that is not known is written as null and is never
 * drawn as zero. Issue #64.
 *
 * The zone is required and is never worked out here. A day is not the same
 * interval for everybody, so a series of days is unreadable without the zone
 * that produced it, and reading a local day in the browser's zone would answer a
 * different question from the one the rows were folded under. A page that stated
 * a zone it was not given would be quoting a setting rather than describing what
 * it drew.
 *
 * It names nobody, and that is a property of what it reads rather than of what
 * it was careful to leave out: three fields are taken off each day and three off
 * its delivery figures, and nothing else on either is looked at.
 *
 * The delivery figures are about the moment a play began and the caption says
 * so. A row carries the method the server reported at the start and, beside it,
 * the moment that method first changed, and the fold behind this view reads the
 * first of those two. A reader who is not told that compares the lower line
 * against what the server was doing later and finds a disagreement that is not
 * one, which is what issue #158 is about.
 *
 * No document, no window, no network and no clock, the same as the drawing
 * module. docs/headless-tests.md.
 */

import { escapeText, lineSeries, stateNotice } from './charts.js';

/* What an answer may say it is. Ready is the one that carries days; the other
 * three are the situations issue #64 asks every view to tell apart. */
const STATES = ['ready', 'empty', 'loading', 'failed'];

/* Which moment the delivery figures speak about, in the words a reader meets.
 * It is exported rather than written inline because the fold it describes is C#
 * and this is JavaScript, so nothing compiles the two together: a case in the C#
 * suite reads this line back out of the tracked file and drives the fold beside
 * it, and neither the sentence nor the behaviour can move without the other.
 * Issue #158. */
export const DELIVERY_IS_READ_AT_THE_START =
    'The delivery figures read each play by the method the server reported when it began, so a play that started as a direct play and was re-encoded partway through is counted here as a direct play.';

/* The two figures a range can be read by. Both are offered because they
 * disagree, and a view showing only one of them answers a question the reader
 * did not ask. */
const FIGURES = {
    plays: {
        title: 'Plays per day',
        sentence: 'One reading per day, in the order the days fall.',
        readingOf: (day) => day.delivery.plays,
        total: (answer) => answer.plays,
        totalWords: (value) => `${value} plays`,
    },
    watchedMinutes: {
        title: 'Watched time per day',
        sentence: 'One reading per day, in minutes watched, in the order the days fall.',
        readingOf: (day) => day.watchedMinutes,
        total: (answer) => answer.watchedMinutes,
        totalWords: (value) => `${value} minutes watched`,
    },
};

/**
 * Draws a range a day at a time, with the plays the server re-encoded under it
 * and the zone the days were read in written beneath both, or says which of the
 * other three situations the view is in.
 *
 * @param {{state: string, reason?: string, zone?: string, plays?: number, watchedMinutes?: number, days?: ReadonlyArray<{day: string, watchedMinutes: number|null, delivery: {plays: number|null, unknown: number, transcode: number}}>}} answer The range, as the server folded it, or the state it is in instead.
 * @param {{figure?: string}} [options] Which of the two figures to draw.
 * @returns {string} The view.
 */
export function usageOverTime(answer, options = {}) {
    const name = options.figure ?? 'plays';
    const figure = Object.prototype.hasOwnProperty.call(FIGURES, name) ? FIGURES[name] : null;

    if (figure === null) {
        throw new Error(
            `There is no figure called ${name}. A view asked for one it does not have would ` +
                'otherwise draw an empty range, which reads as a server nobody used.',
        );
    }

    /* The figure is resolved first because it is a fault in the caller either
     * way, and a view that swallowed it while loading would report it only on
     * the request that succeeded. */
    const state = stateOf(answer);

    if (state !== 'ready') {
        return (
            '<figure class="stats-view stats-view-range">' +
            stateNotice(state, { title: figure.title, reason: answer.reason }) +
            '</figure>'
        );
    }

    const zone = zoneOf(answer);

    /* Three fields off the day and three off its delivery figures, and nothing
     * else on either. Whatever else a day arrives carrying does not reach the
     * drawing, which is what makes "this view names no user" a statement about
     * the code rather than about the data it was tested with. */
    const days = (answer.days ?? []).map((day) => ({
        label: day.day,
        reading: figure.readingOf(day) ?? null,
        unknown: day.delivery.unknown,
        transcode: day.delivery.transcode,
        plays: day.delivery.plays,
    }));

    return (
        '<figure class="stats-view stats-view-range">' +
        lineSeries(
            days.map((day) => ({ label: day.label, value: day.reading })),
            { title: figure.title, description: `${figure.sentence} Days are read in ${zone}.` },
        ) +
        lineSeries(
            days.map((day) => ({
                label: day.label,
                /* A day whose plays all arrived without a delivery method has no
                 * reading here. Nought would say the server sent every one of
                 * them as it was, and what is true is that it never said which. */
                value: day.plays === day.unknown ? null : day.transcode,
            })),
            {
                title: 'Plays re-encoded per day',
                description:
                    'The plays above the server had to re-encode, over the same days, so the ' +
                    'two lines are read against each other.',
            },
        ) +
        '<figcaption class="stats-view-zone">' +
        escapeText(caption(answer, figure, days, zone)) +
        '</figcaption>' +
        '</figure>'
    );
}

/**
 * What the drawings say about themselves under the picture.
 *
 * The total is the one the answer carries rather than the sum of the readings.
 * The fold counts what it was handed separately from the days it produced, so
 * that a fold which lost a day shows, and a view adding the readings up and
 * calling that the total would put the two statements back together.
 *
 * @param {object} answer The range.
 * @param {{total: Function, totalWords: Function}} figure The figure being drawn.
 * @param {ReadonlyArray<{unknown: number}>} days The days being drawn.
 * @param {string} zone The zone the days were read in.
 * @returns {string} The sentence.
 */
function caption(answer, figure, days, zone) {
    const total = figure.total(answer);
    const counted =
        typeof total === 'number'
            ? `${figure.totalWords(total)} over ${days.length} days, read in ${zone}.`
            : `${days.length} days, read in ${zone}.`;

    const unreported = days.reduce((running, day) => running + day.unknown, 0);
    const delivery =
        unreported === 0
            ? ' The server reported how it delivered every play in the range.'
            : ` The server reported no delivery method for ${unreported} of those plays, so the lower line counts the plays known to have been re-encoded and not the plays that were.`;

    return `${counted} A play is counted on the day it started.${delivery} ${DELIVERY_IS_READ_AT_THE_START}`;
}

/**
 * Which of the four situations the answer says it is in, or a refusal.
 *
 * An answer that names no state is refused rather than drawn. A view cannot work
 * out from an empty range whether what is in front of it is a server nobody has
 * used, a request still in flight or a store that would not open, and the caller
 * is the only party that knows.
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
                `like a range nobody played anything in. The states are ${STATES.join(', ')}.`,
        );
    }

    return state;
}

/**
 * The zone the days were read in, or a refusal.
 *
 * @param {unknown} answer The range.
 * @returns {string} The zone.
 */
function zoneOf(answer) {
    const zone = answer === null || typeof answer !== 'object' ? undefined : answer.zone;

    if (typeof zone !== 'string' || zone.trim().length === 0) {
        throw new Error(
            'This view is not drawn without the zone its days were read in. A day is not the ' +
                'same interval for everybody, so a series whose midnight belongs to nobody in ' +
                'particular reads exactly like a correct one.',
        );
    }

    return zone.trim();
}
