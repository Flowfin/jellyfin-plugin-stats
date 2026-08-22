// The reading side's separation of an open that failed from a read that threw.
//
// Issue #31's third condition asks the endpoints to answer that the plugin is
// unavailable rather than that the data is empty. The endpoint above a read can
// only do that if the two failures arrive as different things, and this is the
// one place that decides which is which.

using System;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// One read against a store, opened for it and closed after it.
/// </summary>
public class ReadFromTheStoreTests
{
    /// <summary>
    /// The answer comes back and the store is closed again.
    /// </summary>
    [Fact]
    public void TheStoreIsClosedOnceTheReadHasAnswered()
    {
        var store = new AStoreThatCounts();

        var answer = ReadFromTheStore.Answering(() => store, opened => opened.SaySomething());

        Assert.Equal("something", answer);
        Assert.Equal(1, store.Closed);
    }

    /// <summary>
    /// An open that fails arrives as this plugin's own type, carrying what the
    /// file system said underneath it rather than in place of it.
    /// </summary>
    [Fact]
    public void AnOpenThatFailsIsSaidToBeAnOpenThatFailed()
    {
        var underneath = new UnauthorizedAccessException("the folder");

        var refused = Assert.Throws<StoreCouldNotBeOpenedException>(
            () => ReadFromTheStore.Answering<AStoreThatCounts, string>(
                () => throw underneath,
                opened => opened.SaySomething()));

        Assert.Same(underneath, refused.InnerException);
    }

    /// <summary>
    /// A read that throws after the file is open comes out as itself, and the
    /// store is still closed.
    /// </summary>
    /// <remarks>
    /// This is the half that keeps the separation worth having. A damaged table
    /// and a defect in this plugin both arrive here, and reporting either of
    /// them as a store that could not be opened would put a broken plugin in
    /// front of an operator as a file that is briefly away.
    /// </remarks>
    [Fact]
    public void AReadThatThrowsIsNotAnOpenThatFailed()
    {
        var store = new AStoreThatCounts();

        Assert.Throws<NotSupportedException>(
            () => ReadFromTheStore.Answering<AStoreThatCounts, string>(
                () => store,
                _ => throw new NotSupportedException("a damaged table")));

        Assert.Equal(1, store.Closed);
    }

    /// <summary>
    /// What cannot be absent is refused where it is taken, rather than at the
    /// first line that would have used it.
    /// </summary>
    [Fact]
    public void WhatCannotBeAbsentIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => ReadFromTheStore.Answering<AStoreThatCounts, string>(null!, opened => opened.SaySomething()));

        Assert.Throws<ArgumentNullException>(
            () => ReadFromTheStore.Answering<AStoreThatCounts, string>(() => new AStoreThatCounts(), null!));
    }

    /// <summary>
    /// A store that is nothing but a record of having been closed.
    /// </summary>
    private sealed class AStoreThatCounts : IDisposable
    {
        /// <summary>
        /// Gets how many times this was closed.
        /// </summary>
        public int Closed { get; private set; }

        /// <summary>
        /// Stands in for a read.
        /// </summary>
        /// <returns>Something.</returns>
        public string SaySomething() => "something";

        /// <inheritdoc />
        public void Dispose() => Closed++;
    }
}
