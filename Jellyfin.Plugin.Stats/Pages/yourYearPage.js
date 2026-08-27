/*
 * The requests behind the wrap-up a user opens about themselves, and the mapping
 * from what the endpoints answer to what the drawing takes. Issue #67.
 *
 * Two self reads and nothing else. Which years this account has plays in, and
 * one of those years. Both are served to the calling account and refused to
 * every other account including an administrator, which is the point the issue
 * opens with: the recap applications built on the existing plugin need a
 * credential over the whole server to produce one person's year, and this needs
 * none.
 *
 * The year offered when the page opens is the newest the store holds for this
 * account, not the year the server is in. Those part company on the second of
 * January and on any account whose rows have aged out, and a page that opened
 * the current year would greet a reader with an empty wrap-up under a heading
 * saying it was theirs.
 *
 * Nothing here works out a year, a day or a zone for itself. The zone travels on
 * the answer, the years come from the store, and the day rows are kept from is
 * read where the setting and the clock are - a page that subtracted a retention
 * setting from the browser's clock would be reading two machines where the rows
 * have one.
 *
 * No document, no window, no clock and no network, the same as the drawing
 * modules beside it. What reaches the network arrives as an argument.
 * docs/headless-tests.md.
 *
 * `mountYourYear` is the one exception and it is deliberately short: it is the
 * only thing here that touches a document, and nothing in this tree can drive
 * one.
 */

import { minutesIn } from './charts.js';
import { yourYear } from './yourYear.js';

/* Where the years an account has are read. A path and not a URL: what turns it
 * into one is the client the page was handed, which is also what puts the
 * caller's credential on the request. */
export const YEARS_PATH = 'Stats/Users/{userId}/Years';

/**
 * The address one account's years are at.
 *
 * @param {string} userId The account asking, as the server names it.
 * @returns {string} The path.
 */
export function yearsPathFor(userId) {
    return YEARS_PATH.replace('{userId}', encodeURIComponent(userId));
}

/**
 * The address one calendar year of one account is at.
 *
 * @param {string} userId The account asking, as the server names it.
 * @param {number} year The calendar year.
 * @returns {string} The path.
 */
export function yearPathFor(userId, year) {
    if (!Number.isInteger(year)) {
        throw new Error(
            'A year is asked for as a whole number. A page that sent anything else would ask ' +
                'for a year nobody has and draw the refusal as a year with nothing in it.',
        );
    }

    return `${yearsPathFor(userId)}/${year}`;
}

/**
 * Which of an account's years a page opens on.
 *
 * The newest the store holds, and never the year the server is in. The two part
 * company on the second of January, and on any account whose rows have aged out,
 * and opening a year the store holds nothing of would head a page of nothing
 * with somebody's own name for it.
 *
 * @param {{held: ReadonlyArray<number>}} years The years the store holds.
 * @param {number|undefined} asked A year a reader chose, if they chose one.
 * @returns {number} The year to open.
 */
export function yearToOpen(years, asked) {
    const held = years === null || typeof years !== 'object' ? undefined : years.held;

    if (!Array.isArray(held) || held.length === 0) {
        throw new Error(
            'This account has no years in the store, so there is no year to open. That is a ' +
                'state the view says in words rather than a year to draw.',
        );
    }

    if (asked !== undefined && !held.includes(asked)) {
        throw new Error(
            `The store holds no plays of this account in ${asked}. Opening it would head a ` +
                'page of figures with a year the store says holds nothing of theirs.',
        );
    }

    return asked === undefined ? Math.max(...held) : asked;
}

/**
 * Turns what the two endpoints answered into what the drawing takes.
 *
 * Ten fields are read off the year and two off each top row, and nothing else on
 * either is looked at. The top rows in particular carry the identifier of the
 * item the server folded them from, and a page has no use for it: what a reader
 * sees is a name and a count.
 *
 * @param {object} year The year, as the endpoint folded it.
 * @param {{held: ReadonlyArray<number>, keptFrom: string|null}} years The years the store holds.
 * @returns {object} The answer in the shape the drawing reads.
 */
