/*
 * The three drawings the dashboard views in this plan ask for, as functions
 * from data to markup.
 *
 * Nothing here touches a document, a window, a network or a clock. Each
 * function takes values and returns a string of scalable vector markup, so the
 * same input produces the same bytes on any machine and a test can read the
 * answer without a browser. That is what lets this be covered at all: the
 * headless policy in docs/headless-tests.md refuses a test that drives a
 * browser, and replaces it with a module that has no document access and is
 * unit tested directly. This is that module.
 *
 * It is written here rather than taken from a chart library because a minified
 * bundle in the tree is code nobody in this repository reads, the formatting
 * check and the unicode guard cannot say anything useful about it, and it moves
 * only when somebody remembers. Issue #62.
 *
 * A value that is not known is written as null and is never drawn as zero. A
 * server with no plays on a day and a server whose rows for that day were
 * removed by the retention sweep are different facts, and a drawing that put a
 * zero on both would say the second was the first. What a report sends for such
 * a day is decided where the report is shaped; what this module does with it is
 * to leave a gap.
 *
 * The same argument one level up is why the words for a view with no drawing to
 * show are here rather than in each view. Nothing recorded yet, still loading
 * and could not be read are three situations a reader has to be able to tell
 * apart, and a view that renders an empty frame for all three says the same
 * thing about each. Issue #64.
 */

/* The drawing area, in user units. Every function returns markup with a
 * viewBox and no width or height attribute, so the page decides how large the
 * drawing is and the numbers below never have to move. */
const WIDTH = 640;
const HEIGHT = 240;

/* Room for the labels along the bottom and down the left. */
const PAD_LEFT = 48;
const PAD_RIGHT = 8;
const PAD_TOP = 8;
const PAD_BOTTOM = 28;

const PLOT_WIDTH = WIDTH - PAD_LEFT - PAD_RIGHT;
const PLOT_HEIGHT = HEIGHT - PAD_TOP - PAD_BOTTOM;

const HOURS_IN_A_DAY = 24;
const DAYS_IN_A_WEEK = 7;

/**
 * Escapes text on its way into markup.
 *
 * Every string this module puts into a drawing goes through here, including
 * the ones that came off a row. An item name is whatever somebody called a
 * file, and a page asset is served to anybody who asks for it, so a name
 * carrying a bracket has to arrive as text rather than as markup.
 *
 * @param {unknown} value The text.
 * @returns {string} The same text, safe to place in an element or an attribute.
 */
