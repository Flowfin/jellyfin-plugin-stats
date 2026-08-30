/*
 * The requests behind the page a signed-in user opens about themselves, and the
 * mapping from what the endpoints answer to what the two drawings take. Issue
 * #61.
 *
 * Every request this page makes is a self read: an address under the calling
 * account, which the server checks against the account the request was
 * authenticated as. `SELF_PATHS` is the whole set, it is exported so a test can
 * read it back against the requests this module actually makes, and there is no
 * other place in this file where a path is built. That is the second condition
 * of #61, and the shape it takes here is a closed set rather than a rule
 * somebody remembers: a page that reached one report endpoint would be showing
 * this reader a server total, which the first condition refuses.
 *
 * Nothing here asks for an aggregate. The two report endpoints this plugin
 * serves answer for the whole server and are served to an administrator alone,
 * and a personal figure drawn beside a server one is a subtraction with
 * somebody else on the other side of it.
 *
 * THE FIGURES ENDPOINT IS NOT ON `master` YET. The four per-user reads are #274,
 * cut out of #61 because they are Api and Aggregation work while #61's scope is
 * `Pages/`, and this page consumes them. Until they land, the figures request
 * fails and the figures half of this page draws the failure with the reason,
 * which is the state it is meant to be in when a read is unavailable rather than
 * a state anybody has to arrange. The controls beside it are unaffected: consent
 * and deletion are served today, so that half of the page works on a server
 * running this build. The path below is this page's declaration of what #274 is
 * asked to serve, held in one constant so it is one line to move if that issue
 * names its addresses differently.
 *
 * The window is named and never computed. A page that subtracted thirty days
 * from the browser's clock would be reading one machine about rows another
 * machine folded, and a day is not an interval until somebody says whose
 * midnight is meant - which this plugin answers from the setting, at the moment
 * the request is served. So the request carries a window name, and the zone
 * comes back on the answer.
 *
 * No document, no window, no clock and no network, the same as the drawing
 * modules beside it. What reaches the network arrives as an argument.
 * docs/headless-tests.md.
 *
 * `mountYourStatistics` is the one exception and everything it would otherwise
 * decide is above it, where the node suite reaches it.
 */

import { minutesIn } from './charts.js';
import { WINDOWS, yourStatistics } from './yourStatistics.js';
import { yourPrivacyChoices } from './yourPrivacyChoices.js';

/* Every address this page may reach, and the whole of it. Paths and not URLs:
 * what turns one into a URL is the client the page was handed, which is also
 * what puts the caller's credential on the request. Each carries the account in
 * the address, and the server refuses one account asking for another's rows. */
export const SELF_PATHS = {
    statistics: 'Stats/Users/{userId}/Statistics/{window}',
    consent: 'Stats/Users/{userId}/Consent',
    plays: 'Stats/Users/{userId}/Plays',
};

/* The window the page opens on. The shortest of the three, because it is the one
 * whose figures a reader can still recognise as their own last few weeks. */
export const WINDOW_OPENED_ON = 'last30Days';

/**
 * The address one account's figures over one window are at.
 *
 * @param {string} userId The account asking, as the server names it.
 * @param {string} chosen Which of the three windows.
 * @returns {string} The path.
 */
export function statisticsPathFor(userId, chosen) {
    if (!WINDOWS.some((each) => each.id === chosen)) {
        throw new Error(
            `There is no window called ${chosen}. A page that asked for one the server does ` +
                'not fold would draw the refusal as a window with nothing in it, which reads ' +
                'as a stretch of time this person watched nothing in.',
        );
    }

    return SELF_PATHS.statistics
        .replace('{userId}', encodeURIComponent(userId))
        .replace('{window}', encodeURIComponent(chosen));
}

/**
 * The address one account's answer about being named is at.
 *
 * @param {string} userId The account asking, as the server names it.
 * @returns {string} The path.
 */
export function consentPathFor(userId) {
    return SELF_PATHS.consent.replace('{userId}', encodeURIComponent(userId));
}

/**
 * The address one account's own play history is at.
 *
 * @param {string} userId The account asking, as the server names it.
 * @returns {string} The path.
 */
export function playsPathFor(userId) {
    return SELF_PATHS.plays.replace('{userId}', encodeURIComponent(userId));
}

