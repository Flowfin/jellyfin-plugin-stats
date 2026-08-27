/*
 * The request behind the usage-over-time view, and the mapping from what the
 * endpoint answers to what the drawing takes. Issue #57.
 *
 * One request and never one per day. The endpoint answers every day a range
 * covers in a single body, with the day, the watched time and the delivery split
 * on each row and the range's own totals beside them, so a page that asked per
 * day would be asking for work the server has already done.
 *
 * The range is bounded by the same number the query layer enforces, and the page
 * states it rather than discovering it: a range longer than that is refused
 * before anything is opened, so a control that let a reader ask for one would
 * turn a bound into an error message. The number is written once here and a test
 * in the C# suite reads it back against the layer's own constant, because two
 * copies of a bound with nothing comparing them is a bound that drifts.
 *
 * The zone is never worked out here. The answer carries the zone its days were
 * read in, and the drawing states what it was given; reading the browser's zone
 * would answer a different question from the one the rows were folded under, and
 * `no-zone-read-in-a-page-script` refuses the two calls that would do it.
 *
 * No document, no window, no clock and no network, the same as the drawing
 * modules beside it. What reaches the network arrives as an argument, and the
 * moment a range is measured back from does too, so every function here answers
 * the same way on any machine and at any hour. docs/headless-tests.md.
 *
 * `mountUsageOverTime` is the one exception and it is deliberately four lines:
 * it is the only thing here that touches a document, and nothing in this tree
 * can drive one. What it does is read two controls, call `usageOverTimeMarkup`
 * and put the answer somewhere, and everything it would otherwise decide is in
 * the functions above it, where the node suite reaches them.
 */

import { usageOverTime } from './usageOverTime.js';

/* The longest range any shape in the query layer answers over, in days. Stated
 * rather than discovered, because the layer refuses a longer one before it opens
 * anything and a control that offered one would meet a refusal instead of a
 * shorter report. ThePageStatesTheSameBoundTheQueryLayerEnforces reads this back
 * against the layer's own constant. */
export const LONGEST_RANGE_IN_DAYS = 367;

/* Where the days come from. A path and not a URL: what turns it into one is the
 * client the page was handed, which is also what puts the caller's credential on
 * the request. */
export const USAGE_PATH = 'Stats/Reports/Usage';

/**
 * Turns a number of days into the range the endpoint is asked for, measured back
 * from a moment the caller supplies.
 *
 * The range ends at the moment given rather than at the end of today, so a page
 * opened at noon does not ask for half a day nobody has played yet and draw it
 * as a day with nothing in it. Issue #64 is where that distinction is asked for
 * across every view.
 *
 * @param {number} days How many days back to read.
 * @param {Date} now The moment the range is measured back from.
 * @returns {{from: string, to: string}} The two ends, in the shape the endpoint reads.
 */
export function rangeOf(days, now) {
    if (!Number.isInteger(days) || days < 1) {
        throw new Error(
            'A range is a whole number of days and at least one. A page that asked for none ' +
                'would draw an empty picture, which reads as a server nobody used.',
        );
    }

    if (days > LONGEST_RANGE_IN_DAYS) {
        throw new Error(
            `This plugin answers over ${LONGEST_RANGE_IN_DAYS} days at most, and ${days} is ` +
                'longer. The request would be refused rather than shortened, so it is not made.',
        );
    }

    if (!(now instanceof Date) || Number.isNaN(now.getTime())) {
        throw new Error(
            'The moment a range is measured back from is given to this function rather than ' +
                'read off a clock, and this call supplied none.',
        );
    }

    const to = new Date(now.getTime());
    const from = new Date(to.getTime() - days * 24 * 60 * 60 * 1000);

    return { from: from.toISOString(), to: to.toISOString() };
}

/**
 * Reads a duration the way the endpoint writes one.
 *
 * The server writes a span as `hh:mm:ss`, with a day count and a full stop in
 * front of it once it passes twenty-four hours, and the seconds carry a
 * fractional part where there is one. A page that read only the first shape
 * would report a fortnight of watching as fourteen minutes.
 *
 * @param {string} span The duration as the endpoint wrote it.
 * @returns {number} The whole minutes in it.
 */
