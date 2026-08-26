// Who may see what, as a table, and two tests that stop the table drifting away
// from the endpoints it is about.
//
// Issue #47 asks for three things and each is one of the tests below. Adding an
// endpoint without adding its row fails the suite. Removing an authorization
// attribute from any endpoint fails the suite. And the table is readable on its
// own as a statement of who may see what, which is why it is data with names in
// it rather than a set of test methods whose names happen to describe cases.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.Tests.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// The authorization matrix, and the two walks that keep it honest.
/// </summary>
public class AuthorizationMatrixTests
{
    /// <summary>
    /// Every endpoint this plugin serves, and what each of the four callers
    /// gets from it.
    /// </summary>
    /// <remarks>
    /// Read down a row and it says who may see what:
    /// <code>
    /// endpoint                                   | rows asked for | anonymous | an ordinary user | a different ordinary user | an administrator
    /// GET    /Stats/Users/{userId}/Years/{year}   | their own      | 401       | 200              | 200                       | 200
    /// GET    /Stats/Users/{userId}/Years/{year}   | somebody else's| 401       | 403              | 403                       | 403
    /// DELETE /Stats/Users/{userId}/Plays          | their own      | 401       | 200              | 200                       | 200
    /// DELETE /Stats/Users/{userId}/Plays          | somebody else's| 401       | 403              | 403                       | 403
    /// GET    /Stats/Users/{userId}/Consent        | their own      | 401       | 200              | 200                       | 200
    /// GET    /Stats/Users/{userId}/Consent        | somebody else's| 401       | 403              | 403                       | 403
    /// PUT    /Stats/Users/{userId}/Consent        | their own      | 401       | 200              | 200                       | 200
    /// PUT    /Stats/Users/{userId}/Consent        | somebody else's| 401       | 403              | 403                       | 403
    /// GET    /Stats/Reports/Top                     | nobody's       | 401       | 403              | 403                       | 200
    /// GET    /Stats/Reports/Breakdown               | nobody's       | 401       | 403              | 403                       | 200
    /// </code>
    /// The last two rows are the ones whose answer is not about a person, and
    /// they are the only rows so far with a 200 in the administrator cell
    /// alone. Who may ask for an aggregate view was decided on issue #55
    /// on 2026-08-24 and the answer is an administrator only, on least
    /// privilege; the two ordinary cells are that decision and not a rule about
    /// whose rows are whose, which is why the row asks for nobody's.
    /// The two consent rows about somebody else's answer are the first
    /// condition of issue #42. An administrator cannot set a person's consent
    /// for them and cannot read what they said, and a consent an administrator
    /// could record is not consent.
    /// The two deletion rows say the same thing about a stronger act, and the
    /// administrator cell is the one to read. Nobody may delete somebody else's
    /// history through this plugin, which follows from there being no elevated
    /// route to it at all rather than from a second rule about deletions.
    /// The second row is the statement issue #43 is about. An administrator is
    /// in it, and is refused by the same line as everybody else, because the
    /// elevated route to one person's history is the thing this plugin exists
    /// not to have.
    /// <para>
    /// The first row has no 403 in it and that is not an omission. Consent
    /// governs what other people may see about somebody and a person is not
    /// other people, so a caller reading their own rows is served whether or
    /// not they have consented and whether or not they have withdrawn. Issues
    /// #61 and #67 decided that and point here for the sentence.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A body agreeing to the wording this build ships.
    /// </summary>
    /// <remarks>
    /// Built from the wording rather than written as a number, so a row here
    /// cannot be the thing that goes stale when the words change. A body naming
    /// another version is refused by the endpoint, which is a different
    /// statement from the one this table makes and is asserted where the
    /// endpoint is driven.
    /// </remarks>
    public static readonly string Agreeing = string.Format(
        CultureInfo.InvariantCulture,
        "{{\"Agreed\":true,\"WordingVersion\":{0}}}",
        ConsentWording.Version);

