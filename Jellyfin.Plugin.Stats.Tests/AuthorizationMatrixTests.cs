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
    /// endpoint                                | rows asked for | anonymous | an ordinary user | a different ordinary user | an administrator
    /// GET /Stats/Users/{userId}/Years/{year}  | their own      | 401       | 200              | 200                       | 200
    /// GET /Stats/Users/{userId}/Years/{year}  | somebody else's| 401       | 403              | 403                       | 403
    /// </code>
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
            Administrator: 403)
    ];

    /// <summary>
    /// Which account a row's request names.
    /// </summary>
    public enum WhoseRows
    {
        /// <summary>The account making the request.</summary>
        TheCallersOwn,

        /// <summary>An account that is not the one making the request.</summary>
        SomebodyElses
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

        var answer = await endpoints.Get(expected.PathFor(caller), caller);

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
    public sealed record Row(
        string Action,
        string Method,
        string Path,
        WhoseRows RowsAskedFor,
        int Anonymous,
        int Someone,
        int SomeoneElse,
        int Administrator)
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