/**
 * Turns what the figures endpoint answered into what the drawing takes.
 *
 * The fields read are the window, the zone, the four figures, the points of the
 * series, the top rows and the record of what could not be read in full, and
 * nothing else on the answer is looked at. That is what makes "this page shows
 * nothing about anybody else" a statement about this function rather than about
 * what the response happened to carry: a field the endpoint began sending
 * tomorrow would not reach the drawing.
 *
 * An account with no plays in the window is the empty state and never a page of
 * noughts. A person who has watched nothing and a plugin that could not answer
 * are different facts, and this is the page where confusing them is a statement
 * about somebody's own history. Issue #64.
 *
 * @param {object} answer The body the endpoint returned.
 * @returns {object} The answer in the shape the drawing reads.
 */
export function forDrawing(answer) {
    if (answer === null || typeof answer !== 'object') {
        throw new Error(
            'The figures are drawn from the body the endpoint returned, and this call supplied ' +
                'none. A view drawn from nothing is a person who watched nothing.',
        );
    }

    const zone = answer.zoneId;

    if (typeof zone !== 'string' || zone === '') {
        throw new Error(
            'A window is unreadable without the zone its days were read in, and the answer ' +
                'carries none. A page that supplied one of its own would be quoting a setting ' +
                'rather than describing what it drew.',
        );
    }

    if (!Number.isInteger(answer.plays)) {
        throw new Error(
            'The plays over the window are read as a whole number, and this answer carries ' +
                'none. They fold from the daily rollups and are bounded by days rather than by ' +
                'plays, so an answer without them is an answer that did not come from the fold.',
        );
    }

    return {
        state: answer.plays === 0 ? 'empty' : 'ready',
        window: answer.window,
        zone: zone,
        plays: answer.plays,
        watchedMinutes: typeof answer.watched === 'string' ? minutesIn(answer.watched) : null,
        finished: answer.finished,
        abandoned: answer.abandoned,
        points: (answer.points ?? []).map((point) => ({
            label: point.label,
            value: typeof point.watched === 'string' ? minutesIn(point.watched) : null,
        })),
        topItems: (answer.topItems ?? []).map((row) => ({ name: row.name, plays: row.plays })),
        degraded: answer.degraded,
    };
}

/**
 * Turns what the consent endpoint answered into what the controls take.
 *
 * The two timestamps the endpoint carries are not read. What a control needs is
 * where the answer stands and which words it is about, and a date beside a
 * control is a fact the reader did not ask for on the page where the fewest
 * facts should be.
 *
 * @param {object} answer The body the endpoint returned.
 * @returns {object} The answer in the shape the controls read.
 */
export function choicesForDrawing(answer) {
    if (answer === null || typeof answer !== 'object') {
        throw new Error(
            'The controls are drawn from the body the endpoint returned, and this call ' +
                'supplied none. A control offered over a state nobody read is worse than no ' +
                'control at all.',
        );
    }

    return {
        state: 'ready',
        answered: answer.answered,
        agreed: answer.agreed,
        agreedToVersion: answer.agreedToVersion,
        currentVersion: answer.currentVersion,
        wording: answer.wording,
    };
}

/**
 * What a click on the consent control is saying, read off the control itself.
 *
 * The version travels from the answer through the markup and back to the
 * server, so what is recorded is the version of the words that were on the page
 * the person read. A control that named the current version instead would record
 * an agreement to whatever the server held at the moment of the click, which on
 * a page left open across an upgrade is text nobody was shown.
 *
 * @param {{answer?: string, wordingVersion?: string}} control What the control carries.
 * @returns {{agreed: boolean, wordingVersion: number}} The body to send.
 */
export function consentAnswerFrom(control) {
    const version = Number(
        control === null || control === undefined ? NaN : control.wordingVersion,
    );

    if (!Number.isInteger(version) || version < 1) {
        throw new Error(
            'The control carries no version of the wording it was drawn from, so there is ' +
                'nothing to record an agreement against. It is not sent rather than sent with ' +
                'a version this page worked out.',
        );
    }

    return { agreed: control.answer === 'agree', wordingVersion: version };
}

/**
 * The request that records what a person is now saying.
 *
 * @param {{getUrl: Function}} client The dashboard's own client.
 * @param {string} userId The account asking.
 * @param {{agreed: boolean, wordingVersion: number}} answer What they are saying.
 * @returns {object} The request.
 */
export function consentRequest(client, userId, answer) {
    return {
        type: 'PUT',
        url: client.getUrl(consentPathFor(userId)),
        data: JSON.stringify(answer),
        contentType: 'application/json',
    };
}