    public static readonly IReadOnlyList<Row> Matrix =
    [
        new Row(
            Action: "YourYearController.GetYear",
            Method: "GET",
            Path: "/Stats/Users/{0}/Years/2025",
            RowsAskedFor: WhoseRows.TheCallersOwn,
            Anonymous: 401,
            Someone: 200,
            SomeoneElse: 200,
            Administrator: 200),
        new Row(
            Action: "YourYearController.GetYear",
            Method: "GET",
            Path: "/Stats/Users/{0}/Years/2025",
            RowsAskedFor: WhoseRows.SomebodyElses,
            Anonymous: 401,
            Someone: 403,
            SomeoneElse: 403,
            Administrator: 403),
        new Row(
            Action: "YourHistoryController.DeleteMyPlays",
            Method: "DELETE",
            Path: "/Stats/Users/{0}/Plays",
            RowsAskedFor: WhoseRows.TheCallersOwn,
            Anonymous: 401,
            Someone: 200,
            SomeoneElse: 200,
            Administrator: 200),
        new Row(
            Action: "YourHistoryController.DeleteMyPlays",
            Method: "DELETE",
            Path: "/Stats/Users/{0}/Plays",
            RowsAskedFor: WhoseRows.SomebodyElses,
            Anonymous: 401,
            Someone: 403,
            SomeoneElse: 403,
            Administrator: 403),
        new Row(
            Action: "YourConsentController.GetConsent",
            Method: "GET",
            Path: "/Stats/Users/{0}/Consent",
            RowsAskedFor: WhoseRows.TheCallersOwn,
            Anonymous: 401,
            Someone: 200,
            SomeoneElse: 200,
            Administrator: 200),
        new Row(
            Action: "YourConsentController.GetConsent",
            Method: "GET",
            Path: "/Stats/Users/{0}/Consent",
            RowsAskedFor: WhoseRows.SomebodyElses,
            Anonymous: 401,
            Someone: 403,
            SomeoneElse: 403,
            Administrator: 403),
        new Row(
            Action: "YourConsentController.SetConsent",
            Method: "PUT",
            Path: "/Stats/Users/{0}/Consent",
            RowsAskedFor: WhoseRows.TheCallersOwn,
            Anonymous: 401,
            Someone: 200,
            SomeoneElse: 200,
            Administrator: 200,
            Body: Agreeing),
        new Row(
            Action: "YourConsentController.SetConsent",
            Method: "PUT",
            Path: "/Stats/Users/{0}/Consent",
            RowsAskedFor: WhoseRows.SomebodyElses,
            Anonymous: 401,
            Someone: 403,
            SomeoneElse: 403,
            Administrator: 403,
            Body: Agreeing),
        new Row(
            Action: "AggregateReportsController.GetTopTitles",
            Method: "GET",
            Path: "/Stats/Reports/Top?from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z",
            RowsAskedFor: WhoseRows.NobodysInParticular,
            Anonymous: 401,
            Someone: 403,
            SomeoneElse: 403,
            Administrator: 200),
        new Row(
            Action: "AggregateReportsController.GetBreakdown",
            Method: "GET",
            Path: "/Stats/Reports/Breakdown?from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z",
            RowsAskedFor: WhoseRows.NobodysInParticular,
            Anonymous: 401,
            Someone: 403,
            SomeoneElse: 403,
            Administrator: 200)
    ];

    /// <summary>
    /// Which account a row's request names.
    /// </summary>
    public enum WhoseRows
    {
        /// <summary>The account making the request.</summary>
        TheCallersOwn,

        /// <summary>An account that is not the one making the request.</summary>
        SomebodyElses,

        /// <summary>
        /// No account at all, because the answer is about the server.
        /// </summary>
        /// <remarks>
        /// An aggregate names nobody, so a row about one has no account to put
        /// in its path and the two ordinary cells cannot be read as a statement
        /// about whose rows are whose. What such a row says is who may ask.
        /// </remarks>
        NobodysInParticular
    }

    /// <summary>
    /// Gets the table crossed with the four callers, one case per cell.
    /// </summary>
    public static TheoryData<int, string> EveryCell
    {
        get
        {
            var cells = new TheoryData<int, string>();

            for (var i = 0; i < Matrix.Count; i++)
            {
                foreach (var caller in Caller.All)
                {
                    cells.Add(i, caller.Name);
                }
            }

            return cells;
        }
    }

    /// <summary>
    /// Drives one cell of the table at the endpoint it is about.
    /// </summary>
    /// <param name="row">Which row of the table.</param>
    /// <param name="callerName">Which caller.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [MemberData(nameof(EveryCell))]
    public async Task EveryCellOfTheMatrixIsWhatTheEndpointAnswers(int row, string callerName)
    {
        var expected = Matrix[row];
        var caller = Caller.All.Single(shape => string.Equals(shape.Name, callerName, StringComparison.Ordinal));

        using var endpoints = new InProcessEndpoints();

        var answer = await endpoints.Send(expected.Method, expected.PathFor(caller), caller, expected.Body);

        Assert.Equal(expected.Expects(caller), answer.Status);
    }

