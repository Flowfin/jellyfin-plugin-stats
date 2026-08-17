using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Stats.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// An operator who saves the settings page cannot see which of the values they
/// changed is already in force. A setting read once at start-up goes on behaving
/// the way it did while the page reports it saved, and the two look identical
/// from the dashboard, so the difference has to be written down where it is
/// edited rather than discovered by counting rows afterwards.
/// <para>
/// This holds the page and the model to the same answer. The model carries it
/// per field, in <see cref="TakesEffectAttribute"/>; the page carries it per
/// field, in a <c>data-takes-effect</c> attribute on the input that edits it.
/// Neither can move without the other, and a field added to the model with no
/// answer at all is refused by the first case below.
/// </para>
/// <para>
/// WHAT THIS CANNOT CHECK IS WHETHER AN ANSWER IS TRUE. Whether a consumer reads
/// a value at the moment it uses it is a fact about that consumer, and nothing
/// here reads one. A field declaring <see cref="WhenAChangeTakesEffect.AtOnce"/>
/// while something caches it passes every case in this file and is caught by a
/// reader; the greppable invariant <c>no-configuration-value-in-a-static-field</c>
/// refuses the one shape of that a grep can see. Issue #72.
/// </para>
/// <para>
/// NO FIELD ON THIS MODEL NEEDS A RESTART TODAY. The two cases below that are
/// about a restart therefore compare empty sets, and what carries them until a
/// field answers otherwise is the two cases that do not: every field is
/// classified, and the page offers exactly the fields an operator sets and says
/// the same thing about each one as the model does.
/// </para>
/// </summary>
public class ConfigurationRestartTests
{
    /// <summary>
    /// The sentence a field that needs a restart carries where it is edited.
    /// </summary>
    /// <remarks>
    /// One agreed sentence rather than a search for the word restart. A page is
    /// free to explain as much as it likes around it, and the check reads the
    /// one statement, so a description that mentions restarting for some other
    /// reason does not accidentally satisfy this.
    /// </remarks>
    private const string RestartSentence = "This takes effect when the server is restarted.";

    /// <summary>
    /// What the page writes for each answer the model can give. A member with no
    /// entry here fails rather than being skipped, so widening the enumeration
    /// is a decision about the page as well.
    /// </summary>
    private static readonly IReadOnlyDictionary<WhenAChangeTakesEffect, string> PageMarkers =
        new Dictionary<WhenAChangeTakesEffect, string>
        {
            [WhenAChangeTakesEffect.AtOnce] = "at-once",
            [WhenAChangeTakesEffect.OnRestart] = "on-restart"
        };

    /// <summary>
    /// Every field on the model says when a change to it takes effect. A setting
    /// added without that answer is the drift this file exists against: it
    /// reaches an operator with nothing anywhere saying whether saving it does
    /// anything before the next start.
    /// </summary>
    [Fact]
    public void EveryFieldOnTheModelSaysWhenAChangeToItTakesEffect()
    {
        var unanswered = Fields()
            .Where(field => field.GetCustomAttribute<TakesEffectAttribute>() is null)
            .Select(field => field.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unanswered);
    }