/**
 * The request that removes a person's own history.
 *
 * No window is named, and that is the only spelling of "everything I have" the
 * endpoint reads. A window built here would be this page working out two
 * instants from a clock the rows were never folded against.
 *
 * @param {{getUrl: Function}} client The dashboard's own client.
 * @param {string} userId The account asking.
 * @returns {object} The request.
 */
export function deletionRequest(client, userId) {
    return { type: 'DELETE', url: client.getUrl(playsPathFor(userId)) };
}

/**
 * Asks for one window of this person's figures and for where their answer about
 * being named stands, and draws both, or draws which of the other states each
 * half is in instead.
 *
 * The two halves fail apart on purpose. A reader whose figures could not be read
 * is the reader most likely to want to withdraw or to delete, and a page that
 * drew one notice over everything would take both controls away at exactly that
 * moment.
 *
 * @param {{getUrl: Function, getJSON: Function}} client The dashboard's own client, which is what puts the caller's credential on the request.
 * @param {{userId: string, window?: string}} asked Whose figures, and which window if a reader chose one.
 * @returns {Promise<string>} The page.
 */
export async function yourStatisticsMarkup(client, asked) {
    const chosen = asked.window === undefined ? WINDOW_OPENED_ON : asked.window;
    const [figures, choices] = await Promise.all([
        figuresFor(client, asked.userId, chosen),
        choicesFor(client, asked.userId),
    ]);

    return figures + choices;
}

/**
 * The figures half.
 *
 * @param {{getUrl: Function, getJSON: Function}} client The dashboard's own client.
 * @param {string} userId The account asking.
 * @param {string} chosen Which window.
 * @returns {Promise<string>} The view.
 */
async function figuresFor(client, userId, chosen) {
    try {
        const answer = await client.getJSON(client.getUrl(statisticsPathFor(userId, chosen)));

        return yourStatistics(forDrawing(answer));
    } catch (failure) {
        return yourStatistics({ state: 'failed' });
    }
}

/**
 * The controls half.
 *
 * @param {{getUrl: Function, getJSON: Function}} client The dashboard's own client.
 * @param {string} userId The account asking.
 * @returns {Promise<string>} The view.
 */
async function choicesFor(client, userId) {
    try {
        const answer = await client.getJSON(client.getUrl(consentPathFor(userId)));

        return yourPrivacyChoices(choicesForDrawing(answer));
    } catch (failure) {
        return yourPrivacyChoices({ state: 'failed' });
    }
}

/**
 * Wires the page to the requests above.
 *
 * The only function here that touches a document, and the only one no test in
 * this tree drives: the headless policy refuses a test that needs a browser, so
 * what stands behind these lines is that everything they would otherwise decide
 * is above them. What a click on the consent control means is read by
 * `consentAnswerFrom`, both requests are built by their own functions, and the
 * drawing is the two views. docs/headless-tests.md.
 *
 * Neither control asks a second time. What each one does is on the page in front
 * of it before it is pressed, which is what the third condition of #61 asks for,
 * and a dialogue after the press is a warning about a decision somebody has
 * already made.
 *
 * @param {Document|Element} page The page.
 * @param {{getUrl: Function, getJSON: Function, ajax: Function}} client The dashboard's own client.
 * @param {string} userId The account the page is opened by.
 * @returns {void}
 */
export function mountYourStatistics(page, client, userId) {
    const view = page.querySelector('#stats-your-statistics-view');

    const draw = (chosen) => {
        view.innerHTML =
            yourStatistics({ state: 'loading' }) + yourPrivacyChoices({ state: 'loading' });

        yourStatisticsMarkup(client, { userId, window: chosen }).then((markup) => {
            view.innerHTML = markup;

            // The controls are part of the drawing, so they are wired after each
            // draw rather than once: the markup a reader is looking at is
            // replaced every time, and a handler bound to the old buttons is
            // bound to elements nothing points at any more.
            for (const choice of view.querySelectorAll(
                '.stats-view-your-statistics-window-choice',
            )) {
                choice.addEventListener('click', () => draw(choice.dataset.window));
            }

            const consenting = view.querySelector('.stats-view-your-choices-consent-control');

            if (consenting !== null) {
                consenting.addEventListener('click', () => {
                    client
                        .ajax(consentRequest(client, userId, consentAnswerFrom(consenting.dataset)))
                        .then(() => draw(chosen));
                });
            }

            const deleting = view.querySelector('.stats-view-your-choices-delete-control');

            if (deleting !== null) {
                deleting.addEventListener('click', () => {
                    client.ajax(deletionRequest(client, userId)).then(() => draw(chosen));
                });
            }
        });
    };

    draw(undefined);
}