export function forDrawing(year, years) {
    if (year === null || typeof year !== 'object') {
        throw new Error(
            'The wrap-up is drawn from the body the endpoint returned, and this call supplied ' +
                'none.',
        );
    }

    const zone = year.zoneId;

    if (typeof zone !== 'string' || zone === '') {
        throw new Error(
            'A year is unreadable without the zone it was read in. A year has a local midnight ' +
                'at each end, so a year read in another zone is another set of rows and reads ' +
                'exactly like this one.',
        );
    }

    return {
        state: 'ready',
        year: year.year,
        zone: zone,
        years: { held: years.held, keptFrom: years.keptFrom },
        coverage: year.coverage,
        anythingRecorded: year.anythingRecorded,
        plays: year.plays,
        watchedMinutes: year.watched === null ? null : minutesIn(year.watched),
        distinctItems: year.distinctItems,
        finished: year.finished,
        abandoned: year.abandoned,
        topItems: (year.topItems ?? []).map((row) => ({ name: row.name, plays: row.plays })),
    };
}

/**
 * Asks for the years and one of them, and draws it, or draws which of the other
 * states the view is in instead.
 *
 * An account with nothing recorded is drawn as empty rather than as a failure,
 * and a request that failed is drawn as a failure rather than as an empty year.
 * A reader who has watched nothing and a plugin that could not answer are
 * different facts, and a view that drew both the same way has destroyed the
 * difference before anybody can see it. Issue #64.
 *
 * @param {{getUrl: Function, getJSON: Function}} client The dashboard's own client, which is what puts the caller's credential on the request.
 * @param {{userId: string, year?: number}} asked Whose wrap-up, and which year if a reader chose one.
 * @returns {Promise<string>} The view.
 */
export async function yourYearMarkup(client, asked) {
    let years;

    try {
        years = await client.getJSON(client.getUrl(yearsPathFor(asked.userId)));
    } catch (failure) {
        return yourYear({ state: 'failed', reason: reasonOf(failure) });
    }

    const held = years === null || typeof years !== 'object' ? undefined : years.held;

    if (!Array.isArray(held) || held.length === 0) {
        return yourYear({ state: 'empty' });
    }

    try {
        const open = yearToOpen(years, asked.year);
        const year = await client.getJSON(client.getUrl(yearPathFor(asked.userId, open)));

        return yourYear(forDrawing(year, years));
    } catch (failure) {
        return yourYear({ state: 'failed', reason: reasonOf(failure) });
    }
}

/**
 * What a failure is told to the reader as.
 *
 * @param {unknown} failure What was thrown.
 * @returns {string|undefined} The words, where there are any.
 */
function reasonOf(failure) {
    return failure instanceof Error ? failure.message : undefined;
}

/**
 * Wires the page to the requests above.
 *
 * The only function here that touches a document, and the only one no test in
 * this tree drives: the headless policy refuses a test that needs a browser, so
 * what stands behind these lines is that there are few of them and everything
 * they would otherwise decide is above. docs/headless-tests.md.
 *
 * @param {Document|Element} page The page.
 * @param {{getUrl: Function, getJSON: Function}} client The dashboard's own client.
 * @param {string} userId The account the page is opened by.
 * @returns {void}
 */
export function mountYourYear(page, client, userId) {
    const view = page.querySelector('#stats-year-view');

    const draw = (year) => {
        view.innerHTML = yourYear({ state: 'loading' });
        yourYearMarkup(client, { userId, year }).then((markup) => {
            view.innerHTML = markup;

            // The selector is part of the drawing, so it is wired after each
            // draw rather than once: the markup a reader is looking at is
            // replaced every time, and a handler bound to the old buttons is
            // bound to elements nothing points at any more.
            for (const choice of view.querySelectorAll('.stats-view-year-choice')) {
                choice.addEventListener('click', () => draw(Number(choice.dataset.year)));
            }
        });
    };

    draw(undefined);
}