export function minutesIn(span) {
    if (typeof span !== 'string') {
        throw new Error(
            'A watched time is read as the text the endpoint wrote, and this answer carried ' +
                'something else. Treating it as nought would draw a day somebody watched as a ' +
                'day nobody did.',
        );
    }

    const parts = /^(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(span);

    if (parts === null) {
        throw new Error(
            `A watched time of "${span}" is not a duration this page can read. It is refused ` +
                'rather than guessed at, because every guess here is a figure about somebody.',
        );
    }

    const days = parts[1] === undefined ? 0 : Number(parts[1]);

    return Math.round(
        days * 24 * 60 + Number(parts[2]) * 60 + Number(parts[3]) + Number(parts[4]) / 60,
    );
}

/**
 * Turns what the endpoint answered into what the drawing takes.
 *
 * Five fields are read off the answer and four off each row, and nothing else on
 * either is looked at. That is what makes "this view names no user" a statement
 * about the request path rather than about what the response happened to carry:
 * a field the endpoint began sending tomorrow would not reach the drawing.
 *
 * @param {object} answer The body the endpoint returned.
 * @returns {object} The answer in the shape the drawing reads.
 */
export function forDrawing(answer) {
    if (answer === null || typeof answer !== 'object') {
        throw new Error(
            'The days are drawn from the body the endpoint returned, and this call supplied ' +
                'none. A view drawn from nothing is a server nobody used.',
        );
    }

    const zone = answer.zoneId;

    if (typeof zone !== 'string' || zone === '') {
        throw new Error(
            'A range of days is unreadable without the zone its days were read in, and the ' +
                'answer carries none. A page that supplied one of its own would be quoting a ' +
                'setting rather than describing what it drew.',
        );
    }

    const rows = answer.rows;

    if (!Array.isArray(rows)) {
        throw new Error(
            'The days are read as a list, and this answer carries none. An answer with no ' +
                'days at all is a state rather than a range, and it is said so rather than ' +
                'drawn as an empty picture.',
        );
    }

    return {
        state: rows.length === 0 ? 'empty' : 'ready',
        zone: zone,
        plays: answer.plays,
        watchedMinutes: minutesIn(answer.watched),
        days: rows.map((row) => ({
            day: row.day,
            watchedMinutes: minutesIn(row.watched),
            delivery: {
                plays: row.delivery.plays,
                unknown: row.delivery.unknown,
                transcode: row.delivery.transcode,
            },
        })),
    };
}

/**
 * Asks for a range and draws it, or draws which of the other states the view is
 * in instead.
 *
 * A request that fails is drawn as a failure and never as an empty range. A
 * server that answered nothing and a server nobody used are different facts, and
 * a view that drew both the same way has destroyed the difference before a
 * reader can see it. Issue #64.
 *
 * @param {{getUrl: Function, getJSON: Function}} client The dashboard's own client, which is what puts the caller's credential on the request.
 * @param {{days: number, figure?: string, now: Date}} asked What to ask for and the moment to measure back from.
 * @returns {Promise<string>} The view.
 */
export async function usageOverTimeMarkup(client, asked) {
    const range = rangeOf(asked.days, asked.now);

    try {
        const answer = await client.getJSON(client.getUrl(USAGE_PATH, range));

        return usageOverTime(forDrawing(answer), { figure: asked.figure });
    } catch (failure) {
        return usageOverTime(
            { state: 'failed', reason: failure instanceof Error ? failure.message : undefined },
            { figure: asked.figure },
        );
    }
}

/**
 * The sentence the page states its bound in.
 *
 * @returns {string} What a reader is told about the longest range.
 */
export function boundSentence() {
    return (
        `This plugin answers over at most ${LONGEST_RANGE_IN_DAYS} days at a time. ` +
        'A longer range is refused rather than shortened.'
    );
}

/**
 * Wires the controls on the page to the request above.
 *
 * The only function here that touches a document, and the only one no test in
 * this tree drives: the headless policy refuses a test that needs a browser, so
 * what stands behind these lines is that there are four of them and everything
 * they would otherwise decide is above. docs/headless-tests.md.
 *
 * @param {Document|Element} page The page.
 * @param {{getUrl: Function, getJSON: Function}} client The dashboard's own client.
 * @param {() => Date} now Reads the moment a range is measured back from.
 * @returns {void}
 */
export function mountUsageOverTime(page, client, now) {
    const view = page.querySelector('#stats-usage-view');
    const days = page.querySelector('#stats-usage-days');
    const figure = page.querySelector('#stats-usage-figure');

    page.querySelector('#stats-usage-bound').textContent = boundSentence();
    days.max = String(LONGEST_RANGE_IN_DAYS);

    const draw = () => {
        view.innerHTML = usageOverTime({ state: 'loading' }, { figure: figure.value });
        usageOverTimeMarkup(client, {
            days: Number(days.value),
            figure: figure.value,
            now: now(),
        }).then((markup) => {
            view.innerHTML = markup;
        });
    };

    page.querySelector('#stats-usage-range').addEventListener('submit', (event) => {
        event.preventDefault();
        draw();
    });

    draw();
}