    /// <summary>
    /// Fails when an action exists that the table does not list, so a new
    /// endpoint cannot be added without deciding its row.
    /// </summary>
    [Fact]
    public void EveryActionInTheAssemblyHasARowInTheMatrix()
    {
        var listed = Matrix.Select(row => row.Action).ToHashSet(StringComparer.Ordinal);
        var found = Actions().Select(Name).ToList();

        // The walk finding nothing is the trap this issue names: a reflection
        // test over an empty set reports no action missing from the table and
        // reads as met on a plugin with no endpoints at all.
        Assert.NotEmpty(found);

        foreach (var action in found)
        {
            Assert.Contains(action, listed);
        }
    }

    /// <summary>
    /// Fails when an action or its controller stops carrying an authorization
    /// attribute, or starts carrying one that lets anybody in.
    /// </summary>
    /// <remarks>
    /// The server configures no fallback policy, so an action with no attribute
    /// is reachable without authentication. That is a change nothing else here
    /// would report: the endpoint would go on answering every request it
    /// answers today, and only a caller who was supposed to be refused would
    /// see a difference.
    /// </remarks>
    [Fact]
    public void EveryActionIsBehindAnAuthorizationAttribute()
    {
        foreach (var action in Actions())
        {
            var wanted = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                || action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

            Assert.True(wanted, Name(action) + " carries no authorization attribute.");

            var open = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
                || action.DeclaringType!.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

            Assert.False(open, Name(action) + " lets an unauthenticated caller in.");
        }
    }

    private static IEnumerable<MethodInfo> Actions()
        => typeof(YourYearController).Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());

    private static string Name(MethodInfo action)
        => action.DeclaringType!.Name + "." + action.Name;

    /// <summary>
    /// One line of the table.
    /// </summary>
    /// <param name="Action">The controller and action this row is about.</param>
    /// <param name="Method">The request method.</param>
    /// <param name="Path">The path, with one placeholder for the account the request names.</param>
    /// <param name="RowsAskedFor">Whose rows the request names.</param>
    /// <param name="Anonymous">What a request carrying no authenticated caller gets.</param>
    /// <param name="Someone">What an ordinary signed-in user gets.</param>
    /// <param name="SomeoneElse">What a second ordinary signed-in user gets.</param>
    /// <param name="Administrator">What a signed-in administrator gets.</param>
    /// <param name="Body">The request body, where the request carries one.</param>
    public sealed record Row(
        string Action,
        string Method,
        string Path,
        WhoseRows RowsAskedFor,
        int Anonymous,
        int Someone,
        int SomeoneElse,
        int Administrator,
        string? Body = null)
    {
        /// <summary>
        /// The path one caller sends for this row.
        /// </summary>
        /// <param name="caller">Who is asking.</param>
        /// <returns>The path.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="caller"/> is <c>null</c>.</exception>
        public string PathFor(Caller caller)
        {
            ArgumentNullException.ThrowIfNull(caller);

            // Somebody else's rows are named by an account that is nobody's own
            // in this table, so the row means the same thing in every one of the
            // four cells. Naming one of the callers instead would make the cell
            // for that caller say "their own" while the row heading said
            // otherwise.
            var named = RowsAskedFor == WhoseRows.TheCallersOwn ? caller.UserId : SomebodyInParticular;

            // A path naming nobody carries no placeholder, so the account above
            // reaches nothing. It is still computed rather than branched around,
            // because a row that named an account in its path and declared
            // itself about nobody would be a row lying about its own subject,
            // and the format below is what would put the account there.
            return string.Format(CultureInfo.InvariantCulture, Path, named.ToString("D", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// What this row says one caller gets.
        /// </summary>
        /// <param name="caller">Who is asking.</param>
        /// <returns>The status code.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="caller"/> is <c>null</c>.</exception>
        public int Expects(Caller caller)
        {
            ArgumentNullException.ThrowIfNull(caller);

            if (caller == Caller.Anonymous)
            {
                return Anonymous;
            }

            if (caller == Caller.Someone)
            {
                return Someone;
            }

            if (caller == Caller.SomeoneElse)
            {
                return SomeoneElse;
            }

            return Administrator;
        }

        /// <summary>
        /// The account a row about somebody else's rows names.
        /// </summary>
        /// <remarks>
        /// An account none of the four callers is. An anonymous caller has no
        /// account of their own, so a row that named "the other caller" would
        /// have nothing to put in that cell and would quietly become a row
        /// about something else.
        /// </remarks>
        private static Guid SomebodyInParticular { get; } = new("99999999-9999-9999-9999-999999999999");
    }
}
