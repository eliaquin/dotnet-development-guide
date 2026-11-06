using System.Globalization;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace Store.Infrastructure.Identity;

public sealed class DapperUserStore(SqliteConnectionFactory connectionFactory, IdentityErrorDescriber errors) :
    IUserPasswordStore<StoreUser>, IUserRoleStore<StoreUser>, IUserLockoutStore<StoreUser>,
    IUserSecurityStampStore<StoreUser>
{
    private const string UserColumns = "Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, ConcurrencyStamp, AccessFailedCount, LockoutEnabled, LockoutEnd";
    private bool _disposed;

    public bool HasUsers()
    {
        Check(CancellationToken.None);
        using var connection = connectionFactory.OpenConnection();
        return connection.ExecuteScalar<long>("SELECT EXISTS (SELECT 1 FROM Users);") != 0;
    }

    public Task<IdentityResult> CreateAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        var validation = ValidateForPersistence(user);
        if (!validation.Succeeded)
        {
            return Task.FromResult(validation);
        }

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var newStamp = Guid.NewGuid().ToString();
        try
        {
            connection.Execute(
                """
                INSERT INTO Users
                    (Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, ConcurrencyStamp,
                     AccessFailedCount, LockoutEnabled, LockoutEnd)
                VALUES
                    (@Id, @UserName, @NormalizedUserName, @PasswordHash, @SecurityStamp, @ConcurrencyStamp,
                     @AccessFailedCount, @LockoutEnabled, @LockoutEnd);
                """, Parameters(user, newStamp), transaction);
            PersistRoles(connection, transaction, user);
            transaction.Commit();
            user.ConcurrencyStamp = newStamp;
            return Task.FromResult(IdentityResult.Success);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            return Task.FromResult(IdentityResult.Failed(errors.DuplicateUserName(user.UserName!)));
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 1555)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateUserId",
                Description = "Ya existe una cuenta con ese identificador."
            }));
        }
    }

    public Task<IdentityResult> UpdateAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        var validation = ValidateForPersistence(user);
        if (!validation.Succeeded)
        {
            return Task.FromResult(validation);
        }

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var newStamp = Guid.NewGuid().ToString();
        try
        {
            var affected = connection.Execute(
                """
                UPDATE Users SET UserName = @UserName, NormalizedUserName = @NormalizedUserName,
                    PasswordHash = @PasswordHash, SecurityStamp = @SecurityStamp,
                    ConcurrencyStamp = @ConcurrencyStamp, AccessFailedCount = @AccessFailedCount,
                    LockoutEnabled = @LockoutEnabled, LockoutEnd = @LockoutEnd
                WHERE Id = @Id AND ConcurrencyStamp = @OriginalConcurrencyStamp;
                """, Parameters(user, newStamp), transaction);
            if (affected == 0)
            {
                return Task.FromResult(IdentityResult.Failed(errors.ConcurrencyFailure()));
            }

            connection.Execute("DELETE FROM UserRoles WHERE UserId = @Id;", new { user.Id }, transaction);
            PersistRoles(connection, transaction, user);
            transaction.Commit();
            user.ConcurrencyStamp = newStamp;
            return Task.FromResult(IdentityResult.Success);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            return Task.FromResult(IdentityResult.Failed(errors.DuplicateUserName(user.UserName!)));
        }
    }

    public Task<IdentityResult> DeleteAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        using var connection = connectionFactory.OpenConnection();
        var affected = connection.Execute("DELETE FROM Users WHERE Id = @Id AND ConcurrencyStamp = @ConcurrencyStamp;",
            new { user.Id, user.ConcurrencyStamp });
        return Task.FromResult(affected == 1 ? IdentityResult.Success : IdentityResult.Failed(errors.ConcurrencyFailure()));
    }

    public Task<StoreUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return Task.FromResult(FindUser("Id", userId));
    }

    public Task<StoreUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUserName);
        return Task.FromResult(FindUser("NormalizedUserName", normalizedUserName));
    }

    public Task<string> GetUserIdAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        return Task.FromResult(user.Id);
    }

    public Task<string?> GetUserNameAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        return Task.FromResult(user.UserName);
    }

    public Task SetUserNameAsync(StoreUser user, string? userName, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        return Task.FromResult(user.NormalizedUserName);
    }

    public Task SetNormalizedUserNameAsync(StoreUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task SetPasswordHashAsync(StoreUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        return Task.FromResult(user.PasswordHash);
    }

    public Task<bool> HasPasswordAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        return Task.FromResult(user.PasswordHash is not null);
    }

    public Task AddToRoleAsync(StoreUser user, string roleName, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        var role = StoreRoles.FromNormalized(roleName)
            ?? throw new ArgumentException("El rol no está permitido.", nameof(roleName));
        if (!user.Roles.Contains(role, StringComparer.Ordinal))
        {
            user.Roles.Add(role);
        }

        return Task.CompletedTask;
    }

    public Task RemoveFromRoleAsync(StoreUser user, string roleName, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        var role = StoreRoles.FromNormalized(roleName);
        if (role is not null)
        {
            user.Roles.Remove(role);
        }

        return Task.CompletedTask;
    }

    public Task<IList<string>> GetRolesAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        return Task.FromResult<IList<string>>(user.Roles.Distinct(StringComparer.Ordinal).ToArray());
    }

    public Task<bool> IsInRoleAsync(StoreUser user, string roleName, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        var role = StoreRoles.FromNormalized(roleName);
        return Task.FromResult(role is not null && user.Roles.Contains(role, StringComparer.Ordinal));
    }

    public Task<IList<StoreUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        var role = StoreRoles.FromNormalized(roleName);
        if (role is null)
        {
            return Task.FromResult<IList<StoreUser>>([]);
        }

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        var rows = connection.Query<UserRow>(
            $"SELECT {UserColumns} FROM Users WHERE Id IN (SELECT UserId FROM UserRoles WHERE RoleName = @Role);",
            new { Role = role }, transaction).ToArray();
        var rolesByUser = connection.Query<UserRoleRow>(
            """
            SELECT UserId, RoleName FROM UserRoles
            WHERE UserId IN (SELECT UserId FROM UserRoles WHERE RoleName = @Role);
            """, new { Role = role }, transaction).ToLookup(row => row.UserId, row => row.RoleName, StringComparer.Ordinal);
        IList<StoreUser> users = rows.Select(row => ToUser(row, rolesByUser[row.Id])).ToArray();
        transaction.Commit();
        return Task.FromResult(users);
    }

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        return Task.FromResult(user.LockoutEnd);
    }

    public Task SetLockoutEndDateAsync(StoreUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        user.LockoutEnd = lockoutEnd?.ToUniversalTime();
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        user.AccessFailedCount = checked(user.AccessFailedCount + 1);
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task<bool> GetLockoutEnabledAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        return Task.FromResult(user.LockoutEnabled);
    }

    public Task SetLockoutEnabledAsync(StoreUser user, bool enabled, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task SetSecurityStampAsync(StoreUser user, string stamp, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(StoreUser user, CancellationToken cancellationToken)
    {
        Check(user, cancellationToken);
        return Task.FromResult(user.SecurityStamp);
    }

    public void Dispose() => _disposed = true;

    private StoreUser? FindUser(string column, string value)
    {
        // Both identifiers come exclusively from the two fixed callers above.
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        var row = connection.QuerySingleOrDefault<UserRow>(
            $"SELECT {UserColumns} FROM Users WHERE {column} = @Value;", new { Value = value }, transaction);
        var user = row is null ? null : ToUser(row, connection.Query<string>(
            "SELECT RoleName FROM UserRoles WHERE UserId = @Id ORDER BY RoleName;", new { row.Id }, transaction));
        transaction.Commit();
        return user;
    }

    private static StoreUser ToUser(UserRow row, IEnumerable<string> roles)
    {
        var user = new StoreUser
        {
            Id = row.Id,
            UserName = row.UserName,
            NormalizedUserName = row.NormalizedUserName,
            PasswordHash = row.PasswordHash,
            SecurityStamp = row.SecurityStamp,
            ConcurrencyStamp = row.ConcurrencyStamp,
            AccessFailedCount = checked((int)row.AccessFailedCount),
            LockoutEnabled = row.LockoutEnabled != 0,
            LockoutEnd = row.LockoutEnd is null ? null : DateTimeOffset.ParseExact(
                row.LockoutEnd, "O", CultureInfo.InvariantCulture, DateTimeStyles.None)
        };
        user.Roles.AddRange(roles);
        return user;
    }

    private static object Parameters(StoreUser user, string newStamp) => new
    {
        user.Id,
        user.UserName,
        user.NormalizedUserName,
        user.PasswordHash,
        user.SecurityStamp,
        ConcurrencyStamp = newStamp,
        OriginalConcurrencyStamp = user.ConcurrencyStamp,
        user.AccessFailedCount,
        user.LockoutEnabled,
        LockoutEnd = user.LockoutEnd?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
    };

    private static void PersistRoles(SqliteConnection connection, SqliteTransaction transaction, StoreUser user)
    {
        foreach (var role in user.Roles.Distinct(StringComparer.Ordinal))
        {
            connection.Execute("INSERT INTO UserRoles (UserId, RoleName) VALUES (@Id, @Role);",
                new { user.Id, Role = role }, transaction);
        }
    }

    private IdentityResult ValidateForPersistence(StoreUser user)
    {
        if (string.IsNullOrWhiteSpace(user.UserName) || user.UserName.Length > 50 ||
            string.IsNullOrWhiteSpace(user.NormalizedUserName) || user.NormalizedUserName.Length > 50)
        {
            return IdentityResult.Failed(errors.InvalidUserName(user.UserName ?? string.Empty));
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash) || string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequired",
                Description = "La cuenta requiere contraseña y sello de seguridad."
            });
        }

        if (user.Roles.Any(role => !StoreRoles.IsValid(role)))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidRole",
                Description = "El rol no está permitido."
            });
        }

        return IdentityResult.Success;
    }

    private void Check(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Check(StoreUser user, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        ArgumentNullException.ThrowIfNull(user);
    }

    private sealed record UserRow(
        string Id, string UserName, string NormalizedUserName, string PasswordHash,
        string SecurityStamp, string ConcurrencyStamp, long AccessFailedCount, long LockoutEnabled, string? LockoutEnd);

    private sealed record UserRoleRow(string UserId, string RoleName);
}
