/*
 * The daily view, working out its own midnight.
 *
 * This is the near miss for no-zone-read-in-a-page-script, and it is the other
 * end of the rule beside it: the endpoint cannot take an offset from a request
 * unless something sends one, and this is what sends one.
 *
 * Both lines below read the machine the page happens to be open on. The first
 * is the offset now, in minutes, which is the browser's offset today and not
 * the one that was in force when the rows were recorded. The second is the
 * zone name, which is closer to right and is still the wrong source: it is
 * whoever is looking rather than the zone the server rolled its days up in, so
 * two people open the same report and are shown different days.
 *
 * What replaces both is the zone travelling on the answer, named by the
 * setting the rollup was produced under, and a view that is handed figures
 * without one refusing to draw rather than picking a boundary of its own.
 */

export async function draw(target, userId, year) {
    const minutesFromUtc = -new Date().getTimezoneOffset();
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

    const answer = await fetch(
        '/Stats/Users/' + userId + '/Days/' + year + '?utcOffsetMinutes=' + minutesFromUtc
    );

    target.textContent = zone;

    return (await answer.json()).days;
}
