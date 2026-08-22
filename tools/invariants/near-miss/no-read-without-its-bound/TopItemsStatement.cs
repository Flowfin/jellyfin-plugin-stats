// Near miss for no-read-without-its-bound.
//
// A top list, added to the store the same way as everything beside it, and it
// says nothing about how many rows it can come back with. On the machine it was
// written on it answers with the twenty films in the developer's library. On a
// server that has been recording for three years it answers with every distinct
// item anybody ever played, and the caller that asked for a top ten holds all
// of them in memory to sort them.
//
// The three statements in this tree that legitimately carry no limit are not
// this shape, and each of them says why on its first line. This one has nothing
// to say and does not say it, which is the difference the rule reads.
//
// This file is not compiled. It exists so the rule that refuses it can be shown
// to bite.

private const string SelectTheMostWatchedItems =
    @"SELECT ItemId, ItemName, SUM(WatchedDurationTicks) AS Watched
      FROM plays
      WHERE StartedUtcTicks >= $from
      GROUP BY ItemId
      ORDER BY Watched DESC";