    /// <summary>
    /// The page offers exactly the fields the model calls settings. A setting the
    /// page does not offer cannot be changed without editing the stored file by
    /// hand, and an input for a field the model does not have is a control that
    /// silently does nothing.
    /// </summary>
    [Fact]
    public void ThePageOffersExactlyTheFieldsTheModelCallsSettings()
    {
        var settings = Fields()
            .Where(field => Answer(field) != WhenAChangeTakesEffect.NotASetting)
            .Select(field => field.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        var offered = Inputs()
            .Select(input => input.Field)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(settings, offered);
    }

    /// <summary>
    /// The page and the model agree, field by field, on when a change takes
    /// effect. This is the comparison the second condition of issue #72 asks
    /// for, and it fails in both directions: a page claiming a restart the model
    /// does not declare is as wrong as a model declaring one the page keeps
    /// quiet about.
    /// </summary>
    [Fact]
    public void ThePageAndTheModelAgreeOnWhenEachChangeTakesEffect()
    {
        var model = Fields()
            .Where(field => Answer(field) != WhenAChangeTakesEffect.NotASetting)
            .ToDictionary(field => field.Name, field => Marker(Answer(field)), StringComparer.Ordinal);

        foreach (var input in Inputs())
        {
            Assert.True(
                model.TryGetValue(input.Field, out var declared),
                "The page edits " + input.Field + ", which the configuration model does not offer as a setting.");

            Assert.Equal(declared, input.TakesEffect);
        }
    }

    /// <summary>
    /// A field that needs a restart says so where it is edited, and a field that
    /// does not never says it. The second half is what bites while no field needs
    /// one: a sentence copied onto a field that is already in force teaches an
    /// operator to restart the server for nothing, and to distrust the sentence
    /// where it is true.
    /// </summary>
    [Fact]
    public void ARestartIsNamedWhereTheFieldIsEditedAndNowhereElse()
    {
        foreach (var input in Inputs())
        {
            var saysSo = Words(input.Field).Contains(RestartSentence, StringComparison.Ordinal);

            if (string.Equals(input.TakesEffect, PageMarkers[WhenAChangeTakesEffect.OnRestart], StringComparison.Ordinal))
            {
                Assert.True(
                    saysSo,
                    input.Field + " is edited on the page as needing a restart and the words beside it do not say so. The sentence to write is: " + RestartSentence);
            }
            else
            {
                Assert.False(
                    saysSo,
                    input.Field + " takes effect at once and the words beside it tell an operator to restart the server.");
            }
        }
    }

    /// <summary>
    /// What a field on the page declares.
    /// </summary>
    /// <param name="Field">The name of the field on the configuration model.</param>
    /// <param name="TakesEffect">The value of its data-takes-effect attribute.</param>
    private sealed record Input(string Field, string TakesEffect);

    /// <summary>
    /// The fields of the configuration model, as a caller of the type sees them.
    /// </summary>
    /// <remarks>
    /// The same population ConfigurationReferenceTests compares the reference
    /// document against, and for the same reason: an inherited field is still a
    /// field the stored file carries, and a read-only one is still something the
    /// page can show. Reading them the same way keeps the two checks from
    /// disagreeing about what a field is.
    /// </remarks>
    /// <returns>The public instance properties of the configuration model.</returns>
    private static IEnumerable<PropertyInfo> Fields()
    {
        return typeof(PluginConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance);
    }

    /// <summary>
    /// Reads a field's answer off the model.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>When a change to that field takes effect.</returns>
    private static WhenAChangeTakesEffect Answer(PropertyInfo field)
    {
        var attribute = field.GetCustomAttribute<TakesEffectAttribute>();

        Assert.True(
            attribute is not null,
            field.Name + " on the configuration model does not say when a change to it takes effect.");

        return attribute!.When;
    }

    /// <summary>
    /// Says what the page writes for an answer.
    /// </summary>
    /// <param name="when">The answer the model gives.</param>
    /// <returns>The value the page's data-takes-effect attribute carries.</returns>
    private static string Marker(WhenAChangeTakesEffect when)
    {
        Assert.True(
            PageMarkers.TryGetValue(when, out var marker),
            "The model can answer " + when + " and this check has no idea what the page writes for it. A new answer is a decision about the page as well as about the model.");

        return marker!;
    }

    /// <summary>
    /// The inputs the settings page offers, with what each one declares.
    /// </summary>
    /// <remarks>
    /// The elements are read out of the page rather than parsed as a document.
    /// The page is served as HTML and never as XML, so a reader that insists on
    /// well-formed XML would fail on a page a browser is perfectly happy with,
    /// and it would fail with a parse error rather than with the thing this file
    /// is about.
    /// </remarks>
    /// <returns>One entry per input carrying a name.</returns>
    private static IReadOnlyList<Input> Inputs()
    {
        var inputs = Elements()
            .Select(element => new
            {
                Name = Attribute(element, "name"),
                TakesEffect = Attribute(element, "data-takes-effect")
            })
            .Where(element => element.Name is not null)
            .Select(element =>
            {
                Assert.True(
                    element.TakesEffect is not null,
                    "The page edits " + element.Name + " and does not say when a change to it takes effect. The attribute to write is data-takes-effect.");

                return new Input(element.Name!, element.TakesEffect!);
            })
            .ToList();

        Assert.NotEmpty(inputs);
        return inputs;
    }

    /// <summary>
    /// The words a reader meets beside a field: everything between its input and
    /// the next one, or the end of the form for the last.
    /// </summary>
    /// <remarks>
    /// A convention this check imposes on the page, and the page follows it
    /// today: the label and the description of a field sit after the input that
    /// edits it. It fails closed. A description moved above its input would
    /// leave the sentence unfound and redden this rather than passing quietly.
    /// </remarks>
    /// <param name="field">The name of the field.</param>
    /// <returns>The page text beside that field.</returns>
    private static string Words(string field)
    {
        var page = Page();
        var element = Elements().FirstOrDefault(candidate => string.Equals(Attribute(candidate, "name"), field, StringComparison.Ordinal));

        Assert.True(element is not null, "The page has no input for " + field + ".");

        var from = page.IndexOf(element!, StringComparison.Ordinal) + element!.Length;
        var next = page.IndexOf("<input", from, StringComparison.Ordinal);
        var form = page.IndexOf("</form>", from, StringComparison.Ordinal);
        var to = next >= 0 && next < form ? next : form;

        Assert.True(to > from, "The input for " + field + " is outside the form on the settings page.");

        return page[from..to];
    }

    /// <summary>
    /// Every input element on the page, as it is written.
    /// </summary>
    /// <returns>The text of each input element.</returns>
    private static IReadOnlyList<string> Elements()
    {
        return Regex.Matches(Page(), "<input\\b[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(match => match.Value)
            .ToList();
    }

    /// <summary>
    /// Reads one attribute off an element.
    /// </summary>
    /// <param name="element">The text of the element.</param>
    /// <param name="attribute">The attribute name.</param>
    /// <returns>Its value, or null where the element does not carry it.</returns>
    private static string? Attribute(string element, string attribute)
    {
        var found = Regex.Match(
            element,
            "\\b" + Regex.Escape(attribute) + "\\s*=\\s*\"([^\"]*)\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        return found.Success ? found.Groups[1].Value : null;
    }

    /// <summary>
    /// The settings page, read out of the assembly that ships it.
    /// </summary>
    /// <remarks>
    /// The embedded copy rather than the file beside it, because that is the one
    /// a server serves. PageAssetTests holds the two byte for byte identical, so
    /// this reads the tracked page as well without a second route to it.
    /// </remarks>
    /// <returns>The text of the settings page.</returns>
    private static string Page()
    {
        var assembly = typeof(PluginConfiguration).Assembly;
        var name = typeof(PluginConfiguration).Namespace + ".configPage.html";

        using var stream = assembly.GetManifestResourceStream(name);
        Assert.True(stream is not null, "The settings page is not embedded under " + name + ".");

        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
