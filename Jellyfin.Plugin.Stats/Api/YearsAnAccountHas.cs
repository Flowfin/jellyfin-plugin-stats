using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// Reads which calendar years one account has plays in.
/// </summary>
/// <remarks>
/// A function rather than a store, for the reason the folded year beside it is
/// one: <c>no-store-write-outside-the-write-path</c> refuses the store's
/// interface being named outside the write path, and a controller that named it
/// would be reaching the rows itself. What is handed in instead is a function
/// that opens the store, reads and closes it, supplied where the plugin is
/// assembled, which is the one place that knows where the data folder is.
/// <para>
/// The zone is part of the question and never applied afterwards. A calendar
/// year has a local midnight at each end, so a play in the last hours of
/// December belongs to one year or the next depending on whose midnight is
/// meant. Issue #67.
/// </para>
/// </remarks>
/// <param name="userId">The account whose years are wanted.</param>
/// <param name="zone">The zone the years are read in, which is what decides where one ends and the next begins.</param>
/// <returns>Each year once, oldest first, and empty where that account has no rows.</returns>
public delegate IReadOnlyList<int> YearsAnAccountHas(Guid userId, TimeZoneInfo zone);
