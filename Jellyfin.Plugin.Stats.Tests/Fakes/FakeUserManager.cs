// A user manager the tests own. It answers the lookups this plugin needs to
// turn a user id on an event into a user, and refuses everything else, for the
// same reason FakeSessionManager does: a member that starts answering is a
// widening of the surface and shows up in a diff as one.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// An <see cref="IUserManager"/> over a fixed set of users.
/// </summary>
public sealed class FakeUserManager : IUserManager
{
    private readonly List<User> _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeUserManager"/> class.
    /// </summary>
    /// <param name="users">The users this manager knows about.</param>
    public FakeUserManager(params User[] users)
    {
        _users = new List<User>(users ?? Array.Empty<User>());
    }

    /// <inheritdoc />
    public event EventHandler<GenericEventArgs<User>>? OnUserUpdated;

    /// <summary>
    /// Makes a user with a stable id, so a test that wants two plays by the
    /// same person does not have to build the entity twice.
    /// </summary>
    /// <param name="name">The user name.</param>
    /// <param name="id">The user id. A new one is made when this is omitted.</param>
    /// <returns>The user.</returns>
    public static User NewUser(string name, Guid? id = null)
    {
        return new User(name, "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider", "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider")
        {
            Id = id ?? Guid.NewGuid()
        };
    }

    /// <summary>
    /// Raises the user updated event, which the server raises when a user's
    /// record changes.
    /// </summary>
    /// <param name="user">The user that changed.</param>
    public void RaiseUserUpdated(User user)
    {
        OnUserUpdated?.Invoke(this, new GenericEventArgs<User>(user));
    }

    /// <inheritdoc />
    public IEnumerable<User> GetUsers() => _users;

    /// <inheritdoc />
    public IEnumerable<Guid> GetUsersIds() => _users.Select(user => user.Id);

    /// <summary>
    /// Gets or sets what a lookup throws, per identifier, or null for a lookup
    /// that answers.
    /// </summary>
    /// <remarks>
    /// A user manager that could not answer is not a user manager saying nobody
    /// is there, and code that reconciles against it decides whether rows are
    /// deleted on exactly that difference. It is a hook here rather than a
    /// second fake, because what a test needs is a manager that answers for
    /// some identifiers and fails for one.
    /// </remarks>
    public Func<Guid, Exception?>? Failing { get; set; }

    /// <inheritdoc />
    public User? GetUserById(Guid id)
    {
        var failure = Failing?.Invoke(id);
        if (failure is not null)
        {
            throw failure;
        }

        return _users.Find(user => user.Id.Equals(id));
    }

    /// <inheritdoc />
    public User? GetFirstUser() => _users.Count == 0 ? null : _users[0];

    /// <inheritdoc />
    public User? GetUserByName(string name)
        => _users.Find(user => string.Equals(user.Username, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Removes a user from the set this manager knows about, which is what a
    /// deleted user looks like to code that only reads.
    /// </summary>
    /// <param name="id">The user id.</param>
    /// <returns>Whether a user was removed.</returns>
    public bool Forget(Guid id) => _users.RemoveAll(user => user.Id.Equals(id)) > 0;

    // Everything below writes, authenticates or reaches a database, and none of
    // it is surface this plugin reads.
    private static Exception NotPartOfTheSurface([System.Runtime.CompilerServices.CallerMemberName] string member = "")
        => new NotSupportedException(
            member + " is not part of the server surface this plugin reads. Add it to FakeUserManager only with the code that needs it.");

    /// <inheritdoc />
    public Task InitializeAsync() => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task RenameUser(Guid userId, string oldName, string newName) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task UpdateUserAsync(User user) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task<User> CreateUserAsync(string name) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task DeleteUserAsync(Guid userId) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task ResetPassword(Guid userId) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task ChangePassword(Guid userId, string newPassword) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public UserDto GetUserDto(User user, string? remoteEndPoint = null) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task<User?> AuthenticateUser(string username, string password, string remoteEndPoint, bool isUserSession)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task<ForgotPasswordResult> StartForgotPasswordProcess(string enteredUsername, bool isInNetwork)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task<PinRedeemResult> RedeemPasswordResetPin(string pin) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public NameIdPair[] GetAuthenticationProviders() => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public NameIdPair[] GetPasswordResetProviders() => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task UpdateConfigurationAsync(Guid userId, UserConfiguration config) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task UpdatePolicyAsync(Guid userId, UserPolicy policy) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task ClearProfileImageAsync(User user) => throw NotPartOfTheSurface();
}
