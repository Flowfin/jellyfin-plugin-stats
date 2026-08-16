/*
 * The view of a week's shape: when the server is busy, by hour of day and by
 * day of week.
 *
 * Load on a media server is not spread evenly, and the shape of a week is what
 * tells an administrator when a maintenance task will hurt. Issue #58.
 *
 * What this adds to the drawing beside it is the sentence a grid of hours is
 * useless without. A cell at nought is somebody's midnight, and which
 * somebody's is a fact about the zone the figures were counted in rather than
 * about the picture. The drawing cannot know it and the reader cannot guess it,
 * so this view refuses to draw a grid that does not carry one. A default would
 * be worse than the refusal: a week drawn in the wrong zone looks exactly like
 * a week drawn in the right one.
 *
 * Nothing here counts anything. The hours are read out of the rows on the
 * server, in the zone the configuration names, and this is handed the answer.
 * Reading a local hour here would mean doing it in the browser's zone, which is
 * the prior art's numeric offset in another spelling and is wrong twice a year.
 *
 * It names nobody, and that is a property of what it reads rather than of what
 * it was careful to leave out: three fields are taken off each cell and no
 * fourth is looked at.
 *
 * No document, no window, no network and no clock, the same as the drawing
 * module. docs/headless-tests.md.
 */

import { escapeText, hourGrid } from './charts.js';

/* The figures a week can be drawn by. Both are offered because they disagree:
 * an hour with many short plays and an hour with one long one are different
 * facts about the same server, and a view showing only one of them answers a
 * question the reader did not ask. */
const FIGURES = {
    plays: {
        title: 'Plays by hour and by weekday',
        sentence: 'One cell per hour of the week, darker where more plays started.',
        readingOf: (cell) => cell.plays,
    },
    watchedMinutes: {
        title: 'Watched time by hour and by weekday',
        sentence: 'One cell per hour of the week, darker where more minutes were watched.',
        readingOf: (cell) => cell.watchedMinutes,
    },
};

/**
 * Draws a week, with the zone its hours were counted in written under it.
 *
 * @param {{zone: string, cells: ReadonlyArray<{weekday: number, hour: number, plays: number|null, watchedMinutes: number|null}>}} grid The week, as the server counted it.
 * @param {{figure?: string}} [options] Which of the two figures to draw.
 * @returns {string} The view.
 */
export function usageByHourAndWeekday(grid, options = {}) {
    const zone = zoneOf(grid);
    const name = options.figure ?? 'plays';
    const figure = Object.prototype.hasOwnProperty.call(FIGURES, name) ? FIGURES[name] : null;

    if (figure === null) {
        throw new Error(
            `There is no figure called ${name}. A view asked for one it does not have would ` +
                'otherwise draw an empty week, which reads as a quiet server.',
        );
    }

    /* Three fields and no fourth. Whatever else a cell arrives carrying does
     * not reach the drawing, which is what makes "this view names no user" a
     * statement about the code rather than about the data it was tested with. */
    const cells = (grid.cells ?? []).map((cell) => ({
        weekday: cell.weekday,
        hour: cell.hour,
        value: figure.readingOf(cell) ?? null,
    }));

    return (
        '<figure class="stats-view stats-view-week">' +
        hourGrid(cells, {
            title: figure.title,
            description: `${figure.sentence} Hours are counted in ${zone}.`,
        }) +
        '<figcaption class="stats-view-zone">' +
        `Hours and days are counted in ${escapeText(zone)}. ` +
        'A play is counted in the hour it started.' +
        '</figcaption>' +
        '</figure>'
    );
}

/**
 * The zone the figures were counted in, or a refusal.
 *
 * @param {unknown} grid The week.
 * @returns {string} The zone.
 */
function zoneOf(grid) {
    const zone = grid === null || typeof grid !== 'object' ? undefined : grid.zone;

    if (typeof zone !== 'string' || zone.trim().length === 0) {
        throw new Error(
            'This view is not drawn without the zone its hours were counted in. A grid whose ' +
                'midnight belongs to nobody in particular reads exactly like a correct one, so ' +
                'the zone is carried with the figures and required here.',
        );
    }

    return zone.trim();
}