export function escapeText(value) {
    return String(value)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

/**
 * Writes a number into markup the same way on every machine.
 *
 * Coordinates are rounded to two places rather than left at whatever the
 * division produced, so the markup a test compares is the markup another run
 * produces. The locale is never consulted: a decimal comma in a coordinate is
 * markup a renderer reads as two numbers.
 *
 * @param {number} value The number.
 * @returns {string} The number, with a full stop for a decimal point.
 */
function coordinate(value) {
    return Number(value.toFixed(2)).toString();
}

/**
 * Whether a value is a number that can be drawn.
 *
 * Null is the absent value this module is written around. Undefined, a string
 * and a value that is not a number are absent too rather than being coerced,
 * because a drawing that turned a missing figure into a zero is the failure
 * this whole shape exists against.
 *
 * @param {unknown} value The value.
 * @returns {boolean} True where there is a number to draw.
 */
function isKnown(value) {
    return typeof value === 'number' && Number.isFinite(value);
}

/**
 * The largest value in a set of points, or zero where none is known.
 *
 * @param {ReadonlyArray<{value: number|null}>} points The points.
 * @returns {number} The largest known value.
 */
function largestValue(points) {
    let largest = 0;
    for (const point of points) {
        if (isKnown(point.value) && point.value > largest) {
            largest = point.value;
        }
    }

    return largest;
}

/**
 * Opens a drawing, with the text a reader who cannot see it is given instead.
 *
 * The title and the description are the first two children on purpose. A
 * reader using assistive technology meets them before anything else in the
 * drawing, and a drawing whose only account of itself is its shape has none for
 * that reader.
 *
 * @param {string} title What the drawing is.
 * @param {string} description What it shows, in a sentence.
 * @returns {string} The opening markup.
 */
function open(title, description) {
    return (
        `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${WIDTH} ${HEIGHT}" ` +
        `role="img" aria-label="${escapeText(title)}" class="stats-chart">` +
        `<title>${escapeText(title)}</title>` +
        `<desc>${escapeText(description)}</desc>`
    );
}

/**
 * The sentence that says how many points of a set were not known.
 *
 * It is drawn rather than left to the page, so a drawing that is missing part
 * of its range says so wherever it is shown. A set with nothing missing gets no
 * sentence, because a note saying nothing is missing is noise on every drawing
 * that is complete.
 *
 * @param {number} missing How many points had no value.
 * @param {number} total How many points there were.
 * @returns {string} The markup, or an empty string where nothing is missing.
 */
function absenceNote(missing, total) {
    if (missing === 0) {
        return '';
    }

    const sentence = `${missing} of ${total} not recorded`;
    return (
        `<text class="stats-chart-absence" x="${coordinate(WIDTH - PAD_RIGHT)}" ` +
        `y="${coordinate(HEIGHT - 8)}" text-anchor="end">${escapeText(sentence)}</text>`
    );
}

/**
 * A series over time, drawn as a line with a point on each reading.
 *
 * A point whose value is null breaks the line rather than being drawn at the
 * bottom, so a gap in the data reads as a gap.
 *
 * @param {ReadonlyArray<{label: string, value: number|null}>} points The readings, in order.
 * @param {{title?: string, description?: string}} [options] What the drawing is called.
 * @returns {string} The drawing.
 */
export function lineSeries(points, options = {}) {
    const title = options.title ?? 'Usage over time';
    const description =
        options.description ?? 'One reading per point, in the order they were taken.';

    if (points.length === 0) {
        return stateNotice('empty', { title });
    }

    const top = largestValue(points);
    const step = points.length === 1 ? 0 : PLOT_WIDTH / (points.length - 1);

    let path = '';
    let dots = '';
    let missing = 0;
    let penIsDown = false;

    points.forEach((point, index) => {
        if (!isKnown(point.value)) {
            missing += 1;
            penIsDown = false;
            return;
        }

        const x = PAD_LEFT + (points.length === 1 ? PLOT_WIDTH / 2 : index * step);
        const y = heightOf(point.value, top);

        path += `${penIsDown ? 'L' : 'M'}${coordinate(x)} ${coordinate(y)}`;
        penIsDown = true;
        dots +=
            `<circle class="stats-chart-point" cx="${coordinate(x)}" cy="${coordinate(y)}" r="2.5">` +
            `<title>${escapeText(`${point.label}: ${point.value}`)}</title></circle>`;
    });

    return (
        open(title, description) +
        axes() +
        `<path class="stats-chart-line" d="${path}" fill="none" />` +
        dots +
        edgeLabels(points) +
        absenceNote(missing, points.length) +
        '</svg>'
    );
}

/**
 * A breakdown, drawn as one horizontal bar per entry.
 *
 * Horizontal rather than upright because the labels are names, and a name
 * turned on its side or cut short is a label nobody reads. An entry whose value
 * is not known gets no bar and is counted in the note at the foot, which is the
 * same rule the series follows.
 *
 * @param {ReadonlyArray<{label: string, value: number|null}>} bars The entries, in the order to draw.
 * @param {{title?: string, description?: string}} [options] What the drawing is called.
 * @returns {string} The drawing.
 */
export function barBreakdown(bars, options = {}) {
    const title = options.title ?? 'Breakdown';
    const description =
        options.description ?? 'One bar per entry, longest first where the caller sorted them.';

    if (bars.length === 0) {
        return stateNotice('empty', { title });
    }

    const top = largestValue(bars);
    const lane = PLOT_HEIGHT / bars.length;
    const thickness = Math.max(1, lane - 4);

    let drawn = '';
    let missing = 0;

    bars.forEach((bar, index) => {
        const y = PAD_TOP + index * lane + (lane - thickness) / 2;
        drawn +=
            `<text class="stats-chart-label" x="${coordinate(PAD_LEFT - 6)}" ` +
            `y="${coordinate(y + thickness / 2)}" text-anchor="end" dominant-baseline="middle">` +
            `${escapeText(bar.label)}</text>`;

        if (!isKnown(bar.value)) {
            missing += 1;
            return;
        }

        const length = top === 0 ? 0 : (bar.value / top) * PLOT_WIDTH;
        drawn +=
            `<rect class="stats-chart-bar" x="${coordinate(PAD_LEFT)}" y="${coordinate(y)}" ` +
            `width="${coordinate(length)}" height="${coordinate(thickness)}">` +
            `<title>${escapeText(`${bar.label}: ${bar.value}`)}</title></rect>`;
    });

    return open(title, description) + drawn + absenceNote(missing, bars.length) + '</svg>';
}

/**
 * The shape of a week, drawn as a cell per hour and weekday.
 *
 * The grid is always the whole week and the whole day. A caller who sends
 * fewer cells is not sending a smaller week, they are sending a week with
 * hours nobody has a figure for, and those are drawn as absent rather than
 * being left off the drawing.
 *
 * @param {ReadonlyArray<{weekday: number, hour: number, value: number|null}>} cells The readings.
 * @param {{title?: string, description?: string, weekdayNames?: ReadonlyArray<string>}} [options] What the drawing is called and what the days are called.
 * @returns {string} The drawing.
 */
export function hourGrid(cells, options = {}) {
    const title = options.title ?? 'Usage by hour and by weekday';
    const description =
        options.description ?? 'One cell per hour of the week, darker where there was more.';
    const weekdayNames = options.weekdayNames ?? ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

    const known = new Map();
    for (const cell of cells) {
        if (!isWithinTheWeek(cell)) {
            continue;
        }

        known.set(cell.weekday * HOURS_IN_A_DAY + cell.hour, cell.value);
    }

    const top = largestValue(cells.filter(isWithinTheWeek));
    const cellWidth = PLOT_WIDTH / HOURS_IN_A_DAY;
    const cellHeight = PLOT_HEIGHT / DAYS_IN_A_WEEK;

    let drawn = '';
    let missing = 0;

    for (let weekday = 0; weekday < DAYS_IN_A_WEEK; weekday += 1) {
        drawn +=
            `<text class="stats-chart-label" x="${coordinate(PAD_LEFT - 6)}" ` +
            `y="${coordinate(PAD_TOP + weekday * cellHeight + cellHeight / 2)}" ` +
            `text-anchor="end" dominant-baseline="middle">` +
            `${escapeText(weekdayNames[weekday] ?? String(weekday))}</text>`;

        for (let hour = 0; hour < HOURS_IN_A_DAY; hour += 1) {
            const value = known.get(weekday * HOURS_IN_A_DAY + hour) ?? null;
            const x = PAD_LEFT + hour * cellWidth;
            const y = PAD_TOP + weekday * cellHeight;
            const share = isKnown(value) && top > 0 ? value / top : 0;

            if (!isKnown(value)) {
                missing += 1;
            }

            drawn +=
                `<rect class="${isKnown(value) ? 'stats-chart-cell' : 'stats-chart-cell-absent'}" ` +
                `x="${coordinate(x)}" y="${coordinate(y)}" width="${coordinate(cellWidth)}" ` +
                `height="${coordinate(cellHeight)}" fill-opacity="${coordinate(share)}">` +
                `<title>${escapeText(cellTitle(weekdayNames, weekday, hour, value))}</title></rect>`;
        }
    }

    for (let hour = 0; hour < HOURS_IN_A_DAY; hour += 3) {
        drawn +=
            `<text class="stats-chart-label" x="${coordinate(PAD_LEFT + hour * cellWidth)}" ` +
            `y="${coordinate(HEIGHT - PAD_BOTTOM + 14)}">${escapeText(String(hour))}</text>`;
    }

    return (
        open(title, description) +
        drawn +
        absenceNote(missing, DAYS_IN_A_WEEK * HOURS_IN_A_DAY) +
        '</svg>'
    );
}

/**
 * Whether a cell names an hour of the week that exists.
 *
 * @param {{weekday: number, hour: number}} cell The cell.
 * @returns {boolean} True where both are in range.
 */
function isWithinTheWeek(cell) {
    return (
        Number.isInteger(cell.weekday) &&
        Number.isInteger(cell.hour) &&
        cell.weekday >= 0 &&
        cell.weekday < DAYS_IN_A_WEEK &&
        cell.hour >= 0 &&
        cell.hour < HOURS_IN_A_DAY
    );
}

/**
 * What one cell of the week grid says when a reader stops on it.
 *
 * @param {ReadonlyArray<string>} weekdayNames The day names.
 * @param {number} weekday Which day.
 * @param {number} hour Which hour.
 * @param {number|null} value The figure, or null where there is none.
 * @returns {string} The sentence.
 */
function cellTitle(weekdayNames, weekday, hour, value) {
    const day = weekdayNames[weekday] ?? String(weekday);
    const when = `${day} ${String(hour).padStart(2, '0')}:00`;
    return isKnown(value) ? `${when}: ${value}` : `${when}: not recorded`;
}

/**
 * Where a value sits between the foot of the drawing and the top of the scale.
 *
 * @param {number} value The value.
 * @param {number} top The largest value on the drawing.
 * @returns {number} The vertical coordinate.
 */
function heightOf(value, top) {
    const share = top === 0 ? 0 : value / top;
    return PAD_TOP + PLOT_HEIGHT - share * PLOT_HEIGHT;
}

/**
 * The two lines a reading is measured against.
 *
 * @returns {string} The markup.
 */
function axes() {
    const foot = PAD_TOP + PLOT_HEIGHT;
    return (
        `<path class="stats-chart-axis" fill="none" d="M${coordinate(PAD_LEFT)} ${coordinate(PAD_TOP)}` +
        `L${coordinate(PAD_LEFT)} ${coordinate(foot)}L${coordinate(WIDTH - PAD_RIGHT)} ${coordinate(foot)}" />`
    );
}

/**
 * The first and last labels of a series, which is as many as fit without
 * overlapping on a range nobody has bounded yet.
 *
 * @param {ReadonlyArray<{label: string}>} points The points.
 * @returns {string} The markup.
 */
function edgeLabels(points) {
    const foot = HEIGHT - PAD_BOTTOM + 14;
    const first =
        `<text class="stats-chart-label" x="${coordinate(PAD_LEFT)}" y="${coordinate(foot)}">` +
        `${escapeText(points[0].label)}</text>`;

    if (points.length === 1) {
        return first;
    }

    const last =
        `<text class="stats-chart-label" x="${coordinate(WIDTH - PAD_RIGHT)}" ` +
        `y="${coordinate(foot)}" text-anchor="end">` +
        `${escapeText(points[points.length - 1].label)}</text>`;

    return first + last;
}

/* The three situations a view can be in with no figures to draw, and the words
 * for each. They are written once here because a reader who meets "Nothing
 * recorded yet" on one view and "No data" on the next has to work out whether
 * those are the same state, and because the whole point of the three is that
 * they are told apart on sight. Each carries a class of its own as well as
 * different words, so the difference survives a stylesheet that hides text and
 * is readable by a test without matching prose. Issue #64. */
const STATE_WORDS = {
    empty: {
        heading: 'Nothing recorded yet',
        sentence: 'No plays have been recorded for what this view is about.',
        className: 'stats-chart-empty',
    },
    loading: {
        heading: 'Still loading',
        sentence: 'The figures for this view have been asked for and have not arrived.',
        className: 'stats-chart-loading',
    },
    failed: {
        heading: 'Statistics unavailable',
        sentence: 'These figures could not be read. The server operator has the details of why.',
        className: 'stats-chart-failed',
    },
};

/**
 * Reads a duration the way the server writes one.
 *
 * The server writes a span as `hh:mm:ss`, with a day count and a full stop in
 * front of it once it passes twenty-four hours, and a fractional part on the
 * seconds where there is one. A page that read only the first shape would report
 * a fortnight of watching as fourteen minutes.
 *
 * It sits here rather than in the page that first needed it, because two pages
 * reading a duration two ways is two pages disagreeing about what an hour is,
 * and every view in this directory already depends on this module.
 *
 * @param {string} span The duration as the server wrote it.
 * @returns {number} The whole minutes in it.
 */
export function minutesIn(span) {
    if (typeof span !== 'string') {
        throw new Error(
            'A watched time is read as the text the server wrote, and this answer carried ' +
                'something else. Treating it as nought would draw a day somebody watched as a ' +
                'day nobody did.',
        );
    }

    const parts = /^(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(span);

    if (parts === null) {
        throw new Error(
            `A watched time of "${span}" is not a duration a page can read. It is refused ` +
                'rather than guessed at, because every guess here is a figure about somebody.',
        );
    }

    const days = parts[1] === undefined ? 0 : Number(parts[1]);

    return Math.round(
        days * 24 * 60 + Number(parts[2]) * 60 + Number(parts[3]) + Number(parts[4]) / 60,
    );
}

/**
 * The drawing a view shows in place of figures it does not have.
 *
 * A caller with no data gets a drawing that says which of the three situations
 * it is in, rather than an empty frame: an empty frame, a frame whose figures
 * have not arrived and a frame whose figures could not be read look the same,
 * and the last of those is the one a reader would otherwise only find in the
 * log.
 *
 * The words for a state are this module's and a caller adds nothing to them.
 * That is the disclosure decided on #64 on 2026-08-29 rather than a shape this
 * module happens to have: the only reason this plugin knows for a failure names
 * a file in the server's own storage, the endpoints answer a store that will not
 * open with a bare 503 carrying no reason at all, and a reader who is not the
 * operator can do nothing with a storage path but learn where the server keeps
 * things. So the failed notice says that the figures are unavailable and that
 * the operator has the details, and nothing else. The operator reads the reason
 * itself on the settings page, which is where the repair happens.
 *
 * A caller handing a reason is refused rather than ignored. Ignoring it would
 * let a page pass a server's words in and pass every check while nothing drew
 * them, so the next reader of that page would believe the reason reaches
 * somebody. Refusing puts the decision in front of whoever writes the line.
 *
 * @param {string} state One of empty, loading or failed.
 * @param {{title?: string}} [options] What the drawing is.
 * @returns {string} The drawing.
 */
export function stateNotice(state, options = {}) {
    if (!Object.prototype.hasOwnProperty.call(STATE_WORDS, state)) {
        throw new Error(
            `There is no view state called ${state}. A view in a state this module does not ` +
                'know would otherwise draw nothing, which is the empty frame the three states ' +
                'exist to replace.',
        );
    }

    // The KEY is refused and not only a key carrying something. A caller that
    // hands in `reason: undefined` reads to the next person as a caller whose
    // reason reaches somebody, and it walks through a test that can only see
    // what was drawn: the view drops the undefined, the page goes on building
    // one, and the two halves are wrong only together. That is the shape that
    // reached `master` once already. Issues #64 and #289.
    if (Object.prototype.hasOwnProperty.call(options, 'reason')) {
        throw new Error(
            'A state notice is drawn in the words this module holds, and it carries no reason ' +
                'from its caller. What a failure here can name is a file in the server storage, ' +
                'which the operator reads on the settings page and nobody else reads at all. ' +
                'Issue #64.',
        );
    }

    const words = STATE_WORDS[state];
    const middle = HEIGHT / 2;

    return (
        open(options.title ?? words.heading, words.sentence) +
        `<text class="${words.className}" x="${coordinate(WIDTH / 2)}" ` +
        `y="${coordinate(middle)}" text-anchor="middle">` +
        `${escapeText(words.heading)}</text>` +
        '</svg>'
    );
}
